using System.Runtime.InteropServices;

namespace Jexel.App.Services;

/// <summary>
/// 基于 Win32 通用文件对话框（IFileOpenDialog）的原生选择器。
/// 相比 UWP 选择器，它在未打包（unpackaged）的桌面应用中行为完全一致，
/// 支持多选、文件夹选择、初始目录以及自定义过滤器。
/// </summary>
internal static class NativeFileDialog
{
    private const uint FosPickFolders = 0x0000_0020;
    private const uint FosForceFileSystem = 0x0000_0040;
    private const uint FosAllowMultiSelect = 0x0000_0200;
    private const uint FosPathMustExist = 0x0000_0800;
    private const uint FosFileMustExist = 0x0000_1000;

    private const int HResultCanceled = unchecked((int)0x800704C7);
    private const uint SigdnFileSysPath = 0x80058000;

    private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

    /// <summary>打开"选择图像"对话框，返回选中的文件路径；用户取消时返回空列表。</summary>
    /// <param name="owner">宿主窗口句柄。</param>
    /// <param name="filter">Win32 过滤器字符串，格式为 "名称|模式|名称|模式"。</param>
    /// <param name="initialFolder">初始目录，可为空。</param>
    /// <param name="title">对话框标题。</param>
    public static IReadOnlyList<string> PickFiles(
        IntPtr owner,
        string filter,
        string? initialFolder = null,
        string title = "选择图像文件")
    {
        var dialog = CreateDialog();
        try
        {
            SetCommonOptions(dialog, allowMultiSelect: true);
            dialog.SetTitle(title);
            ApplyFilter(dialog, filter);
            ApplyInitialFolder(dialog, initialFolder);

            var hr = dialog.Show(owner);
            if (hr == HResultCanceled)
            {
                return Array.Empty<string>();
            }

            Marshal.ThrowExceptionForHR(hr);

            dialog.GetResults(out var items);
            return ReadAllPaths(items);
        }
        catch (COMException)
        {
            return Array.Empty<string>();
        }
        finally
        {
            ReleaseComObject(dialog);
        }
    }

    /// <summary>打开"选择文件夹"对话框；用户取消时返回 null。</summary>
    /// <param name="owner">宿主窗口句柄。</param>
    /// <param name="initialFolder">初始目录，可为空。</param>
    /// <param name="title">对话框标题。</param>
    public static string? PickFolder(IntPtr owner, string? initialFolder = null, string title = "选择文件夹")
    {
        var dialog = CreateDialog();
        try
        {
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FosPickFolders | FosForceFileSystem | FosPathMustExist);
            dialog.SetTitle(title);
            ApplyInitialFolder(dialog, initialFolder);

            var hr = dialog.Show(owner);
            if (hr == HResultCanceled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var item);
            try
            {
                return ReadPath(item);
            }
            finally
            {
                ReleaseComObject(item);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            ReleaseComObject(dialog);
        }
    }

    private static IFileOpenDialog CreateDialog()
    {
        var type = Type.GetTypeFromCLSID(ClsidFileOpenDialog, throwOnError: true)!;
        return (IFileOpenDialog)Activator.CreateInstance(type)!;
    }

    private static void SetCommonOptions(IFileOpenDialog dialog, bool allowMultiSelect)
    {
        dialog.GetOptions(out var options);

        // FOS_FORCEFILESYSTEM 保证返回的是真实文件系统路径，而不是虚拟项。
        options |= FosForceFileSystem | FosFileMustExist | FosPathMustExist;

        if (allowMultiSelect)
        {
            options |= FosAllowMultiSelect;
        }
        else
        {
            options &= ~FosAllowMultiSelect;
        }

        dialog.SetOptions(options);
    }

    private static void ApplyFilter(IFileOpenDialog dialog, string filter)
    {
        var specs = ParseFilter(filter);
        if (specs.Length > 0)
        {
            dialog.SetFileTypes((uint)specs.Length, specs);
        }
    }

    /// <summary>把 "名称|模式|名称|模式" 解析为 COM 过滤器结构。</summary>
    private static ComDlgFilterSpec[] ParseFilter(string filter)
    {
        var parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var specs = new List<ComDlgFilterSpec>(parts.Length / 2);

        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            specs.Add(new ComDlgFilterSpec
            {
                Name = parts[i],
                Spec = parts[i + 1],
            });
        }

        return specs.ToArray();
    }

    private static void ApplyInitialFolder(IFileOpenDialog dialog, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        if (SHCreateItemFromParsingName(folder, IntPtr.Zero, typeof(IShellItem).GUID, out var item) == 0 && item is not null)
        {
            try
            {
                dialog.SetFolder(item);
            }
            finally
            {
                ReleaseComObject(item);
            }
        }
    }

    private static IReadOnlyList<string> ReadAllPaths(IShellItemArray array)
    {
        var paths = new List<string>();
        try
        {
            array.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                array.GetItemAt(i, out var item);
                try
                {
                    var path = ReadPath(item);
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }
        }
        finally
        {
            ReleaseComObject(array);
        }

        return paths;
    }

    private static string? ReadPath(IShellItem? item)
    {
        if (item is null)
        {
            return null;
        }

        if (item.GetDisplayName(SigdnFileSysPath, out var pointer) != 0 || pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.ReleaseComObject(instance);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItem? ppv);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ComDlgFilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Spec;
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] ComDlgFilterSpec[] rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
    }

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog : IFileDialog
    {
        [PreserveSig]
        new int Show(IntPtr parent);

        new void SetFileTypes(uint cFileTypes, [MarshalAs(UnmanagedType.LPArray)] ComDlgFilterSpec[] rgFilterSpec);
        new void SetFileTypeIndex(uint iFileType);
        new void GetFileTypeIndex(out uint piFileType);
        new void Advise(IntPtr pfde, out uint pdwCookie);
        new void Unadvise(uint dwCookie);
        new void SetOptions(uint fos);
        new void GetOptions(out uint pfos);
        new void SetDefaultFolder(IShellItem psi);
        new void SetFolder(IShellItem psi);
        new void GetFolder(out IShellItem ppsi);
        new void GetCurrentSelection(out IShellItem ppsi);
        new void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        new void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        new void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        new void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        new void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        new void GetResult(out IShellItem ppsi);
        new void AddPlace(IShellItem psi, int fdap);
        new void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        new void Close(int hr);
        new void SetClientGuid(ref Guid guid);
        new void ClearClientData();
        new void SetFilter(IntPtr pFilter);

        void GetResults(out IShellItemArray ppenum);
        void GetSelectedItems(out IShellItemArray ppsai);
    }
}

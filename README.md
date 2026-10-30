# Jexel — JPEG XL 转换器

基于 WinUI 3 的 Windows 桌面工具，专注于把常见格式的图像转换为 **JPEG XL**（`.jxl`）。
界面遵循 Windows 11 设计规范（Mica 材质、Fluent 控件、Segoe Fluent 图标、圆角卡片布局）。

---

## 功能

### 压缩
| 项目 | 说明 |
| --- | --- |
| 无损模式 | 解码结果与源图像**逐像素完全一致**（含 alpha 通道、16 位深、灰度） |
| 有损模式 | 质量 1–99 可调，体积随质量单调变化 |
| 编码强度 | 1–9，数值越高体积越小、耗时越长（默认 7） |
| 色彩配置 | 可保留源文件内嵌的 ICC 配置文件（默认保留）；关闭可减小体积，但非 sRGB 图像颜色会偏移 |

### 导入（三种方式）
- **拖拽**：把文件或文件夹直接拖到窗口任意位置
- **添加文件**：原生多选对话框，支持全部受支持的格式
- **添加文件夹**：扫描整个文件夹并自动筛出可转换的图像

### 输出（两种方式）
- **指定输出文件夹**：所有结果集中输出
- **原地输出**：与源文件同目录。因为扩展名由原格式变为 `.jxl`，不会与源文件重名

目标文件已存在时可选：**自动重命名**（默认，追加 `(1)`、`(2)`…）/ 跳过 / 覆盖。

### 对比与检查
- 每行采用**左右对比**布局：左侧输入（格式 · 尺寸 · 体积）⇄ 右侧输出（体积 · 体积变化百分比）
- 每行提供**单独的打开按钮**，可用系统默认程序直接打开源文件或转换后的文件，直观比对画质
- 底部实时汇总**输入总计 → 输出总计**与整体节省比例

---

## 下载

从 [Releases](../../releases) 页面下载 `Jexel-<版本>-win-x64.zip`，解压后直接运行 `Jexel.exe`。
发布包是**自包含**的（内含 .NET 运行时与 Windows App SDK），无需再安装任何运行时。

> 未做代码签名，首次运行时 Windows SmartScreen 可能提示"未知发布者"，
> 选择「更多信息 → 仍要运行」即可。

## 系统要求

- Windows 10 1809（17763）或更高版本，推荐 Windows 11
- 从源码构建需要 [.NET SDK 9.0.100+](https://dotnet.microsoft.com/download/dotnet/9.0)
  —— 解决方案使用 `.slnx` 格式，旧版 SDK 无法识别；仓库中的 `global.json` 已声明该下限
- 若使用**框架依赖**方式构建（README 中的默认 `dotnet build` / `dotnet run`），另需
  [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0) 与
  [Windows App Runtime 1.8](https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe)

---

## 构建与运行

```powershell
# 还原并构建（解决方案与单项目产出一致，输出到 bin\<Configuration>\...）
dotnet build Jexel.slnx -c Release

# 直接运行
dotnet run --project src/Jexel.App/Jexel.App.csproj

# 发布可分发目录（框架依赖，体积小，但目标机器需安装运行时）
dotnet publish src/Jexel.App/Jexel.App.csproj -c Release

# 自包含发布（与 Releases 中的分发包一致，解压即用）
dotnet publish src/Jexel.App/Jexel.App.csproj -c Release -r win-x64 -p:Platform=x64 `
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -o publish/win-x64
```

可执行文件位于：

```
src/Jexel.App/bin/<Configuration>/net8.0-windows10.0.19041.0/win-x64/Jexel.exe
```

应用以普通用户权限运行（`asInvoker`）——这是拖放功能正常工作的前提。

### 自检

仓库附带一个覆盖核心转换链路的端到端测试：

```powershell
dotnet run --project tests/Jexel.SmokeTest/Jexel.SmokeTest.csproj
```

它验证 39 项断言，包括：无损 bit-exact 往返、有损体积单调性、原地输出、重名策略、
源文件哈希不变、损坏文件隔离、取消响应、ICC 保留开关、临时文件清理与扫描排除等。

上述构建与测试会在每次 push / PR 时由 GitHub Actions 自动执行（见 `.github/workflows/build.yml`）；
推送 `v*` 标签会触发 `.github/workflows/release.yml`，自动执行冒烟测试、构建自包含分发包并创建 Release。

---

## 技术实现

### 转换引擎
图像编解码完全交给 **[Magick.NET](https://github.com/dlemstra/Magick.NET)**（ImageMagick 的 .NET 封装，
内嵌 libjxl）。当前构建可读取 **284 种格式**，包含 PNG / JPEG / WebP / AVIF / TIFF / GIF / BMP /
PSD / SVG / EXR / QOI / JPEG 2000 等，无需任何外部进程或命令行工具。

参数映射经过像素级实测确认：

| 模式 | 实现 |
| --- | --- |
| 无损 | `Quality = 100`（触发 libjxl 无损编码，实测与源图 bit-exact） |
| 有损 | `Quality = 1..99` |
| 编码强度 | `jxl:effort = 1..9` |
| ICC 保留 | 关闭时 `RemoveProfile("icc")`，像素不受影响，仅丢弃色彩配置文件 |

> 注：ImageMagick 的 `jxl:lossless` define 在本版本中并不生效，必须以 `quality = 100` 触发无损；
> 这一点是实测得出的，代码中已注明。

### 原始文件安全
所有写入都遵循同一条路径：

1. 目标目录下创建隐藏临时文件（`.<guid>.jxl`）
2. 编码完整成功后，用 `File.Move` **原子改名**为最终文件
3. 任何异常都会删除临时文件，磁盘上不会留下半成品

因此：**源文件只被读取，永远不会被写入**；即使进程崩溃或断电，原始图像也完好无损。
冒烟测试通过对比转换前后的 SHA-256 来验证这一点。

### 界面
- **非打包（unpackaged）WinUI 3 应用**，通过 Windows App SDK 1.8 引导启动
- 文件/文件夹选择使用 Win32 `IFileOpenDialog`：相比 UWP 选择器，它在非打包桌面应用中行为完全一致
- 视图模型不依赖 `SynchronizationContext`，所有界面更新显式经由 `DispatcherQueue`，
  避免"后台线程更新 UI 被静默吞掉"这类难查的问题
- 窗口尺寸通过 `GetMonitorInfo` 按物理像素计算，保证不同 DPI 下居中且不超出屏幕

---

## 项目结构

```
Jexel.slnx
├── src/
│   ├── Jexel.Core/                  转换核心（纯 .NET 类库，无 UI 依赖）
│   │   ├── Models/                  压缩模式、输出策略、转换计划与结果模型
│   │   └── Services/
│   │       ├── ImageFormatCatalog.cs    格式探测、目录扫描、临时文件识别、对话框过滤器
│   │       ├── OutputPathPlanner.cs     输出路径规划与冲突处理
│   │       ├── ConversionPlanner.cs     转换计划构建
│   │       ├── JxlEncoder.cs            单文件编码（临时文件 + 原子改名）
│   │       └── BatchConverter.cs        串行批量编排与进度上报
│   └── Jexel.App/                   WinUI 3 界面
│       ├── ViewModels/              主视图模型与行视图模型
│       ├── Services/                原生文件对话框、Shell 打开
│       ├── Helpers/                 x:Bind 转换函数、体积格式化
│       └── Assets/                  应用图标（ICO / PNG）
├── tests/
│   └── Jexel.SmokeTest/             端到端冒烟测试
├── .github/workflows/
│   ├── build.yml                    CI：构建 + 冒烟测试
│   └── release.yml                  CD：推送标签时发布自包含分发包
├── .editorconfig                    代码风格约定
├── .gitattributes                   换行符与二进制标记
├── .gitignore                       构建产物忽略规则
├── global.json                      .NET SDK 版本下限
└── LICENSE                          MIT 许可证
```

---

## 已知限制

- **HEIC / HEIF**：Magick.NET 未内置 libheif，需安装系统级 HEIF 图像扩展后才可读取。
  界面会明确提示"无法识别的图像格式"，不会静默失败。
- **无损模式下 JPEG 源通常体积变大**：JPEG XL 的无损编码保存的是解码后的像素，
  不会复用 JPEG 原有的 DCT 系数。JPEG 需要显著缩小体积时请使用有损模式。
- 编码过程不可中断：取消操作在文件之间生效，当前正在编码的文件会先完成。
- 本工具只做图像 → JPEG XL 的单向转换，不提供反向导出。

---

## 许可

本项目以 **MIT 许可证** 发布，可自由使用、修改与分发，详见 [LICENSE](LICENSE)。

### 致谢

本项目基于以下成熟开源项目构建：

- [Magick.NET / ImageMagick](https://github.com/dlemstra/Magick.NET) — 图像编解码
- [libjxl](https://github.com/libjxl/libjxl) — JPEG XL 编解码器（经 ImageMagick 调用）
- [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) — WinUI 3 运行时
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM 基础设施

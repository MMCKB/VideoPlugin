# 第三方组件与许可声明

本仓库（ClassIsland.VideoPlayer，作者 MMCKB）以 **GNU Affero General Public License v3.0** 发布，全文见 [LICENSE](LICENSE)。

插件在开发与运行过程中使用了以下第三方组件，特此声明。

## 一、随插件一起分发的组件

| 组件 | 版本 | 许可证 | 用途 | 说明 |
| --- | --- | --- | --- | --- |
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | MIT | 音频输出（`WaveOutEvent` + `BufferedWaveProvider`） | `NAudio*.dll` 会随 `.cipx` 一起打包分发，MIT 许可证允许商业使用、修改与再分发，需保留版权声明 |

NAudio 的 MIT 许可证全文：<https://github.com/naudio/NAudio/blob/master/license.txt>

## 二、仅编译期引用、由 ClassIsland 在运行时提供的组件（不随本插件分发）

| 组件 | 版本 | 许可证 | 用途 |
| --- | --- | --- | --- |
| [ClassIsland.PluginSdk](https://www.nuget.org/packages/ClassIsland.PluginSdk) / ClassIsland.Core | 2.1.0.1 | LGPL-3.0-only | 插件 API、组件基类、设置页基类 |
| [Avalonia](https://avaloniaui.net/) | 11.3.17 | MIT | UI 框架 |
| [FluentAvalonia](https://github.com/amwx/FluentAvalonia) | 2.4.1 | MIT | 控件库（`SettingsExpander`、`InfoBar` 等） |

上述程序集在插件项目中以 `ExcludeAssets="runtime; native"` 引用，即**不会被打包进插件**，运行时由 ClassIsland 本体提供。依据 LGPL-3.0 的动态链接条款，本插件以 AGPL-3.0 发布不受影响；ClassIsland 本体及其 SDK 的许可证条款仍归其原作者所有。

## 三、FFmpeg（不随本插件分发）

**本插件不包含、不链接、也不再分发 FFmpeg 的任何二进制文件或源代码。**

- 插件只是在运行时以**独立进程**方式调用用户自行准备（或由插件下载到用户本机）的 `ffmpeg.exe`，通过管道读取解码后的原始视频帧与 PCM 音频。
- 由于 FFmpeg 始终是独立的可执行程序、与本插件不构成链接关系，本插件不是 FFmpeg 的衍生作品，两者仅在进程边界上通过标准输入输出交互。
- 插件设置页中的「一键下载」会从 <https://www.gyan.dev/ffmpeg/builds/> 下载官方构建的 FFmpeg 压缩包，解压后保存到**用户本机的插件配置目录**。该过程是把第三方程序提供给用户，本仓库不再分发该二进制。

### 再分发时必须注意

gyan.dev 的 `essentials` 构建包含 libx264、libx265 等 GPL 组件，因此**该构建整体按 GPL v3 授权**。如果你打算把 FFmpeg 与本插件打包在一起再分发（例如做成整合安装包），你必须自行遵守 FFmpeg 的 GPL/LGPL 条款，包括但不限于：

- 随附 FFmpeg 的许可证全文与版权声明；
- 提供 FFmpeg 对应的完整源代码（或符合条款的书面获取方式）；
- 不得对 FFmpeg 施加额外限制。

FFmpeg 的许可说明：<https://ffmpeg.org/legal.html>
FFmpeg 源代码：<https://github.com/FFmpeg/FFmpeg>

## 四、图标

`ClassIsland.VideoPlayer/icon.png` 基于 **ClassIsland 官方 AppLogo**（<https://github.com/ClassIsland/ClassIsland>，GPL-3.0）二次创作，右下角添加了播放键标记，仅用于标识本插件。

"ClassIsland" 的名称、图标与相关品牌资源版权归 ClassIsland 项目及其作者所有。

## 五、免责声明

本插件为第三方作品，与 ClassIsland 官方无隶属关系，也未获得官方背书。使用本插件产生的任何后果由使用者自行承担。

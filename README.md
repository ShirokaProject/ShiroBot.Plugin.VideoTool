# VideoTool 视频工具箱

ShiroBot 1.0.0 / .NET 10 插件，版本 **0.1.0**，作者 **greepar**。引用视频或视频文件，直接回复命令即可操作。
目标仓库：[ShirokaProject/ShiroBot.Plugin.VideoTool](https://github.com/ShirokaProject/ShiroBot.Plugin.VideoTool)。许可证：GPL-3.0-only。

## 功能与命令

群聊、私聊均支持。除帮助和硬件探测外，下面的命令均需引用一条视频消息或带视频后缀的文件消息。

| 命令 | 功能 | 返回形式 |
| --- | --- | --- |
| `2gif` | 转换 GIF，默认 512×512、12 fps，从 0.5 秒开始 | GIF 图片 |
| `vclip 00:05 00:15` | 截取第 5 到 15 秒，重新编码以准确截取 | H.264/AAC MP4 视频 |
| `2mp3` | 提取第一条音轨，默认 192 kbps | MP3 文件 |
| `vcompress 10` | 压缩至 10 MiB 以内；不带参数使用配置值 | H.264/AAC MP4 视频 |
| `vcover 00:03` | 截取第 3 秒的画面；不带参数默认第 0 秒 | JPEG 图片 |
| `vinfo` | 查看格式、时长、大小、编码、尺寸、帧率与音轨 | 文字 |
| `vhw` | 实际编码测试并查看各硬件接口状态，无需引用 | 文字 |
| `vhelp` | 查看命令说明，无需引用 | 文字 |

时间支持秒数（如 `3.5`）、`分:秒`、`时:分:秒`。压缩目标支持小数，例如 `vcompress 0.5`，单位 MiB（1024×1024 字节）。
视频消息不限制文件扩展名；文件消息支持 `.mp4`、`.m4v`、`.mov`、`.mkv`、`.avi`、`.webm`、`.flv`、`.wmv`、`.mpg`、`.mpeg`、`.ts`、`.mts`、`.m2ts`、`.3gp`、`.3g2`、`.ogv`，大小写不敏感。
兼容原有 `#gif`、同一消息附带视频和 `#gif`，以及 `#gif https://example.com/video.mp4` 直链用法。

MP3 作为独立文件上传，适配 Milky 的文件发送要求；视频、图片和文字结果引用命令消息回复。
QQ 群文件和私聊文件提供专用下载地址解析。消息查询、资源下载地址解析、媒体发送及大小限制仍取决于所使用的适配器和平台。

## 安装与升级

```sh
dotnet build -c Release
```

将 `bin/Release/net10.0/ShiroBot.Plugin.VideoTool.dll` 复制到宿主的 `plugins/`，加载并启用插件。FFmpeg、ffprobe、gifsicle 由用户自行安装，不随插件下载或分发。

从 VideoToGif 升级时，用新 DLL 替换 `ShiroBot.Plugin.VideoToGif.dll`，避免同时保留两份入口 DLL。**稳定插件 ID 继续使用 `VideoToGif`**，因此已有群路由、启用状态和 `plugins/VideoToGif/config.toml` 直接沿用，不搬迁或覆盖用户配置。源码目录、程序集名称、显示名称和仓库名称更新为 VideoTool。原有 GIF 配置键及默认转换参数保持兼容，新增字段由宿主加载时补齐。

## 程序路径自动探测

WebUI“基础”中可配置 FFmpeg、gifsicle 和 ffprobe 的完整路径或程序名。

- 留空时，从宿主进程的 `PATH` 查找。Windows 自动查找 `.exe` / `.com`。
- ffprobe 留空时优先查找已选 FFmpeg 的同目录，再查找 PATH。
- 手动路径优先，指定路径无效时会报错；不要给路径加引号或附带参数。
- GIF 操作只需要 FFmpeg 和 gifsicle；其余操作使用 FFmpeg 与 ffprobe。缺少工具时在下载视频前提示。
- 修改系统 PATH 后需要重启宿主；从桌面启动的程序与终端可能拥有不同的 PATH。

例如 macOS 路径为 `/opt/homebrew/bin/ffmpeg`、`/opt/homebrew/bin/ffprobe`、`/opt/homebrew/bin/gifsicle`。Windows 可以填写 `C:\tools\ffmpeg.exe`。

## WebUI 配置

| 页面 | 参数 |
| --- | --- |
| 基础 | FFmpeg、ffprobe、gifsicle 路径 |
| GIF | 起始时间、截取时长、帧率、尺寸、裁剪/填充、缩放算法、调色板、抖动、循环、优化和有损压缩 |
| 视频 | 最大高度、最大帧率、编码预设、截取 CRF、默认压缩目标、最大截取时长、JPEG 质量、压缩编码器、VAAPI 设备 |
| 音频 | MP3 比特率、视频 AAC 比特率 |
| 高级 | 任务超时、最大输入大小、最大发送大小、硬件探测超时 |

配置保存后对新任务生效，当前任务继续使用开始时的配置。后台也会验证字段范围与下拉选项。
手动配置参考 `config.example.toml`，所有字段仍位于 TOML 顶层，不要添加分组表。已有配置的用户值保留，例如旧配置的输入大小为 100 MiB 时不会强制改为 30。

输入默认必须小于 **30 MiB**，先检查文件消息大小，再检查 HTTP 响应和实际下载字节数，达到限制就停止。所有操作共用输入限制；默认输出上限 30 MiB、任务总超时 180 秒、一次只处理一个任务。忙时提示重试，不排队。

## 调整 GIF 裁剪取景

引用视频只需回复一个偏移数字，输出宽高不变，不加边：

| 命令 | 横屏转方形 | 竖屏转方形 |
| --- | --- | --- |
| `2gif` 或 `2gif 0` | 居中 | 居中 |
| `2gif -25` | 偏左 | 偏上 |
| `2gif 25` | 偏右 | 偏下 |
| `2gif -50` | 最左 | 顶部 |
| `2gif 50` | 最右 | 底部 |

偏移范围 -50 到 50，支持小数。0 是中间，-25 是中间与左/上边缘的中点，不是最左/上端。按缩放后实际可裁剪的方向自动移动，无需指定两个坐标；不再接受逗号形式。源视频与输出比例相同时没有裁剪余量，偏移不会改变画面。

每次 `2gif` 独立取景，不修改全局配置；不带参数始终居中并使用裁剪模式。`#gif` 兼容入口仍使用 WebUI 的裁剪位置及适配模式。

## 处理行为

- **GIF**：默认与原命令一致：从 0.5 秒开始、12 fps、Lanczos 等比放大居中裁剪至 512×512、128 色、diff 统计、Bayer 4、无限循环、gifsicle `-O3 --lossy=160`。默认转到结尾，可通过 GIF 截取时长控制耗时和大小。
- **截取**：重新编码为 H.264/AAC，默认最高 720p、30 fps，不放大较小的视频；无音轨的视频也可截取。默认最长截取 300 秒。
- **压缩**：根据时长计算视频和音频码率，默认 auto 优先用实测可用硬件按目标码率编码，最多调整三次；失败或体积仍超限则回退 CPU 两遍编码（最多两轮）。已小于目标的视频提示无需压缩；不为追求目标体积放大原视频。目标过小或所有尝试仍无法达到时明确报错。压缩目标不能超过最大发送大小。
- **提取 MP3**：没有音轨时明确提示，不生成空文件。
- **封面**：保留原始画面尺寸，时间超过视频时长时提示错误。
- **信息**：使用 ffprobe 读取元数据，不进行重新编码。

下载、探测和所有转码步骤共用任务超时。超时或插件卸载会取消下载并终止进程树。输入视频、中间 GIF、失败产物和两遍编码日志及时删除；成功输出由宿主管理临时目录保留，供适配器上传，到期清理。调用使用进程参数列表，不经过 shell。

## 跨平台硬件压缩

「视频 → 压缩编码器」默认 `auto`，也可选 `cpu` 或具体编码器。当前仅 `vcompress` 启用硬件编码；GIF、截取和缩放仍沿用 CPU 路径。`cpu` 使用 libx264 两遍编码；硬件采用各自独立的参数，不能复用 x264 的 CRF、preset 和 pass 参数。

| 接口 | 候选编码器 | 适用路径 |
| --- | --- | --- |
| NVIDIA NVENC | `h264_nvenc` | NVIDIA 驱动和 FFmpeg 支持的平台 |
| Intel QSV | `h264_qsv` | Intel Quick Sync |
| AMD AMF | `h264_amf` | 安装 AMF 运行时的支持平台 |
| Apple VideoToolbox | `h264_videotoolbox` | macOS，禁止隐式软件回退 |
| VAAPI | `h264_vaapi` | Linux，自动枚举 renderD 设备或指定路径，NV12 上传 |
| V4L2 M2M | `h264_v4l2m2m` | Linux SoC，包括暴露可用编码节点的高通 Venus 等驱动；分别尝试 NV12/YUV420P |
| Media Foundation | `h264_mf` | Windows，含高通 ARM，强制使用硬件 MFT |
| Rockchip MPP | `h264_rkmpp` | 含此编码器的厂商 FFmpeg 构建与 MPP 驱动 |
| OpenMAX IL | `h264_omx` | 有对应厂商组件的 ARM 平台，具体是否走硬件取决于组件实现 |

不是所有芯片、驱动和内核都实现编码接口。高通 Linux 的 GPU 渲染/Vulkan 可用不等于 V4L2 视频编码可用；需要 FFmpeg 编译对应编码器，内核与固件支持编码，宿主账户有设备访问权限。较新 SoC 或只有解码接口的驱动不会被标记为可编码。没有在不具备相应硬件的平台上宣称已经实机验证。

插件先读取 `ffmpeg -encoders`，再为编译存在的每个候选使用 640×480 的原始帧实际编码 12 帧，使用 ffprobe 检查 H.264、尺寸和帧数，并用 FFmpeg 实际解码输出。只有全部通过才可选中，CPU 参考编码器也执行相同验证。每个候选默认超时 10 秒，总探测仍受任务超时约束，取消会终止进程树。探测样片立即清理。

结果在内存缓存 10 分钟，FFmpeg 程序更新时间、ffprobe 路径、VAAPI 配置或探测超时改变会重新测试。发送 `vhw` 查看缓存内的真实测试状态；WebUI 插件操作「重新探测硬件编码器」可强制刷新并查看失败原因。多显卡 VAAPI 可手动指定 `/dev/dri/renderD129` 等路径。V4L2 编码节点由 FFmpeg 自动扫描。

短测通过仍可能遇到实际视频的尺寸、驱动、设备忙等限制。实际硬件压缩失败会记录原因，尝试其他已通过的候选，最终回退 CPU。最终体积必须达到目标并满足发送上限，超时取消不触发新一轮回退。硬件输出还会校验编码、时长和音轨。宿主日志记录最终使用的编码器。

接口和参数依据 FFmpeg 官方[编码器文档](https://ffmpeg.org/ffmpeg-codecs.html)、[V4L2 编码实现](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/v4l2_m2m_enc.c)、[Media Foundation 实现](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/mfenc.c)和 [VideoToolbox 实现](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/videotoolboxenc.c)。

## 验证

安装 FFmpeg（含 ffprobe）和 gifsicle 后执行：

```sh
dotnet run --project Tests/VideoTool.Verification.csproj
# 可在末尾指定 FFmpeg 和 gifsicle 完整路径，ffprobe 会查找 FFmpeg 同目录：
# dotnet run --project Tests/VideoTool.Verification.csproj -- /opt/homebrew/bin/ffmpeg /opt/homebrew/bin/gifsicle
```

验证会生成真实 H.264/AAC 样片，检查 GIF、截取、MP3、JPEG、目标体积压缩和元数据，以及参数校验、程序探测、无音轨、时间越界、损坏输入、超时取消和文件清理。模拟宿主与本地 HTTP 服务覆盖所有命令的消息类型、引用查询、热更新、大小限制、忙时回复和卸载。实际平台上传与显示需要连接适配器验证。

另有 6 轮可回收 AssemblyLoadContext 生命周期测试：每轮实际下载并转换 GIF、卸载插件、强制 GC 后确认程序集上下文可回收，并检查无残留编码器子进程；首轮同时运行硬件探测缓存。此测试和代码检查不能证明所有输入及长期运行均绝无内存泄漏。FFmpeg/gifsicle 在独立进程中可能因视频大小、帧数及调色板分析占用较高峰值内存，插件总超时会限制异常长的任务。

## 发布到 ShirokaProject

将本目录作为 `ShirokaProject/ShiroBot.Plugin.VideoTool` 仓库根目录上传。GitHub Actions 在推送和 PR 时执行真实转码验证及 Release 构建。Tag（当前为 `v0.1.0`）必须与项目和插件元数据版本一致；推送 tag 后自动发布 `ShiroBot.Plugin.VideoTool.dll` 与 `ShiroBot.Plugin.VideoTool.zip`。

ZIP 只包含 DLL、README、LICENSE 和配置示例，不包含工具、SDK、用户配置或测试文件。仓库公开并发布正式 Release 后，可将 `market-entry.json` 内容加入 `ShirokaProject/awesome-shirobot` 的插件清单申请收录。

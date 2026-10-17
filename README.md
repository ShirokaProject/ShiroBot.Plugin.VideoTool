# VideoTool 视频工具箱

ShiroBot 视频处理插件，群聊、私聊均可使用。

## 安装

将 `ShiroBot.Plugin.VideoTool.dll` 放入宿主 `plugins/` 目录并启用。
需要自行安装 **FFmpeg（含 ffprobe）和 gifsicle**。插件会自动从 PATH 查找，也可在 WebUI 中填写程序路径。

## 用法

**引用一条视频或视频文件消息，再发送命令：**

| 命令 | 功能 |
| --- | --- |
| `2gif` | 居中转 GIF，默认 512×512、12 fps |
| `2gif -25` / `2gif 25` | 调整 GIF 取景：横屏向左/右，竖屏向上/下 |
| `vclip 00:05 00:15` | 截取第 5 到 15 秒，输出 MP4 |
| `2mp3` | 提取音轨，发送 MP3 文件 |
| `vcompress 10` | 压缩到 10 MiB 以内，不填大小默认 10 MiB |
| `vcover 00:03` | 截取第 3 秒封面，不填时间默认第 0 秒 |
| `vinfo` | 查看视频时长、尺寸、编码等信息 |

GIF 偏移范围 **-50～50**：0 居中，-50 最左/上，50 最右/下。时间可填写秒数、分:秒或时:分:秒。

直接发送 `vhelp` 查看帮助，发送 `vhw` 检测可用的硬件编码器，无需引用视频。

## 配置

在 WebUI 调整 GIF、视频、音频参数及大小限制。输入默认须小于 **30 MiB**，任务默认超时 **180 秒**。
`vcompress` 默认自动探测硬件编码，失败时回退 CPU。详细配置见 [config.example.toml](config.example.toml)。

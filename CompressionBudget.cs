namespace ShiroBot.Plugin.VideoTool;

internal sealed record CompressionBudget(int VideoRate, int AudioRate, int Height, int Fps)
{
    internal static CompressionBudget Create(MediaInfo info, long target, PluginConfig config)
    {
        var total = target * 8d / info.Duration * 0.90;
        var audio = info.AudioCodec is null ? 0 : Math.Clamp((int)(total * 0.15 / 1000), 32, config.VideoAudioKbps) * 1000;
        var video = (int)Math.Min(int.MaxValue / 2, total - audio);
        if (video < 16000)
            throw new ArgumentException($"目标体积相对 {info.Duration:0.##} 秒时长过小，扣除音频后视频仅约 {Math.Max(0, video) / 1000} kbps，请增大目标或截短视频。此情况不代表硬件不可用。");
        var fps = Math.Min(config.VideoMaxFps, video < 100000 ? 12 : video < 300000 ? 15 : config.VideoMaxFps);
        var height = Math.Min(config.VideoMaxHeight, info.Height);
        var pixels = video / (fps * (config.CompressionCodec == "hevc" ? 0.035 : 0.05));
        var budgetHeight = (int)Math.Sqrt(pixels * info.Height / Math.Max(1, info.Width));
        height = Math.Min(height, Math.Max(144, budgetHeight));
        return new(video, audio, Math.Max(2, height / 2 * 2), fps);
    }

    internal CompressionBudget Retry(long target, long actual) => this with
    {
        VideoRate = Math.Max(16000, (int)(VideoRate * Math.Min(0.9, target / (double)actual * 0.9))),
        Height = Math.Min(Height, Math.Max(144, Height * 3 / 4 / 2 * 2)), Fps = Math.Min(Fps, 12)
    };
}

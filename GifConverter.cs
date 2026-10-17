using System.Globalization;

namespace ShiroBot.Plugin.VideoTool;

internal static class GifConverter
{
    internal static string BuildFilter(PluginConfig config)
    {
        config.Validate();
        // Keep the original center filter unchanged for existing configurations.
        var position = config.CropXPercent == 50 && config.CropYPercent == 50 ? "" :
            FormattableString.Invariant($":x=(iw-ow)*{config.CropXPercent / 100:0.########}:y=(ih-oh)*{config.CropYPercent / 100:0.########}");
        var fit = config.FitMode == "crop"
            ? $"force_original_aspect_ratio=increase:flags={config.ScaleAlgorithm},crop={config.Width}:{config.Height}{position}"
            : $"force_original_aspect_ratio=decrease:flags={config.ScaleAlgorithm},pad={config.Width}:{config.Height}:(ow-iw)/2:(oh-ih)/2:color=black";
        return FormattableString.Invariant($"[0:v]fps={config.Fps},scale={config.Width}:{config.Height}:{fit},split[a][b];[a]palettegen=max_colors={config.MaxColors}:stats_mode={config.StatsMode}[p];[b][p]paletteuse=dither={config.Dither}:bayer_scale={config.BayerScale}");
    }

    internal static string[] FfmpegArguments(PluginConfig config, string input, string output)
    {
        var filter = BuildFilter(config);
        var args = new List<string>
        {
            "-y", "-nostdin", "-hide_banner", "-loglevel", "error",
            "-ss", config.StartSeconds.ToString("0.########", CultureInfo.InvariantCulture),
            "-protocol_whitelist", "file,pipe", "-i", input
        };
        if (config.DurationSeconds > 0)
            args.AddRange(["-t", config.DurationSeconds.ToString("0.########", CultureInfo.InvariantCulture)]);
        args.AddRange(["-filter_complex", filter, "-loop", config.Loop.ToString(CultureInfo.InvariantCulture), output]);
        return [.. args];
    }

    internal static string[] GifsicleArguments(PluginConfig config, string input, string output)
    {
        config.Validate();
        return [$"-O{config.OptimizationLevel}", $"--lossy={config.Lossy}", input, "-o", output];
    }

    internal static async Task<string> ConvertAsync(string input, string directory, PluginConfig config, CancellationToken token)
    {
        config.Validate();
        var ffmpeg = ExecutableLocator.Resolve(config.FfmpegPath, "ffmpeg");
        var gifsicle = ExecutableLocator.Resolve(config.GifsiclePath, "gifsicle");
        var intermediate = Path.Combine(directory, "temp_base.gif");
        var output = Path.Combine(directory, "output.gif");
        var completed = false;
        try
        {
            await ProcessRunner.RunFfmpegAsync(ffmpeg, FfmpegArguments(config, input, intermediate), "FFmpeg", config.FfmpegThreads, token);
            if (!File.Exists(intermediate) || new FileInfo(intermediate).Length == 0)
                throw new InvalidDataException("FFmpeg 没有生成画面，请检查起始时间是否超过视频长度。");
            await RunAsync(gifsicle, GifsicleArguments(config, intermediate, output), "gifsicle", token);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidDataException("gifsicle 没有生成有效的 GIF。");
            if (new FileInfo(output).Length > config.MaxOutputMiB * 1024L * 1024L)
                throw new InvalidDataException($"生成的 GIF 超过 {config.MaxOutputMiB} MiB，请降低帧率、尺寸或截取时长。");
            completed = true;
            return output;
        }
        finally
        {
            File.Delete(intermediate);
            if (!completed) File.Delete(output);
        }
    }

    internal static async Task RunAsync(string executable, IEnumerable<string> arguments, string name, CancellationToken token)
    {
        await ProcessRunner.RunAsync(executable, arguments, name, token);
    }
}

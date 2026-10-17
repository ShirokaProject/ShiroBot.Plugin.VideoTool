using ShiroBot.Plugin.VideoTool;

internal static class CropVerification
{
    internal static async Task RunAsync(string root, string ffmpeg, string gifsicle, CancellationToken token)
    {
        var directory = Path.Combine(root, "crop positions");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "portrait.mp4");
        await ProcessRunner.RunAsync(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i",
            "color=red:size=64x192:rate=4,drawbox=x=0:y=64:w=64:h=64:color=green:t=fill,drawbox=x=0:y=128:w=64:h=64:color=blue:t=fill",
            "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", input], "crop source", token);
        var config = new PluginConfig { FfmpegPath = ffmpeg, GifsiclePath = gifsicle, StartSeconds = 0, Width = 64, Height = 64, Fps = 4, Lossy = 0 };
        foreach (var (text, expectedY) in new[] { ("2gif", 50d), ("2gif -25", 25d), ("2gif 25", 75d), ("2gif -50", 0d), ("2gif 50", 100d) })
        {
            var command = VideoCommand.Parse(text)!;
            var original = new PluginConfig { CropYPercent = 10, FitMode = "pad" };
            var adjusted = original.ForGifCommand(command);
            if (adjusted.CropXPercent != expectedY || adjusted.CropYPercent != expectedY || adjusted.FitMode != "crop" || original.CropYPercent != 10 || original.FitMode != "pad")
                throw new Exception("Per-command crop settings or isolation incorrect: " + text);
            Console.WriteLine("PASS Command crop without changing global config: " + text);
        }
        foreach (var text in new[] { "2gif extra", "2gif 51", "2gif NaN", "2gif 0,-25", "2gif 25 0" })
        {
            try { VideoCommand.Parse(text); throw new Exception("Invalid crop accepted: " + text); }
            catch (ArgumentException) { }
        }
        foreach (var landscape in new[] { false, true })
        {
        if (landscape)
        {
            input = Path.Combine(directory, "landscape.mp4");
            await ProcessRunner.RunAsync(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i",
                "color=red:size=192x64:rate=4,drawbox=x=64:y=0:w=64:h=64:color=green:t=fill,drawbox=x=128:y=0:w=64:h=64:color=blue:t=fill",
                "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", input], "landscape source", token);
        }
        foreach (var (position, expected) in new[] { (0d, "red"), (50d, "green"), (100d, "blue") })
        {
            var command = VideoCommand.Parse($"2gif {position - 50}")!;
            var output = await VideoOperations.ExecuteAsync(command, input, directory, config, token);
            var gif = output.Path!;
            var ppm = Path.Combine(directory, "pixel.ppm");
            await ProcessRunner.RunAsync(ffmpeg, ["-y", "-v", "error", "-i", gif, "-frames:v", "1", "-vf", "scale=1:1", ppm], "read crop", token);
            var bytes = await File.ReadAllBytesAsync(ppm, token);
            var rgb = bytes[^3..];
            var index = expected == "red" ? 0 : expected == "green" ? 1 : 2;
            if (rgb[index] < 80 || rgb[index] <= rgb[(index + 1) % 3] * 2 || rgb[index] <= rgb[(index + 2) % 3] * 2)
                throw new Exception($"Crop {position}% did not select {expected}");
            Console.WriteLine($"PASS Actual {(landscape ? "landscape" : "portrait")} GIF single offset {position - 50} selects {expected}");
        }
        }
        config.CropXPercent = 25;
        config.CropYPercent = 25;
        if (!GifConverter.BuildFilter(config).Contains(":x=(iw-ow)*0.25:y=(ih-oh)*0.25")) throw new Exception("Crop coordinates incorrect");
        config.CropYPercent = -1;
        try { GifConverter.BuildFilter(config); throw new Exception("Invalid crop accepted"); }
        catch (ArgumentException) { Console.WriteLine("PASS Crop position range validated"); }
    }
}

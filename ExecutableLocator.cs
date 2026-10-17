namespace ShiroBot.Plugin.VideoTool;

internal static class ExecutableLocator
{
    internal static string ResolveProbe(PluginConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.FfprobePath)) return Resolve(config.FfprobePath, "ffprobe");
        var ffmpeg = Resolve(config.FfmpegPath, "ffmpeg");
        var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        return CanExecute(sibling) ? sibling : Resolve(null, "ffprobe");
    }

    internal static string Resolve(string? configured, string program)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? program : configured.Trim();
        if (Path.IsPathRooted(value) || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            var path = Path.GetFullPath(value);
            if (CanExecute(path)) return path;
            throw new InvalidOperationException($"配置的 {program} 不存在或没有执行权限：{value}");
        }

        var extensions = OperatingSystem.IsWindows() && !Path.HasExtension(value)
            ? new[] { ".exe", ".com" }
            : new[] { "" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            // Empty PATH entries must not implicitly search the working directory.
            var folder = directory.Trim().Trim('"');
            if (folder.Length == 0 || !Path.IsPathRooted(folder)) continue;
            foreach (var extension in extensions)
            {
                var path = Path.Combine(folder, value + extension);
                if (CanExecute(path)) return Path.GetFullPath(path);
            }
        }
        throw new InvalidOperationException($"在宿主进程的 PATH 环境变量中找不到 {value}。请安装 {program} 并重启宿主，或在插件“基础”配置中填写完整路径。");
    }

    private static bool CanExecute(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        try
        {
            const UnixFileMode executable = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & executable) != 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

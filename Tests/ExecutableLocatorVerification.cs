using ShiroBot.Plugin.VideoTool;

internal static class ExecutableLocatorVerification
{
    internal static void Run(string root)
    {
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var first = Path.Combine(root, "first tool directory");
        var second = Path.Combine(root, "second tool directory");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var firstTool = Path.Combine(first, name);
        var secondTool = Path.Combine(second, name);
        foreach (var path in new[] { firstTool, secondTool })
        {
            File.WriteAllText(path, "verification fixture");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        try
        {
            Environment.SetEnvironmentVariable("PATH", first + Path.PathSeparator + second);
            Check(ExecutableLocator.Resolve("", "ffmpeg") == firstTool, "Empty config detects the first executable in PATH, including spaces");
            Check(ExecutableLocator.Resolve("ffmpeg", "ffmpeg") == firstTool, "Configured program name searches PATH");
            Check(ExecutableLocator.Resolve(secondTool, "ffmpeg") == secondTool, "Explicit executable path takes priority over PATH");
            Reject(Path.Combine(root, "missing", name), "Invalid explicit path does not fall back to PATH");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(firstTool, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Check(ExecutableLocator.Resolve("", "ffmpeg") == secondTool, "PATH search skips files without execute permission");
            }
            Environment.SetEnvironmentVariable("PATH", "");
            Reject("", "Missing PATH produces a clear configuration error");
        }
        finally { Environment.SetEnvironmentVariable("PATH", originalPath); }
    }

    private static void Reject(string configured, string label)
    {
        try { ExecutableLocator.Resolve(configured, "ffmpeg"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS " + label); return; }
        throw new Exception(label);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }
}

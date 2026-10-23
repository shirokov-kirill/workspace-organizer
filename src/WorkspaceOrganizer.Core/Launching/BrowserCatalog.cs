using Microsoft.Win32;

namespace WorkspaceOrganizer.Core.Launching;

public enum BrowserEngine { Chromium, Firefox }

public sealed record BrowserInfo(string Id, string Name, BrowserEngine Engine, string ExecutablePath);

/// <summary>Finds installed browsers that support per-profile isolation from the command line.</summary>
public static class BrowserCatalog
{
    sealed record Known(
        string Id, string Name, BrowserEngine Engine, string ExeName,
        string[] WindowsPaths, string[] MacPaths, string[] UnixCommands);

    static readonly Known[] KnownBrowsers =
    [
        new("chrome", "Google Chrome", BrowserEngine.Chromium, "chrome.exe",
            [@"Google\Chrome\Application\chrome.exe"],
            ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"],
            ["google-chrome", "google-chrome-stable"]),
        new("edge", "Microsoft Edge", BrowserEngine.Chromium, "msedge.exe",
            [@"Microsoft\Edge\Application\msedge.exe"],
            ["/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"],
            ["microsoft-edge", "microsoft-edge-stable"]),
        new("brave", "Brave", BrowserEngine.Chromium, "brave.exe",
            [@"BraveSoftware\Brave-Browser\Application\brave.exe"],
            ["/Applications/Brave Browser.app/Contents/MacOS/Brave Browser"],
            ["brave-browser", "brave"]),
        new("vivaldi", "Vivaldi", BrowserEngine.Chromium, "vivaldi.exe",
            [@"Vivaldi\Application\vivaldi.exe"],
            ["/Applications/Vivaldi.app/Contents/MacOS/Vivaldi"],
            ["vivaldi"]),
        new("firefox", "Mozilla Firefox", BrowserEngine.Firefox, "firefox.exe",
            [@"Mozilla Firefox\firefox.exe"],
            ["/Applications/Firefox.app/Contents/MacOS/firefox"],
            ["firefox"]),
    ];

    public static IReadOnlyList<BrowserInfo> Detect()
    {
        var found = new List<BrowserInfo>();
        foreach (var known in KnownBrowsers)
        {
            if (Locate(known) is { } path)
                found.Add(new BrowserInfo(known.Id, known.Name, known.Engine, path));
        }
        return found;
    }

    static string? Locate(Known known)
    {
        if (OperatingSystem.IsWindows())
        {
            string[] roots =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ];
            return FromAppPaths(known.ExeName)
                ?? roots.Where(r => r.Length > 0)
                    .SelectMany(r => known.WindowsPaths.Select(p => Path.Combine(r, p)))
                    .FirstOrDefault(File.Exists);
        }

        if (OperatingSystem.IsMacOS())
            return known.MacPaths.FirstOrDefault(File.Exists);

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        return known.UnixCommands
            .SelectMany(c => pathDirs.Select(d => Path.Combine(d, c)))
            .FirstOrDefault(File.Exists);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static string? FromAppPaths(string exeName)
    {
        var subKey = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(subKey);
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
                return path.Trim('"');
        }
        return null;
    }
}

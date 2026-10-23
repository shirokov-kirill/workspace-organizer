using System.Diagnostics;
using System.Text;
using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Profiles;

namespace WorkspaceOrganizer.Core.Launching;

/// <param name="ProfileDirectory">Created before launch when set.</param>
/// <param name="Environment">Applied on top of the organizer's own environment when set.</param>
public sealed record LaunchCommand(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? ProfileDirectory,
    ProfileEnvironment? Environment = null,
    string? WorkingDirectory = null);

/// <summary>
/// Turns workspace settings into concrete command lines. Isolation comes from giving each
/// workspace its own browser/app/CLI data inside its profile folder, so logins never mix.
/// </summary>
public static class LaunchPlanner
{
    public const string ProfileDirPlaceholder = "{profileDir}";

    public static LaunchCommand Browser(ProfileLayout layout, string? browserProfile, BrowserInfo browser, IEnumerable<string> urls)
    {
        var args = new List<string>();
        string? profileDir = null;
        var useOwnProfile = !string.IsNullOrWhiteSpace(browserProfile);

        switch (browser.Engine)
        {
            case BrowserEngine.Chromium when useOwnProfile:
                args.Add($"--profile-directory={browserProfile!.Trim()}");
                break;
            case BrowserEngine.Chromium:
                profileDir = layout.Browser(browser.Id);
                args.Add($"--user-data-dir={profileDir}");
                args.Add("--no-first-run");
                break;
            case BrowserEngine.Firefox when useOwnProfile:
                args.AddRange(["-P", browserProfile!.Trim()]);
                break;
            case BrowserEngine.Firefox:
                profileDir = layout.Browser(browser.Id);
                args.AddRange(["-profile", profileDir]);
                break;
        }

        args.AddRange(urls.Select(NormalizeUrl));
        return new LaunchCommand(browser.ExecutablePath, args, profileDir);
    }

    public static LaunchCommand App(ProfileLayout layout, AppShortcut app, ProfileEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(app.Path))
            throw new ArgumentException("No executable path is set.");

        var tokens = SplitArguments(app.Arguments);
        string? profileDir = null;
        if (tokens.Any(t => t.Contains(ProfileDirPlaceholder, StringComparison.Ordinal)))
        {
            profileDir = layout.App(app.Name);
            tokens = tokens.Select(t => t.Replace(ProfileDirPlaceholder, profileDir, StringComparison.Ordinal)).ToList();
        }
        return new LaunchCommand(app.Path.Trim().Trim('"'), tokens, profileDir, environment, layout.Root);
    }

    /// <summary>
    /// A PowerShell window that runs the profile's activate script. The script, not inherited env vars, is
    /// what makes this reliable: a Windows Terminal that is already running ignores the caller's environment.
    /// </summary>
    /// <param name="windowsTerminal">wt.exe, or null to open a plain console window.</param>
    /// <param name="shell">pwsh.exe or powershell.exe.</param>
    public static LaunchCommand Terminal(ProfileLayout layout, string title, string? windowsTerminal, string shell)
    {
        string[] shellArgs = [shell, "-NoExit", "-ExecutionPolicy", "Bypass", "-File", layout.ActivatePowerShell];
        return windowsTerminal is null
            ? new LaunchCommand(shell, shellArgs[1..], null, WorkingDirectory: layout.Root)
            : new LaunchCommand(windowsTerminal, ["-w", "new", "--title", title, "-d", layout.Root, .. shellArgs], null, WorkingDirectory: layout.Root);
    }

    /// <summary>Accepts http(s) URLs; bare hosts get https://. Anything else is rejected so it can't act as a browser flag.</summary>
    public static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
            trimmed = "https://" + trimmed;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.AbsoluteUri;
        throw new ArgumentException($"'{url}' is not an http(s) URL.");
    }

    /// <summary>Splits on whitespace; double quotes group a token and are removed.</summary>
    public static List<string> SplitArguments(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                    tokens.Add(current.ToString());
                current.Clear();
                hasToken = false;
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }
        if (hasToken)
            tokens.Add(current.ToString());
        return tokens;
    }
}

public static class ProcessLauncher
{
    public static void Start(LaunchCommand command)
    {
        if (command.ProfileDirectory is not null)
            Directory.CreateDirectory(command.ProfileDirectory);

        using var _ = Process.Start(CreateStartInfo(command));
    }

    public static ProcessStartInfo CreateStartInfo(LaunchCommand command)
    {
        var info = new ProcessStartInfo(command.FileName) { UseShellExecute = false };
        foreach (var argument in command.Arguments)
            info.ArgumentList.Add(argument);
        if (command.WorkingDirectory is { } directory && Directory.Exists(directory))
            info.WorkingDirectory = directory;
        command.Environment?.ApplyTo(info.Environment);
        return info;
    }

    /// <summary>The terminal programs available for <see cref="LaunchPlanner.Terminal"/>.</summary>
    public static (string? WindowsTerminal, string Shell) DetectTerminal()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var windowsTerminal = ExecutableResolver.Find("wt", path, pathExt, null);
        var shell = ExecutableResolver.Find("pwsh", path, pathExt, null)
            ?? ExecutableResolver.Find("powershell", path, pathExt, null)
            ?? "powershell.exe";
        return (windowsTerminal, shell);
    }
}

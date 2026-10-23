using System.Diagnostics;
using WorkspaceOrganizer.Core.Launching;
using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Profiles;
using WorkspaceOrganizer.Core.Storage;

namespace WorkspaceOrganizer.Cli;

static class Program
{
    const string Usage = """
        wso — run programs with a workspace profile's logins

          wso which [command]               show the active profile here (and the program `command` would run)
          wso list                          list profiles
          wso run <profile> [--] <cmd> ...  run a command in a profile
          wso shell <profile>               start PowerShell with the profile activated
          wso env <profile>                 print the profile's environment variables
          wso open <url>                    open a URL in the active profile's browser (profiles set BROWSER to this)
          wso shims install | uninstall     manage command shims (claude, gh, git, …) on your PATH

        Shims pick the profile from WSO_PROFILE (set by profile terminals) or from the current
        folder being inside <profiles root>\<profile>. Elsewhere they run the program unchanged.
        """;

    static int Main(string[] args)
    {
        var self = Environment.ProcessPath ?? "wso";
        var invokedAs = Path.GetFileNameWithoutExtension(self);
        try
        {
            if (string.Equals(invokedAs, ProfileLayout.OpenerName, StringComparison.OrdinalIgnoreCase))
                return args is [var link] ? Open(link) : Help(Console.Error, 2);
            if (!string.Equals(invokedAs, ShimInstaller.CliName, StringComparison.OrdinalIgnoreCase))
                return RunShim(invokedAs, args, Path.GetDirectoryName(self)!);

            return args switch
            {
                ["which"] => Which(null),
                ["which", var command] => Which(command),
                ["list"] => List(),
                ["run", var profile, "--", var command, .. var rest] => RunInProfile(profile, command, rest),
                ["run", var profile, var command, .. var rest] => RunInProfile(profile, command, rest),
                ["shell", var profile] => Shell(profile),
                ["env", var profile] => PrintEnvironment(profile),
                ["open", var url] => Open(url),
                ["shims", "install"] => InstallShims(),
                ["shims", "uninstall"] => UninstallShims(),
                ["help" or "--help" or "-h" or "/?"] => Help(Console.Out, 0),
                _ => Help(Console.Error, 2),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"wso: {ex.Message}");
            return 1;
        }
    }

    static string? Get(IDictionary<string, string?> environment, string name) =>
        environment.TryGetValue(name, out var value) ? value : null;

    static int Help(TextWriter writer, int exitCode)
    {
        writer.WriteLine(Usage);
        return exitCode;
    }

    /// <summary>Shims live in &lt;root&gt;\.shims, so their parent is the root. Otherwise ask the organizer's settings.</summary>
    static string ProfilesRoot()
    {
        var baseDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (string.Equals(Path.GetFileName(baseDir), ProfileLayout.ShimsFolderName, StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(baseDir)!;
        return new JsonDataStore(JsonDataStore.DefaultPath).TryRead()?.Settings.ProfilesRoot ?? OrganizerSettings.DefaultProfilesRoot;
    }

    static ProfileResolution? ResolveHere(string root) =>
        ProfileResolver.Resolve(root, Environment.CurrentDirectory, Environment.GetEnvironmentVariable(ProfileEnvironment.ProfileVariable));

    static ProfileMarker RequireProfile(string root, string folder) =>
        (ProfileLayout.IsValidFolder(folder) ? ProfileMarker.TryRead(new ProfileLayout(root, folder).Marker) : null)
        ?? throw new InvalidOperationException($"No profile \"{folder}\" in {root}. Run `wso list` to see profiles.");

    static int RunShim(string command, string[] args, string shimDirectory)
    {
        var root = ProfilesRoot();
        return Execute(command, args, ResolveHere(root)?.Marker, shimDirectory);
    }

    static int RunInProfile(string folder, string command, string[] args)
    {
        var root = ProfilesRoot();
        return Execute(command, args, RequireProfile(root, folder), ProfileLayout.ShimsDirectory(root));
    }

    /// <summary>Runs the real program (never a shim) in the console we were started from and returns its exit code.</summary>
    static int Execute(string command, string[] args, ProfileMarker? profile, string shimDirectory)
    {
        var info = new ProcessStartInfo { UseShellExecute = false };
        profile?.Environment.ApplyTo(info.Environment);

        var real = ExecutableResolver.Find(command, Get(info.Environment, "PATH"), Get(info.Environment, "PATHEXT"), shimDirectory);
        if (real is null)
        {
            Console.Error.WriteLine($"wso: `{command}` was not found on PATH (outside {shimDirectory}).");
            return 127;
        }

        info.FileName = real;
        foreach (var argument in args)
            info.ArgumentList.Add(argument);

        // Ctrl+C reaches every process on the console; the child decides what it means, we just keep waiting.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Couldn't start {real}.");
        process.WaitForExit();
        return process.ExitCode;
    }

    static int Which(string? command)
    {
        var root = ProfilesRoot();
        var resolution = ResolveHere(root);
        Console.WriteLine(resolution is null
            ? $"No profile: not inside a profile folder under {root} and {ProfileEnvironment.ProfileVariable} isn't set."
            : $"Profile \"{resolution.Marker.Name}\" ({resolution.Marker.Folder}) because {resolution.Reason}.");

        if (command is not null)
        {
            var environment = new ProcessStartInfo().Environment;
            resolution?.Marker.Environment.ApplyTo(environment);
            var real = ExecutableResolver.Find(command, Get(environment, "PATH"), Get(environment, "PATHEXT"), ProfileLayout.ShimsDirectory(root));
            Console.WriteLine(real is null ? $"`{command}` is not on PATH." : $"`{command}` runs {real}");
        }
        return 0;
    }

    static int List()
    {
        var root = ProfilesRoot();
        var markers = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root)
                .Select(d => ProfileMarker.TryRead(Path.Combine(d, ProfileLayout.MarkerFileName)))
                .OfType<ProfileMarker>()
                .ToList()
            : [];
        if (markers.Count == 0)
            Console.WriteLine($"No profiles in {root}. Open the Workspace Organizer once to create them.");
        foreach (var marker in markers)
            Console.WriteLine($"{marker.Folder,-20} {marker.Name}");
        return 0;
    }

    static int Shell(string folder)
    {
        var root = ProfilesRoot();
        RequireProfile(root, folder);
        var command = LaunchPlanner.Terminal(new ProfileLayout(root, folder), folder, null, ProcessLauncher.DetectTerminal().Shell);
        var info = ProcessLauncher.CreateStartInfo(command);
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Couldn't start PowerShell.");
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Opens <paramref name="url"/> in the browser of the profile that applies here (the caller's WSO_PROFILE
    /// or current folder), with that profile's isolated data dir. Falls back to the system default browser.
    /// </summary>
    static int Open(string url)
    {
        var root = ProfilesRoot();
        if (ResolveHere(root)?.Marker is { } marker)
        {
            var browser = marker.BrowserId is { } id ? BrowserCatalog.Detect().FirstOrDefault(b => b.Id == id) : null;
            if (browser is null)
            {
                Console.Error.WriteLine($"wso: profile \"{marker.Folder}\" has no installed browser selected; using the system default.");
            }
            else
            {
                try
                {
                    ProcessLauncher.Start(LaunchPlanner.Browser(new ProfileLayout(root, marker.Folder), marker.BrowserProfile, browser, [url]));
                    return 0;
                }
                catch (ArgumentException)
                {
                    // Not an http(s) URL (e.g. mailto:); let Windows pick the handler.
                }
            }
        }

        using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return 0;
    }

    static int PrintEnvironment(string folder)
    {
        var marker = RequireProfile(ProfilesRoot(), folder);
        foreach (var (name, value) in marker.Variables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine($"{name}={value}");
        Console.WriteLine($"PATH prefix: {string.Join(Path.PathSeparator, marker.PathPrefix)}");
        return 0;
    }

    static int InstallShims()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Shims are only supported on Windows for now.");

        var root = ProfilesRoot();
        var data = new JsonDataStore(JsonDataStore.DefaultPath).TryRead() ?? OrganizerData.CreateDefault();
        var report = ShimInstaller.Install(root, AppContext.BaseDirectory, ShimInstaller.CommandsFor(data.Workspaces.Select(w => w.EnabledTools)));
        Console.WriteLine($"Shims in {report.Directory}: {string.Join(", ", report.Commands)}");
        foreach (var warning in report.Warnings)
            Console.WriteLine("  ! " + warning);
        return 0;
    }

    static int UninstallShims()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Shims are only supported on Windows for now.");

        foreach (var warning in ShimInstaller.Uninstall(ProfilesRoot()))
            Console.WriteLine("  ! " + warning);
        Console.WriteLine("Shims removed. Open a new terminal to pick up the PATH change.");
        return 0;
    }
}

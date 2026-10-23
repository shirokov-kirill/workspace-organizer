using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using WorkspaceOrganizer.Core.Profiles;

namespace WorkspaceOrganizer.Core.Launching;

public sealed record ShimReport(string Directory, IReadOnlyList<string> Commands, IReadOnlyList<string> Warnings);

/// <summary>
/// Manages <c>&lt;profilesRoot&gt;\.shims</c>. It always holds the <c>wso</c> runtime and <c>wso-open.exe</c>
/// (profiles point <c>BROWSER</c> at it); installing shims adds one renamed copy of the <c>wso.exe</c> app host
/// per tool command and puts the folder on PATH. The host always loads <c>wso.dll</c> from its own folder, and
/// <c>wso</c> picks its mode from the name it runs under.
/// </summary>
public static class ShimInstaller
{
    public const string CliName = "wso";
    static readonly string[] RequiredCliFiles = ["wso.exe", "wso.dll", "wso.runtimeconfig.json", "WorkspaceOrganizer.Core.dll"];
    static readonly string[] OptionalCliFiles = ["wso.deps.json"];
    static readonly string[] ReservedNames = [CliName, ProfileLayout.OpenerName];

    public static IEnumerable<string> CommandsFor(IEnumerable<IReadOnlyCollection<string>?> enabledToolsPerProfile) =>
        enabledToolsPerProfile
            .SelectMany(CliToolCatalog.Enabled)
            .SelectMany(t => t.Commands)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>Copies the CLI runtime and <c>wso-open.exe</c> into the shims folder. Doesn't touch PATH.</summary>
    /// <returns>Warnings, e.g. files that were in use.</returns>
    public static IReadOnlyList<string> DeployCli(string profilesRoot, string cliDirectory)
    {
        var missing = RequiredCliFiles.Where(f => !File.Exists(Path.Combine(cliDirectory, f))).ToList();
        if (missing.Count > 0)
            throw new FileNotFoundException($"The wso CLI wasn't found in {cliDirectory} (missing {string.Join(", ", missing)}).");

        var shims = ProfileLayout.ShimsDirectory(profilesRoot);
        Directory.CreateDirectory(shims);
        var warnings = new List<string>();
        foreach (var file in RequiredCliFiles.Concat(OptionalCliFiles).Select(f => Path.Combine(cliDirectory, f)).Where(File.Exists))
            CopyIfChanged(file, Path.Combine(shims, Path.GetFileName(file)), warnings);
        CopyIfChanged(Path.Combine(cliDirectory, "wso.exe"), ProfileLayout.OpenerPath(profilesRoot), warnings);
        return warnings;
    }

    [SupportedOSPlatform("windows")]
    public static ShimReport Install(string profilesRoot, string cliDirectory, IEnumerable<string> commands)
    {
        var shims = ProfileLayout.ShimsDirectory(profilesRoot);
        var warnings = DeployCli(profilesRoot, cliDirectory).ToList();

        var wanted = commands.Where(c => !ReservedNames.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        RemoveToolShims(shims, keep: wanted, warnings);
        foreach (var command in wanted)
            CopyIfChanged(Path.Combine(cliDirectory, "wso.exe"), Path.Combine(shims, command + ".exe"), warnings);

        if (UserPath.Prepend(shims))
            warnings.Add("Added the shims folder to the start of your user PATH. Open a new terminal to pick it up.");

        // Windows searches the machine PATH before the user PATH, so a shim can't win there.
        var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        foreach (var command in wanted)
        {
            if (ExecutableResolver.Find(command, machinePath, pathExt, null) is { } real)
            {
                warnings.Add($"`{command}` is on the system PATH ({real}), which Windows searches before the shims. " +
                    "It still uses profiles in a profile terminal; for plain terminals, add this line to your PowerShell $PROFILE: " +
                    $"$env:PATH = '{shims};' + $env:PATH");
            }
        }
        return new ShimReport(shims, wanted, warnings);
    }

    /// <summary>Removes the tool shims and the PATH entry; keeps the runtime, which profiles' BROWSER still uses.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> Uninstall(string profilesRoot)
    {
        var shims = ProfileLayout.ShimsDirectory(profilesRoot);
        var warnings = new List<string>();
        UserPath.Remove(shims);
        if (Directory.Exists(shims))
            RemoveToolShims(shims, keep: [], warnings);
        return warnings;
    }

    static void RemoveToolShims(string shims, IReadOnlyCollection<string> keep, List<string> warnings)
    {
        foreach (var shim in Directory.GetFiles(shims, "*.exe")
                     .Where(f => !keep.Concat(ReservedNames).Contains(Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            TryDelete(shim, warnings);
        }
    }

    static void CopyIfChanged(string source, string target, List<string> warnings)
    {
        var from = new FileInfo(source);
        var to = new FileInfo(target);
        if (to.Exists && to.Length == from.Length && to.LastWriteTimeUtc == from.LastWriteTimeUtc)
            return;
        try
        {
            File.Copy(source, target, overwrite: true);
        }
        catch (IOException ex)
        {
            warnings.Add($"Couldn't update {target}; it is probably running. Close it and refresh the shims. ({ex.Message})");
        }
    }

    static void TryDelete(string file, List<string> warnings)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Couldn't remove the old shim {file}: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    static class UserPath
    {
        const string ValueName = "Path";

        public static bool Prepend(string directory) => Update(parts => [directory, .. parts.Where(p => !Same(p, directory))]);

        public static bool Remove(string directory) => Update(parts => parts.Where(p => !Same(p, directory)).ToList());

        static bool Update(Func<List<string>, List<string>> change)
        {
            using var key = Registry.CurrentUser.CreateSubKey("Environment", writable: true);
            var current = key.GetValue(ValueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
            var updated = string.Join(';', change(current.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList()));
            if (updated == current)
                return false;

            key.SetValue(ValueName, updated, RegistryValueKind.ExpandString);
            BroadcastEnvironmentChange();
            return true;
        }

        static bool Same(string entry, string directory)
        {
            try
            {
                return string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(entry))),
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>Tells Explorer (and so newly started terminals) that the environment changed.</summary>
        static void BroadcastEnvironmentChange() =>
            SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "Environment", 0x0002, 5000, out _);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    }
}

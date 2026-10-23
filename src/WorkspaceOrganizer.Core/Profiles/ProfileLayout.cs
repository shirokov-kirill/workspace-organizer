namespace WorkspaceOrganizer.Core.Profiles;

/// <summary>Paths inside one profile folder, <c>&lt;profilesRoot&gt;\&lt;folder&gt;</c>.</summary>
public sealed class ProfileLayout(string profilesRoot, string folder)
{
    public const string MarkerFileName = ".wso-profile.json";
    public const string ShimsFolderName = ".shims";

    public string ProfilesRoot { get; } = Path.GetFullPath(profilesRoot);
    public string Folder { get; } = folder;
    public string Root => Path.Combine(ProfilesRoot, Folder);
    public string Marker => Path.Combine(Root, MarkerFileName);
    public string ActivatePowerShell => Path.Combine(Root, "activate.ps1");
    public string ActivateCmd => Path.Combine(Root, "activate.cmd");
    public string Bin => Path.Combine(Root, "bin");
    public string Home => Path.Combine(Root, "home");
    public string Config(string toolId) => Path.Combine(Root, "config", SafeSegment(toolId));
    public string Browser(string browserId) => Path.Combine(Root, "browser", SafeSegment(browserId));
    public string App(string appName) => Path.Combine(Root, "apps", SafeSegment(appName));

    public static string ShimsDirectory(string profilesRoot) => Path.Combine(Path.GetFullPath(profilesRoot), ShimsFolderName);

    /// <summary>
    /// The program profiles set as <c>BROWSER</c>: a copy of <c>wso.exe</c> that opens a URL in the profile's
    /// browser. Always deployed next to the shims, whether or not shims are installed.
    /// </summary>
    public static string OpenerPath(string profilesRoot) =>
        Path.Combine(ShimsDirectory(profilesRoot), OperatingSystem.IsWindows() ? OpenerName + ".exe" : OpenerName);

    public const string OpenerName = "wso-open";

    /// <summary>Lowercase, no whitespace or path characters. Leading dots are stripped so folders can't clash with <c>.shims</c>.</summary>
    public static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Trim()
            .Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c))
            .ToArray()).TrimStart('.');
        return safe.Length == 0 ? "default" : safe;
    }

    public static bool IsValidFolder(string folder) => folder == SafeSegment(folder) && folder is not ("." or "..");

    public static string UniqueFolder(string name, ICollection<string> taken)
    {
        var baseName = SafeSegment(name);
        var candidate = baseName;
        for (var i = 2; taken.Contains(candidate); i++)
            candidate = $"{baseName}-{i}";
        return candidate;
    }
}

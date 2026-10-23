namespace WorkspaceOrganizer.Core.Profiles;

public sealed record ProfileResolution(ProfileMarker Marker, string Reason);

/// <summary>Decides which profile a program started "here" belongs to.</summary>
public static class ProfileResolver
{
    /// <summary>
    /// Priority: an explicit <see cref="ProfileEnvironment.ProfileVariable"/> (set by a profile terminal),
    /// then the current folder being inside <c>&lt;profilesRoot&gt;\&lt;profile&gt;</c>. Null means "no profile".
    /// </summary>
    public static ProfileResolution? Resolve(string profilesRoot, string currentDirectory, string? profileVariable)
    {
        if (!string.IsNullOrWhiteSpace(profileVariable) && ProfileLayout.IsValidFolder(profileVariable)
            && ProfileMarker.TryRead(new ProfileLayout(profilesRoot, profileVariable).Marker) is { } named)
        {
            return new ProfileResolution(named, $"{ProfileEnvironment.ProfileVariable}={profileVariable}");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profilesRoot)) + Path.DirectorySeparatorChar;
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory)) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!current.StartsWith(root, comparison) || current.Length == root.Length)
            return null;

        var folder = current[root.Length..].Split(Path.DirectorySeparatorChar)[0];
        var layout = new ProfileLayout(profilesRoot, folder);
        return ProfileMarker.TryRead(layout.Marker) is { } marker
            ? new ProfileResolution(marker, $"current folder is inside {layout.Root}")
            : null;
    }
}

public static class ExecutableResolver
{
    /// <summary>
    /// Finds <paramref name="command"/> on <paramref name="path"/> the way Windows does (trying PATHEXT
    /// extensions), skipping <paramref name="excludeDirectory"/> so a shim never finds itself.
    /// </summary>
    public static string? Find(string command, string? path, string? pathExt, string? excludeDirectory)
    {
        var extensions = OperatingSystem.IsWindows() && !Path.HasExtension(command)
            ? (pathExt ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        var excluded = excludeDirectory is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(excludeDirectory));

        foreach (var directory in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string full;
            try
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim().Trim('"')));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }
            if (excluded is not null && string.Equals(full, excluded, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(full, command + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}

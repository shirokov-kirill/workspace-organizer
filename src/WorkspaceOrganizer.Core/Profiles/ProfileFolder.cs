using Microsoft.VisualBasic.FileIO;

namespace WorkspaceOrganizer.Core.Profiles;

public sealed record FolderSize(long Files, long Bytes);

/// <summary>Deleting and measuring a whole profile folder.</summary>
public static class ProfileFolder
{
    /// <summary>
    /// Deletes <c>&lt;profilesRoot&gt;\&lt;folder&gt;</c>, moving it to the Recycle Bin when asked (Windows only;
    /// elsewhere it's deleted permanently). Refuses anything that isn't a profile folder directly under the root.
    /// </summary>
    public static void Delete(ProfileLayout layout, bool toRecycleBin)
    {
        var root = Path.TrimEndingDirectorySeparator(layout.ProfilesRoot);
        var target = Path.GetFullPath(layout.Root);
        if (!ProfileLayout.IsValidFolder(layout.Folder)
            || layout.Folder == ProfileLayout.ShimsFolderName
            || !string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to delete {target}: it isn't a profile folder directly inside {root}.");
        }

        if (!Directory.Exists(target))
            return;
        if (toRecycleBin && OperatingSystem.IsWindows())
            FileSystem.DeleteDirectory(target, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            Directory.Delete(target, recursive: true);
    }

    /// <summary>Counts files and bytes below <paramref name="path"/>, skipping unreadable entries and links.</summary>
    public static Task<FolderSize> MeasureAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!Directory.Exists(path))
            return new FolderSize(0, 0);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        long files = 0, bytes = 0;
        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            files++;
            bytes += file.Length;
        }
        return new FolderSize(files, bytes);
    }, cancellationToken);
}

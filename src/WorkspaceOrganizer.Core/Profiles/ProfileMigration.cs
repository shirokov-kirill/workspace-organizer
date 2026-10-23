using WorkspaceOrganizer.Core.Models;

namespace WorkspaceOrganizer.Core.Profiles;

public static class ProfileMigration
{
    /// <summary>Where v1 kept isolated browser data: <c>&lt;legacyRoot&gt;\&lt;workspaceId&gt;\&lt;browserId&gt;</c>.</summary>
    public static string LegacyProfilesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkspaceOrganizer", "Profiles");

    /// <summary>Moves v1 browser data into each profile's <c>browser\</c> folder. Returns problems, if any.</summary>
    public static IReadOnlyList<string> MoveLegacyBrowserData(OrganizerData data, string legacyRoot)
    {
        var problems = new List<string>();
        foreach (var workspace in data.Workspaces)
        {
            var legacy = Path.Combine(legacyRoot, workspace.Id);
            if (!Directory.Exists(legacy))
                continue;

            var layout = new ProfileLayout(data.Settings.ProfilesRoot, workspace.Folder);
            foreach (var source in Directory.GetDirectories(legacy))
            {
                var target = layout.Browser(Path.GetFileName(source));
                if (Directory.Exists(target))
                    continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Directory.Move(source, target);
                }
                catch (IOException ex)
                {
                    problems.Add($"Couldn't move browser data {source} → {target} (close the browser and restart): {ex.Message}");
                }
            }
            if (Directory.GetFileSystemEntries(legacy).Length == 0)
                Directory.Delete(legacy);
        }
        return problems;
    }
}

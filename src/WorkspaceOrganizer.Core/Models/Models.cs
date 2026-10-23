using WorkspaceOrganizer.Core.Profiles;

namespace WorkspaceOrganizer.Core.Models;

/// <summary>Everything the organizer persists. Credentials are never stored here — only metadata.</summary>
public sealed class OrganizerData
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public OrganizerSettings Settings { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = [];
    public List<Account> Accounts { get; set; } = [];
    public List<Subscription> Subscriptions { get; set; } = [];

    public static OrganizerData CreateDefault() => new OrganizerData
    {
        Workspaces =
        [
            new Workspace { Name = "Work", Color = "#2563EB" },
            new Workspace { Name = "Personal", Color = "#16A34A" },
        ],
    }.Normalize();

    /// <summary>
    /// Repairs data that would otherwise be silently dropped on the next save:
    /// guarantees at least one workspace, gives every workspace a unique profile folder,
    /// and reassigns orphaned items to the first workspace.
    /// </summary>
    public OrganizerData Normalize()
    {
        if (Workspaces.Count == 0)
            Workspaces.Add(new Workspace { Name = "Personal", Color = "#16A34A" });

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in Workspaces)
        {
            if (string.IsNullOrWhiteSpace(workspace.Folder) || !ProfileLayout.IsValidFolder(workspace.Folder) || !folders.Add(workspace.Folder))
            {
                workspace.Folder = ProfileLayout.UniqueFolder(workspace.Name, folders);
                folders.Add(workspace.Folder);
            }
        }

        var known = Workspaces.Select(w => w.Id).ToHashSet();
        var fallback = Workspaces[0].Id;
        foreach (var account in Accounts.Where(a => !known.Contains(a.WorkspaceId)))
            account.WorkspaceId = fallback;
        foreach (var subscription in Subscriptions.Where(s => !known.Contains(s.WorkspaceId)))
            subscription.WorkspaceId = fallback;
        return this;
    }
}

public sealed class OrganizerSettings
{
    public static string DefaultProfilesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Profiles");

    /// <summary>Each workspace owns the folder <c>&lt;ProfilesRoot&gt;\&lt;Workspace.Folder&gt;</c>.</summary>
    public string ProfilesRoot { get; set; } = DefaultProfilesRoot;
}

public sealed class Workspace
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";

    /// <summary>Profile folder name under the profiles root. Kept when the workspace is renamed.</summary>
    public string Folder { get; set; } = "";

    public string Color { get; set; } = "#64748B";
    public string? BrowserId { get; set; }

    /// <summary>Ids from <see cref="CliToolCatalog"/>; null means all tools, including ones added later.</summary>
    public List<string>? EnabledTools { get; set; }

    /// <summary>Also point USERPROFILE/HOME/APPDATA/LOCALAPPDATA into the profile, for tools without their own setting.</summary>
    public bool RedirectHome { get; set; }

    public Dictionary<string, string> ExtraEnvironment { get; set; } = [];

    /// <summary>
    /// Null or empty: an isolated browser data directory owned by the organizer.
    /// Otherwise the name of an existing profile inside the browser (e.g. "Profile 1" for Chrome).
    /// </summary>
    public string? BrowserProfile { get; set; }

    public List<AppShortcut> Apps { get; set; } = [];
}

public sealed class AppShortcut
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>Command-line arguments; <c>{profileDir}</c> expands to a per-workspace data directory.</summary>
    public string Arguments { get; set; } = "";
}

public sealed class Account
{
    public string Id { get; set; } = Ids.New();
    public string WorkspaceId { get; set; } = "";
    public string Service { get; set; } = "";
    public string Url { get; set; } = "";
    public string Login { get; set; } = "";
    public string TwoFactor { get; set; } = "";
    public string Notes { get; set; } = "";
}

public enum BillingCycle { Weekly, Monthly, Quarterly, Yearly }

public sealed class Subscription
{
    public string Id { get; set; } = Ids.New();
    public string WorkspaceId { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "EUR";
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;

    /// <summary>Any known renewal date; later occurrences are derived from it.</summary>
    public DateOnly? NextRenewal { get; set; }

    public bool Active { get; set; } = true;
    public string Notes { get; set; } = "";
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}

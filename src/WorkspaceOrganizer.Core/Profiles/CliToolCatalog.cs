namespace WorkspaceOrganizer.Core.Profiles;

/// <summary>How well a tool's login can be separated per profile.</summary>
public enum ToolIsolation
{
    /// <summary>The tool reads a config-location env var, so each profile gets its own login.</summary>
    EnvVar,
    /// <summary>Only separated when the profile redirects the whole home folder.</summary>
    HomeRedirect,
    /// <summary>Login lives somewhere env vars can't reach (e.g. an OS keychain shared by all profiles).</summary>
    Unsupported,
}

/// <param name="Commands">Executable names that get a shim.</param>
/// <param name="Environment">Env vars that point the tool's config into the profile.</param>
/// <param name="Seed">Creates starter config once; must never overwrite existing files.</param>
public sealed record CliTool(
    string Id,
    string Name,
    IReadOnlyList<string> Commands,
    ToolIsolation Isolation,
    Func<ProfileLayout, IReadOnlyDictionary<string, string>> Environment,
    Action<ProfileLayout>? Seed = null,
    string? Note = null);

public static class CliToolCatalog
{
    public static IReadOnlyList<CliTool> All { get; } =
    [
        new("claude", "Claude Code", ["claude"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = l.Config("claude") }),
        new("gh", "GitHub CLI", ["gh"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["GH_CONFIG_DIR"] = l.Config("gh") },
            Note: "If two profiles share a GitHub account, its token in Windows Credential Manager is shared too; `gh auth login --insecure-storage` keeps it in the profile."),
        new("git", "Git", ["git"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["GIT_CONFIG_GLOBAL"] = GitConfigPath(l) },
            SeedGitConfig,
            "Edit config\\git\\.gitconfig to set this profile's user.name / user.email."),
        new("aws", "AWS CLI", ["aws"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string>
            {
                ["AWS_CONFIG_FILE"] = Path.Combine(l.Config("aws"), "config"),
                ["AWS_SHARED_CREDENTIALS_FILE"] = Path.Combine(l.Config("aws"), "credentials"),
            }),
        new("az", "Azure CLI", ["az"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["AZURE_CONFIG_DIR"] = l.Config("az") }),
        new("gcloud", "Google Cloud CLI", ["gcloud"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["CLOUDSDK_CONFIG"] = l.Config("gcloud") }),
        new("npm", "npm", ["npm", "npx"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["NPM_CONFIG_USERCONFIG"] = Path.Combine(l.Config("npm"), ".npmrc") }),
        new("docker", "Docker CLI", ["docker"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["DOCKER_CONFIG"] = l.Config("docker") }),
        new("kubectl", "kubectl", ["kubectl"], ToolIsolation.EnvVar,
            l => new Dictionary<string, string> { ["KUBECONFIG"] = Path.Combine(l.Config("kubectl"), "config") }),
    ];

    public static IEnumerable<CliTool> Enabled(IReadOnlyCollection<string>? enabledIds) =>
        enabledIds is null ? All : All.Where(t => enabledIds.Contains(t.Id));

    static string GitConfigPath(ProfileLayout layout) => Path.Combine(layout.Config("git"), ".gitconfig");

    /// <summary>
    /// Includes the user's real global config (aliases, LFS, …) and gives Git Credential Manager a
    /// per-profile namespace so stored credentials don't collide between profiles.
    /// </summary>
    static void SeedGitConfig(ProfileLayout layout)
    {
        var path = GitConfigPath(layout);
        if (File.Exists(path))
            return;

        var realGlobal = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".gitconfig");
        var lines = new List<string> { $"# Git config for the \"{layout.Folder}\" profile. The organizer creates this once and never overwrites it." };
        if (File.Exists(realGlobal))
            lines.AddRange(["[include]", $"\tpath = {realGlobal.Replace('\\', '/')}"]);
        lines.AddRange(
        [
            "[credential]",
            $"\tnamespace = wso-{layout.Folder}",
            "# [user]",
            "# \tname = ",
            "# \temail = ",
        ]);
        File.WriteAllLines(path, lines);
    }
}

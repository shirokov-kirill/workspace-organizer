using WorkspaceOrganizer.Core.Models;

namespace WorkspaceOrganizer.Core.Profiles;

/// <summary>The env vars that make programs use one profile's logins.</summary>
public sealed record ProfileEnvironment(IReadOnlyDictionary<string, string> Variables, IReadOnlyList<string> PathPrefix)
{
    public const string ProfileVariable = "WSO_PROFILE";

    /// <summary>Read by Claude Code, gh and others instead of the system default browser when they open a URL.</summary>
    public const string BrowserVariable = "BROWSER";

    /// <summary>
    /// Precedence, lowest to highest: home redirect and browser, per-tool settings, the profile's extra
    /// variables, then <see cref="ProfileVariable"/>, which always names the profile.
    /// </summary>
    public static ProfileEnvironment Build(Workspace workspace, ProfileLayout layout)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (workspace.RedirectHome)
        {
            variables["USERPROFILE"] = layout.Home;
            variables["HOME"] = layout.Home;
            variables["APPDATA"] = Path.Combine(layout.Home, "AppData", "Roaming");
            variables["LOCALAPPDATA"] = Path.Combine(layout.Home, "AppData", "Local");
        }
        // Without this, login flows (`claude` → "log in with browser") open the system default browser,
        // i.e. whichever account is signed in there, not this profile's.
        if (workspace.BrowserId is not null)
            variables[BrowserVariable] = ProfileLayout.OpenerPath(layout.ProfilesRoot);
        foreach (var tool in CliToolCatalog.Enabled(workspace.EnabledTools))
        {
            foreach (var (name, value) in tool.Environment(layout))
                variables[name] = value;
        }
        foreach (var (name, value) in workspace.ExtraEnvironment)
            variables[name] = value;
        variables[ProfileVariable] = layout.Folder;

        return new ProfileEnvironment(variables, [layout.Bin]);
    }

    /// <summary>Applies the variables and PATH prefix to a process environment (e.g. <c>ProcessStartInfo.Environment</c>).</summary>
    public void ApplyTo(IDictionary<string, string?> environment)
    {
        foreach (var (name, value) in Variables)
            environment[name] = value;

        var pathKey = environment.Keys.FirstOrDefault(k => string.Equals(k, "PATH", StringComparison.OrdinalIgnoreCase)) ?? "PATH";
        environment.TryGetValue(pathKey, out var path);
        var existing = (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !PathPrefix.Contains(p, StringComparer.OrdinalIgnoreCase));
        environment[pathKey] = string.Join(Path.PathSeparator, PathPrefix.Concat(existing));
    }
}

/// <summary>Round-trips extra env vars as <c>KEY=VALUE</c> lines for editing.</summary>
public static class EnvironmentText
{
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var separator = line.IndexOf('=');
            if (line.Length == 0 || line.StartsWith('#') || separator <= 0)
                continue;
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    public static string Format(IReadOnlyDictionary<string, string> variables) =>
        string.Join(Environment.NewLine, variables.Select(kv => $"{kv.Key}={kv.Value}"));
}

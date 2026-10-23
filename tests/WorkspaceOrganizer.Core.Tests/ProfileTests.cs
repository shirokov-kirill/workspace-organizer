using WorkspaceOrganizer.Core.Launching;
using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Profiles;
using WorkspaceOrganizer.Core.Storage;

namespace WorkspaceOrganizer.Core.Tests;

public sealed class ProfileTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "wso-profile-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    ProfileLayout Layout(string folder) => new(_root, folder);

    static Workspace Work(Action<Workspace>? configure = null)
    {
        var workspace = new Workspace { Name = "Work", Folder = "work" };
        configure?.Invoke(workspace);
        return workspace;
    }

    OrganizerData Data(params Workspace[] workspaces) => new() { Settings = { ProfilesRoot = _root }, Workspaces = [.. workspaces] };

    // --- environment

    [Fact]
    public void Environment_points_enabled_tools_into_the_profile()
    {
        var layout = Layout("work");

        var env = ProfileEnvironment.Build(Work(w => w.EnabledTools = ["claude", "aws"]), layout);

        Assert.Equal(Path.Combine(layout.Root, "config", "claude"), env.Variables["CLAUDE_CONFIG_DIR"]);
        Assert.Equal(Path.Combine(layout.Root, "config", "aws", "credentials"), env.Variables["AWS_SHARED_CREDENTIALS_FILE"]);
        Assert.False(env.Variables.ContainsKey("GH_CONFIG_DIR"));
        Assert.False(env.Variables.ContainsKey("USERPROFILE"));
        Assert.Equal("work", env.Variables[ProfileEnvironment.ProfileVariable]);
        Assert.Equal([layout.Bin], env.PathPrefix);
    }

    [Fact]
    public void Null_enabled_tools_means_all_tools()
    {
        var env = ProfileEnvironment.Build(Work(), Layout("work"));

        Assert.All(CliToolCatalog.All, tool => Assert.All(tool.Environment(Layout("work")).Keys, key => Assert.Contains(key, env.Variables.Keys)));
    }

    [Fact]
    public void Precedence_is_home_redirect_then_tools_then_extras_then_profile_name()
    {
        var layout = Layout("work");
        var workspace = Work(w =>
        {
            w.RedirectHome = true;
            w.ExtraEnvironment = new() { ["CLAUDE_CONFIG_DIR"] = @"D:\custom", ["APPDATA"] = @"D:\appdata", [ProfileEnvironment.ProfileVariable] = "spoof" };
        });

        var env = ProfileEnvironment.Build(workspace, layout);

        Assert.Equal(layout.Home, env.Variables["USERPROFILE"]);
        Assert.Equal(@"D:\appdata", env.Variables["APPDATA"]);
        Assert.Equal(@"D:\custom", env.Variables["CLAUDE_CONFIG_DIR"]);
        Assert.Equal("work", env.Variables[ProfileEnvironment.ProfileVariable]);
    }

    [Fact]
    public void Browser_variable_points_at_the_opener_only_when_a_browser_is_chosen()
    {
        var layout = Layout("work");

        var withBrowser = ProfileEnvironment.Build(Work(w => w.BrowserId = "chrome"), layout);
        var without = ProfileEnvironment.Build(Work(), layout);
        var overridden = ProfileEnvironment.Build(Work(w => { w.BrowserId = "chrome"; w.ExtraEnvironment = new() { ["BROWSER"] = "custom.exe" }; }), layout);

        Assert.Equal(Path.Combine(_root, ".shims", "wso-open.exe"), withBrowser.Variables[ProfileEnvironment.BrowserVariable]);
        Assert.False(without.Variables.ContainsKey(ProfileEnvironment.BrowserVariable));
        Assert.Equal("custom.exe", overridden.Variables[ProfileEnvironment.BrowserVariable]);
    }

    [Fact]
    public void Marker_carries_the_browser_choice()
    {
        ProfileWriter.WriteAll(Data(Work(w => { w.BrowserId = "edge"; w.BrowserProfile = " Profile 1 "; }), new Workspace { Name = "P", Folder = "p", BrowserProfile = "  " }));

        var work = ProfileMarker.TryRead(Layout("work").Marker);
        var personal = ProfileMarker.TryRead(Layout("p").Marker);

        Assert.Equal(("edge", "Profile 1"), (work?.BrowserId, work?.BrowserProfile));
        Assert.Equal((null, null), (personal?.BrowserId, personal?.BrowserProfile));
    }

    [Fact]
    public void DeployCli_copies_runtime_and_opener_without_tool_shims()
    {
        var cli = Directory.CreateDirectory(Path.Combine(_root, "cli")).FullName;
        foreach (var file in new[] { "wso.exe", "wso.dll", "wso.runtimeconfig.json", "WorkspaceOrganizer.Core.dll" })
            File.WriteAllText(Path.Combine(cli, file), file);

        var warnings = ShimInstaller.DeployCli(_root, cli);

        Assert.Empty(warnings);
        Assert.Equal("wso.exe", File.ReadAllText(ProfileLayout.OpenerPath(_root)));
        Assert.Equal(["wso-open.exe", "wso.exe"], Directory.GetFiles(ProfileLayout.ShimsDirectory(_root), "*.exe").Select(Path.GetFileName).Order());
    }

    [Fact]
    public void DeployCli_reports_missing_cli()
    {
        Assert.Throws<FileNotFoundException>(() => ShimInstaller.DeployCli(_root, Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName));
    }

    [Fact]
    public void ApplyTo_prepends_path_once()
    {
        var env = new ProfileEnvironment(new Dictionary<string, string> { ["A"] = "1" }, [@"C:\p\bin"]);
        var process = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Path"] = @"C:\p\bin;C:\Windows" };

        env.ApplyTo(process);
        env.ApplyTo(process);

        Assert.Equal("1", process["A"]);
        Assert.Equal(@"C:\p\bin;C:\Windows", process["Path"]);
    }

    [Fact]
    public void EnvironmentText_round_trips_and_skips_junk()
    {
        var parsed = EnvironmentText.Parse("# comment\nAWS_PROFILE = work\n\nnot a pair\n=nokey\nURL=https://x?a=b\r\n");

        Assert.Equal(new Dictionary<string, string> { ["AWS_PROFILE"] = "work", ["URL"] = "https://x?a=b" }, parsed);
        Assert.Equal(parsed, EnvironmentText.Parse(EnvironmentText.Format(parsed)));
    }

    // --- writer

    [Fact]
    public void WriteAll_creates_marker_scripts_and_tool_folders()
    {
        ProfileWriter.WriteAll(Data(Work(w => w.EnabledTools = ["claude", "git"])));

        var layout = Layout("work");
        var marker = ProfileMarker.TryRead(layout.Marker);
        Assert.NotNull(marker);
        Assert.Equal(layout.Config("claude"), marker.Variables["CLAUDE_CONFIG_DIR"]);
        Assert.True(Directory.Exists(layout.Config("claude")));
        Assert.True(Directory.Exists(layout.Bin));
        Assert.Contains("namespace = wso-work", File.ReadAllText(Path.Combine(layout.Config("git"), ".gitconfig")));
        Assert.Contains("CLAUDE_CONFIG_DIR", File.ReadAllText(layout.ActivatePowerShell));
        Assert.Contains("prompt [work] $P$G", File.ReadAllText(layout.ActivateCmd));
    }

    [Fact]
    public void Seeding_never_overwrites_user_edits()
    {
        var data = Data(Work(w => w.EnabledTools = ["git"]));
        ProfileWriter.WriteAll(data);
        var gitconfig = Path.Combine(Layout("work").Config("git"), ".gitconfig");
        File.WriteAllText(gitconfig, "[user]\n\temail = me@work.example\n");

        ProfileWriter.WriteAll(data);

        Assert.Equal("[user]\n\temail = me@work.example\n", File.ReadAllText(gitconfig));
    }

    [Fact]
    public void Removed_workspace_loses_marker_but_keeps_files()
    {
        ProfileWriter.WriteAll(Data(Work(), new Workspace { Name = "Old", Folder = "old" }));
        var old = Layout("old");
        File.WriteAllText(Path.Combine(old.Root, "notes.txt"), "keep me");

        ProfileWriter.WriteAll(Data(Work()));

        Assert.False(File.Exists(old.Marker));
        Assert.False(File.Exists(old.ActivatePowerShell));
        Assert.True(File.Exists(Path.Combine(old.Root, "notes.txt")));
        Assert.True(File.Exists(Layout("work").Marker));
    }

    [Fact]
    public void PowerShell_script_quotes_paths_with_spaces_and_apostrophes()
    {
        var env = new ProfileEnvironment(new Dictionary<string, string> { ["X"] = @"C:\Users\O'Brien\My Profiles" }, [@"C:\a b\bin"]);

        var script = ProfileWriter.PowerShellScript("work", "Work", env);

        Assert.Contains(@"Set-Item -Path 'env:X' -Value 'C:\Users\O''Brien\My Profiles'", script);
        Assert.Contains(@"$env:PATH = 'C:\a b\bin;' + ((($env:PATH -split ';') -ne 'C:\a b\bin') -join ';')", script);
    }

    [Fact]
    public void Cmd_script_escapes_percent()
    {
        var script = ProfileWriter.CmdScript("work", "Work", new ProfileEnvironment(new Dictionary<string, string> { ["X"] = "100%" }, []));

        Assert.Contains("set \"X=100%%\"", script);
    }

    // --- resolver

    [Fact]
    public void Resolver_finds_profile_from_nested_folder()
    {
        ProfileWriter.WriteAll(Data(Work()));
        var nested = Directory.CreateDirectory(Path.Combine(_root, "work", "repos", "app", "src")).FullName;

        var resolution = ProfileResolver.Resolve(_root, nested, null);

        Assert.Equal("work", resolution?.Marker.Folder);
    }

    [Fact]
    public void Resolver_prefers_profile_variable_over_folder()
    {
        ProfileWriter.WriteAll(Data(Work(), new Workspace { Name = "Personal", Folder = "personal" }));

        var resolution = ProfileResolver.Resolve(_root, Path.Combine(_root, "work"), "personal");

        Assert.Equal("personal", resolution?.Marker.Folder);
        Assert.Contains("WSO_PROFILE", resolution?.Reason);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\elsewhere")]
    [InlineData("missing")]
    public void Resolver_ignores_bad_profile_variable_and_falls_back_to_folder(string variable)
    {
        ProfileWriter.WriteAll(Data(Work()));

        Assert.Equal("work", ProfileResolver.Resolve(_root, Path.Combine(_root, "work"), variable)?.Marker.Folder);
        Assert.Null(ProfileResolver.Resolve(_root, Path.GetTempPath(), variable));
    }

    [Fact]
    public void Resolver_returns_null_outside_root_at_root_and_without_marker()
    {
        ProfileWriter.WriteAll(Data(Work()));
        var unmarked = Directory.CreateDirectory(Path.Combine(_root, "random")).FullName;
        var sibling = _root + "-sibling";

        Assert.Null(ProfileResolver.Resolve(_root, _root, null));
        Assert.Null(ProfileResolver.Resolve(_root, unmarked, null));
        Assert.Null(ProfileResolver.Resolve(_root, sibling, null));
    }

    // --- deleting

    [Fact]
    public void Delete_removes_only_that_profile_folder()
    {
        ProfileWriter.WriteAll(Data(Work(), new Workspace { Name = "P", Folder = "p" }));
        Directory.CreateDirectory(Path.Combine(_root, "work", "repos", "app"));
        File.WriteAllText(Path.Combine(_root, "work", "repos", "app", "file.txt"), "x");
        var shims = Directory.CreateDirectory(ProfileLayout.ShimsDirectory(_root)).FullName;

        ProfileFolder.Delete(Layout("work"), toRecycleBin: false);

        Assert.False(Directory.Exists(Layout("work").Root));
        Assert.True(File.Exists(Layout("p").Marker));
        Assert.True(Directory.Exists(shims));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".shims")]
    [InlineData(@"..\outside")]
    [InlineData(@"work\nested")]
    public void Delete_refuses_anything_but_a_profile_folder(string folder)
    {
        Directory.CreateDirectory(Path.Combine(_root, "work", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, ".shims"));

        Assert.Throws<InvalidOperationException>(() => ProfileFolder.Delete(Layout(folder), toRecycleBin: false));
        Assert.True(Directory.Exists(Path.Combine(_root, "work", "nested")));
        Assert.True(Directory.Exists(Path.Combine(_root, ".shims")));
    }

    [Fact]
    public void Delete_of_missing_folder_does_nothing()
    {
        ProfileFolder.Delete(Layout("never-created"), toRecycleBin: false);
    }

    [Fact]
    public async Task MeasureAsync_counts_nested_files()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "work", "a", "b")).FullName;
        File.WriteAllText(Path.Combine(_root, "work", "one.txt"), "123");
        File.WriteAllText(Path.Combine(folder, "two.txt"), "45");

        var size = await ProfileFolder.MeasureAsync(Layout("work").Root, CancellationToken.None);

        Assert.Equal(new FolderSize(2, 5), size);
        Assert.Equal(new FolderSize(0, 0), await ProfileFolder.MeasureAsync(Layout("missing").Root, CancellationToken.None));
    }

    // --- executables

    [Fact]
    public void ExecutableResolver_skips_the_shims_folder()
    {
        var shims = Directory.CreateDirectory(Path.Combine(_root, ".shims")).FullName;
        var real = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;
        File.WriteAllText(Path.Combine(shims, "tool.exe"), "");
        File.WriteAllText(Path.Combine(real, "tool.cmd"), "");
        var path = string.Join(Path.PathSeparator, shims, real);

        Assert.Equal(Path.Combine(real, "tool.cmd"), ExecutableResolver.Find("tool", path, ".EXE;.CMD", shims + Path.DirectorySeparatorChar), ignoreCase: true);
        Assert.Equal(Path.Combine(shims, "tool.exe"), ExecutableResolver.Find("tool", path, ".EXE;.CMD", null), ignoreCase: true);
        Assert.Null(ExecutableResolver.Find("nope", path, ".EXE", null));
    }

    // --- folders, migration

    [Theory]
    [InlineData("Work", "work")]
    [InlineData("  Client A / B ", "client-a---b")]
    [InlineData(".shims", "shims")]
    [InlineData("...", "default")]
    public void SafeSegment_makes_folder_names(string name, string expected)
    {
        Assert.Equal(expected, ProfileLayout.SafeSegment(name));
    }

    [Fact]
    public void Normalize_assigns_unique_folders_and_keeps_existing_ones()
    {
        var data = new OrganizerData
        {
            Workspaces =
            [
                new Workspace { Name = "Renamed", Folder = "work" },
                new Workspace { Name = "Work" },
                new Workspace { Name = "Dup", Folder = "WORK" },
                new Workspace { Name = "Bad", Folder = @"..\x" },
            ],
        }.Normalize();

        Assert.Equal(["work", "work-2", "dup", "bad"], data.Workspaces.Select(w => w.Folder));
    }

    [Fact]
    public void Version1_data_file_loads_with_folders_and_default_root()
    {
        var file = Path.Combine(_root, "organizer.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(file, """{ "Version": 1, "Workspaces": [ { "Id": "a1", "Name": "Work" }, { "Id": "b2", "Name": "Personal" } ] }""");

        var data = new JsonDataStore(file).Load(out var warning);

        Assert.Null(warning);
        Assert.Equal(["work", "personal"], data.Workspaces.Select(w => w.Folder));
        Assert.Equal(OrganizerSettings.DefaultProfilesRoot, data.Settings.ProfilesRoot);
        Assert.Null(data.Workspaces[0].EnabledTools);
    }

    [Fact]
    public void Legacy_browser_data_moves_into_profile()
    {
        var legacy = Path.Combine(_root, "legacy");
        Directory.CreateDirectory(Path.Combine(legacy, "id1", "chrome"));
        File.WriteAllText(Path.Combine(legacy, "id1", "chrome", "Cookies"), "c");
        var data = Data(new Workspace { Id = "id1", Name = "Work", Folder = "work" });

        var problems = ProfileMigration.MoveLegacyBrowserData(data, legacy);

        Assert.Empty(problems);
        Assert.Equal("c", File.ReadAllText(Path.Combine(Layout("work").Browser("chrome"), "Cookies")));
        Assert.False(Directory.Exists(Path.Combine(legacy, "id1")));
    }

    [Fact]
    public void Terminal_runs_activate_script_in_new_window()
    {
        var layout = Layout("work");

        var wt = LaunchPlanner.Terminal(layout, "Work", @"C:\wt.exe", "pwsh.exe");
        var plain = LaunchPlanner.Terminal(layout, "Work", null, "pwsh.exe");

        Assert.Equal(@"C:\wt.exe", wt.FileName);
        Assert.Equal(["-w", "new", "--title", "Work", "-d", layout.Root, "pwsh.exe", "-NoExit", "-ExecutionPolicy", "Bypass", "-File", layout.ActivatePowerShell], wt.Arguments);
        Assert.Equal("pwsh.exe", plain.FileName);
        Assert.Equal(["-NoExit", "-ExecutionPolicy", "Bypass", "-File", layout.ActivatePowerShell], plain.Arguments);
    }
}

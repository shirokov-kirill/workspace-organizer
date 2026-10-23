using WorkspaceOrganizer.Core.Launching;
using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Profiles;

namespace WorkspaceOrganizer.Core.Tests;

public class LaunchPlannerTests
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "wso-profiles");
    static ProfileLayout Layout(string folder) => new(Root, folder);
    static readonly ProfileEnvironment NoEnvironment = new(new Dictionary<string, string>(), []);
    static readonly BrowserInfo Chrome = new("chrome", "Google Chrome", BrowserEngine.Chromium, @"C:\chrome.exe");
    static readonly BrowserInfo Firefox = new("firefox", "Mozilla Firefox", BrowserEngine.Firefox, @"C:\firefox.exe");

    [Fact]
    public void Chromium_without_profile_uses_isolated_data_dir_per_workspace()
    {
        var work = LaunchPlanner.Browser(Layout("work1"), null, Chrome, ["mail.example.com"]);
        var home = LaunchPlanner.Browser(Layout("home1"), "  ", Chrome, []);

        var workDir = Path.Combine(Root, "work1", "browser", "chrome");
        Assert.Equal(workDir, work.ProfileDirectory);
        Assert.Equal([$"--user-data-dir={workDir}", "--no-first-run", "https://mail.example.com/"], work.Arguments);
        Assert.NotEqual(work.ProfileDirectory, home.ProfileDirectory);
    }

    [Fact]
    public void Chromium_with_profile_name_uses_existing_browser_profile()
    {
        var command = LaunchPlanner.Browser(Layout("w"), " Profile 2 ", Chrome, []);

        Assert.Null(command.ProfileDirectory);
        Assert.Equal(["--profile-directory=Profile 2"], command.Arguments);
    }

    [Fact]
    public void Firefox_uses_profile_path_or_named_profile()
    {
        var isolated = LaunchPlanner.Browser(Layout("w"), null, Firefox, []);
        var named = LaunchPlanner.Browser(Layout("w"), "work", Firefox, []);

        Assert.Equal(["-profile", Path.Combine(Root, "w", "browser", "firefox")], isolated.Arguments);
        Assert.Equal(["-P", "work"], named.Arguments);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows")]
    public void Rejects_urls_that_are_not_http(string url)
    {
        Assert.Throws<ArgumentException>(() => LaunchPlanner.Browser(Layout("w"), null, Chrome, [url]));
    }

    [Fact]
    public void Url_can_never_be_passed_as_a_browser_flag()
    {
        var command = LaunchPlanner.Browser(Layout("w"), null, Chrome, ["--disable-web-security"]);

        Assert.StartsWith("https://", command.Arguments[^1]);
    }

    [Fact]
    public void App_expands_profile_placeholder_inside_tokens()
    {
        var app = new AppShortcut { Name = "VS Code", Path = "\"C:\\Code\\code.exe\"", Arguments = "--user-data-dir \"{profileDir}\" --new-window" };

        var command = LaunchPlanner.App(Layout("w"), app, NoEnvironment);

        var dir = Path.Combine(Root, "w", "apps", "vs-code");
        Assert.Equal(@"C:\Code\code.exe", command.FileName);
        Assert.Equal(dir, command.ProfileDirectory);
        Assert.Equal(["--user-data-dir", dir, "--new-window"], command.Arguments);
    }

    [Fact]
    public void App_without_placeholder_has_no_profile_dir()
    {
        var command = LaunchPlanner.App(Layout("w"), new AppShortcut { Name = "Slack", Path = "slack.exe", Arguments = "" }, NoEnvironment);

        Assert.Null(command.ProfileDirectory);
        Assert.Empty(command.Arguments);
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a  b", new[] { "a", "b" })]
    [InlineData("\"with space\" x", new[] { "with space", "x" })]
    [InlineData("--flag=\"a b\"", new[] { "--flag=a b" })]
    [InlineData("\"\"", new[] { "" })]
    public void SplitArguments_handles_quotes(string input, string[] expected)
    {
        Assert.Equal(expected, LaunchPlanner.SplitArguments(input));
    }
}

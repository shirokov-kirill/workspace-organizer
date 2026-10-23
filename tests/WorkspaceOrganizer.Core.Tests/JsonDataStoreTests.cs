using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Storage;

namespace WorkspaceOrganizer.Core.Tests;

public sealed class JsonDataStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "wso-tests-" + Guid.NewGuid().ToString("N"));
    string FilePath => Path.Combine(_dir, "organizer.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_loads_default_work_and_personal()
    {
        var data = new JsonDataStore(FilePath).Load(out var warning);

        Assert.Null(warning);
        Assert.Equal(["Work", "Personal"], data.Workspaces.Select(w => w.Name));
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonDataStore(FilePath);
        var data = OrganizerData.CreateDefault();
        var work = data.Workspaces[0];
        work.Apps.Add(new AppShortcut { Name = "Code", Path = "code.exe", Arguments = "--user-data-dir {profileDir}" });
        data.Accounts.Add(new Account { WorkspaceId = work.Id, Service = "GitHub", Login = "me@work.example" });
        data.Subscriptions.Add(new Subscription
        {
            WorkspaceId = work.Id, Name = "Copilot", Cost = 19.99m, Cycle = BillingCycle.Yearly, NextRenewal = new DateOnly(2027, 2, 1),
        });

        store.Save(data);
        var loaded = store.Load(out _);

        Assert.Equal("--user-data-dir {profileDir}", loaded.Workspaces[0].Apps[0].Arguments);
        Assert.Equal("me@work.example", loaded.Accounts[0].Login);
        Assert.Equal(19.99m, loaded.Subscriptions[0].Cost);
        Assert.Equal(BillingCycle.Yearly, loaded.Subscriptions[0].Cycle);
        Assert.Equal(new DateOnly(2027, 2, 1), loaded.Subscriptions[0].NextRenewal);
        Assert.Contains("\"Yearly\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_not_overwritten()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        var data = new JsonDataStore(FilePath).Load(out var warning);

        Assert.NotNull(warning);
        Assert.Equal(2, data.Workspaces.Count);
        Assert.False(File.Exists(FilePath));
        var backup = Assert.Single(Directory.GetFiles(_dir, "organizer.json.corrupt-*"));
        Assert.Equal("{ not json", File.ReadAllText(backup));
    }

    [Fact]
    public void Normalize_reassigns_orphans_and_ensures_a_workspace()
    {
        var data = new OrganizerData
        {
            Accounts = [new Account { WorkspaceId = "gone" }],
            Subscriptions = [new Subscription { WorkspaceId = "gone" }],
        }.Normalize();

        var only = Assert.Single(data.Workspaces);
        Assert.Equal(only.Id, data.Accounts[0].WorkspaceId);
        Assert.Equal(only.Id, data.Subscriptions[0].WorkspaceId);
    }
}

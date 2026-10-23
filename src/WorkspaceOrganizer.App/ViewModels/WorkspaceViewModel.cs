using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkspaceOrganizer.Core.Launching;
using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Profiles;
using WorkspaceOrganizer.Core.Subscriptions;

namespace WorkspaceOrganizer.App.ViewModels;

public partial class WorkspaceViewModel : EditableViewModel
{
    static readonly IBrush FallbackBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#64748B"));
    const int RenewalWindowDays = 14;

    readonly string _id;
    // Kept separately so a browser that's temporarily not detected isn't wiped on save.
    string? _browserId;
    Dictionary<string, string> _extraEnvironment;

    public WorkspaceViewModel(Workspace model, MainViewModel owner, OrganizerData? data)
    {
        Owner = owner;
        _id = model.Id;
        _browserId = model.BrowserId;
        _extraEnvironment = new Dictionary<string, string>(model.ExtraEnvironment, StringComparer.OrdinalIgnoreCase);
        Folder = model.Folder;
        FolderDraft = model.Folder;
        Name = model.Name;
        Color = model.Color;
        BrowserProfile = model.BrowserProfile;
        RedirectHome = model.RedirectHome;
        ExtraEnvironmentText = EnvironmentText.Format(_extraEnvironment);
        SelectedBrowser = owner.Browsers.FirstOrDefault(b => b.Id == model.BrowserId)
            ?? (model.BrowserId is null ? owner.Browsers.FirstOrDefault() : null);
        Tools = CliToolCatalog.All
            .Select(t => new ToolToggleViewModel(t, model.EnabledTools?.Contains(t.Id) ?? true, this))
            .ToList();

        foreach (var app in model.Apps)
            Apps.Add(new AppShortcutViewModel(app, this));
        if (data is not null)
        {
            foreach (var account in data.Accounts.Where(a => a.WorkspaceId == _id))
                Accounts.Add(new AccountViewModel(account, this));
            foreach (var subscription in data.Subscriptions.Where(s => s.WorkspaceId == _id))
                Subscriptions.Add(new SubscriptionViewModel(subscription, this));
        }

        Apps.CollectionChanged += (_, _) => OnEdited();
        Accounts.CollectionChanged += (_, _) => OnEdited();
        Subscriptions.CollectionChanged += (_, _) => RefreshSummary();
        RefreshSummary();
    }

    public MainViewModel Owner { get; }
    public ObservableCollection<AppShortcutViewModel> Apps { get; } = [];
    public ObservableCollection<AccountViewModel> Accounts { get; } = [];
    public ObservableCollection<SubscriptionViewModel> Subscriptions { get; } = [];
    public IReadOnlyList<ToolToggleViewModel> Tools { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Brush))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileFolderPath), nameof(ProfileHint))]
    public partial string Folder { get; private set; } = "";

    [ObservableProperty]
    public partial string FolderDraft { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Brush))]
    public partial string Color { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileHint))]
    public partial BrowserInfo? SelectedBrowser { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileHint))]
    public partial string? BrowserProfile { get; set; }

    [ObservableProperty]
    public partial bool RedirectHome { get; set; }

    [ObservableProperty]
    public partial string ExtraEnvironmentText { get; set; } = "";

    [ObservableProperty]
    public partial AppShortcutViewModel? SelectedApp { get; set; }

    [ObservableProperty]
    public partial AccountViewModel? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial SubscriptionViewModel? SelectedSubscription { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    public IBrush Brush => Avalonia.Media.Color.TryParse(Color, out var c) ? new SolidColorBrush(c) : FallbackBrush;

    public ProfileLayout Layout => new(Owner.ProfilesRoot, Folder);

    public string ProfileFolderPath => Layout.Root;

    /// <summary>Null when every tool is on, so tools added in later versions are on by default too.</summary>
    public IReadOnlyCollection<string>? EnabledToolIds =>
        Tools.All(t => t.IsEnabled) ? null : Tools.Where(t => t.IsEnabled).Select(t => t.Tool.Id).ToList();

    public string ProfileHint => SelectedBrowser is not { } browser
        ? "Pick a browser to open this workspace's accounts in."
        : string.IsNullOrWhiteSpace(BrowserProfile)
            ? $"Isolated: {browser.Name} runs with its own data folder, separate from every other workspace:\n{Layout.Browser(browser.Id)}"
            : $"Uses {browser.Name}'s own profile \"{BrowserProfile.Trim()}\" (e.g. \"Default\", \"Profile 1\"). Cookies stay in that profile.";

    partial void OnSelectedBrowserChanged(BrowserInfo? value)
    {
        if (value is not null)
            _browserId = value.Id;
    }

    partial void OnExtraEnvironmentTextChanged(string value) => _extraEnvironment = EnvironmentText.Parse(value);

    protected override void OnEdited() => Owner.ScheduleSave();

    /// <summary>Called when the profiles root changes, since every path is derived from it.</summary>
    public void OnLayoutChanged()
    {
        OnPropertyChanged(nameof(ProfileFolderPath));
        OnPropertyChanged(nameof(ProfileHint));
        foreach (var tool in Tools)
            tool.OnLayoutChanged();
    }

    public void RefreshSummary()
    {
        var subscriptions = Subscriptions.Select(s => s.ToModel(_id)).ToList();
        var totals = SubscriptionMath.MonthlyTotals(subscriptions);
        var renewing = SubscriptionMath.RenewingWithin(subscriptions, DateOnly.FromDateTime(DateTime.Today), RenewalWindowDays);

        var text = totals.Count == 0
            ? "No active subscriptions."
            : string.Join("   ·   ", totals.Select(t => $"{t.Monthly:0.00} {t.Currency}/month ({t.Yearly:0.00}/year)"));
        if (renewing.Count > 0)
            text += $"   ·   renewing in the next {RenewalWindowDays} days: {string.Join(", ", renewing.Select(s => s.Name))}";
        SummaryText = text;
    }

    Workspace ToModel() => new()
    {
        Id = _id,
        Name = Name,
        Folder = Folder,
        Color = Color,
        BrowserId = _browserId,
        BrowserProfile = string.IsNullOrWhiteSpace(BrowserProfile) ? null : BrowserProfile.Trim(),
        EnabledTools = EnabledToolIds?.ToList(),
        RedirectHome = RedirectHome,
        ExtraEnvironment = new Dictionary<string, string>(_extraEnvironment),
        Apps = Apps.Select(a => a.ToModel()).ToList(),
    };

    public void WriteTo(OrganizerData data)
    {
        data.Workspaces.Add(ToModel());
        data.Accounts.AddRange(Accounts.Select(a => a.ToModel(_id)));
        data.Subscriptions.AddRange(Subscriptions.Select(s => s.ToModel(_id)));
    }

    void OpenInBrowser(string[] urls)
    {
        if (SelectedBrowser is not { } browser)
        {
            Owner.Status = $"Pick a browser for \"{Name}\" on the Launch tab first.";
            return;
        }
        Owner.Launch(() => LaunchPlanner.Browser(Layout, BrowserProfile, browser, urls), $"{browser.Name} ({Name})");
    }

    [RelayCommand]
    void OpenBrowser() => OpenInBrowser([]);

    [RelayCommand]
    void OpenAccount(AccountViewModel? account)
    {
        if (account is null)
            return;
        if (string.IsNullOrWhiteSpace(account.Url))
        {
            Owner.Status = $"\"{account.Service}\" has no URL.";
            return;
        }
        OpenInBrowser([account.Url]);
    }

    [RelayCommand]
    void OpenTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            Owner.Status = $"Profile terminals are Windows-only for now; run `wso shell {Folder}` instead.";
            return;
        }
        // The terminal runs activate.ps1, so it must reflect unsaved edits.
        Owner.SaveNow();
        var (windowsTerminal, shell) = Owner.Terminal;
        Owner.Launch(() => LaunchPlanner.Terminal(Layout, Name, windowsTerminal, shell), $"a terminal for {Name}");
    }

    [RelayCommand]
    void OpenFolder()
    {
        Owner.SaveNow();
        Owner.Launch(() => new LaunchCommand(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open", [Layout.Root], Layout.Root),
            $"the {Name} folder");
    }

    [RelayCommand]
    void RenameFolder()
    {
        var folder = ProfileLayout.SafeSegment(FolderDraft);
        FolderDraft = folder;
        if (folder == Folder)
            return;
        if (Owner.Workspaces.Any(w => w != this && string.Equals(w.Folder, folder, StringComparison.OrdinalIgnoreCase)))
        {
            Owner.Status = $"Another workspace already uses the folder \"{folder}\".";
            return;
        }

        var from = Layout.Root;
        var to = new ProfileLayout(Owner.ProfilesRoot, folder).Root;
        if (Directory.Exists(to))
        {
            Owner.Status = $"{to} already exists; pick another name or move it away first.";
            return;
        }
        try
        {
            if (Directory.Exists(from))
                Directory.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Owner.Status = $"Couldn't rename {from}; close terminals, browsers and editors using it. ({ex.Message})";
            return;
        }

        Folder = folder;
        OnLayoutChanged();
        Owner.SaveNow();
        Owner.Status = $"Profile folder is now {to}. Reopen any profile terminals.";
    }

    [RelayCommand]
    void AddApp()
    {
        var app = new AppShortcutViewModel(new AppShortcut { Name = "New app" }, this);
        Apps.Add(app);
        SelectedApp = app;
    }

    [RelayCommand]
    void RemoveApp(AppShortcutViewModel? app)
    {
        if (app is not null)
            Apps.Remove(app);
    }

    [RelayCommand]
    void LaunchApp(AppShortcutViewModel? app)
    {
        if (app is null)
            return;
        var layout = Layout;
        Owner.Launch(() => LaunchPlanner.App(layout, app.ToModel(), ProfileEnvironment.Build(ToModel(), layout)), $"{app.Name} ({Name})");
    }

    [RelayCommand]
    void AddAccount()
    {
        var account = new AccountViewModel(new Account { Service = "New account" }, this);
        Accounts.Add(account);
        SelectedAccount = account;
    }

    [RelayCommand]
    void RemoveAccount(AccountViewModel? account)
    {
        if (account is not null)
            Accounts.Remove(account);
    }

    [RelayCommand]
    void AddSubscription()
    {
        var subscription = new SubscriptionViewModel(new Subscription { Name = "New subscription" }, this);
        Subscriptions.Add(subscription);
        SelectedSubscription = subscription;
    }

    [RelayCommand]
    void RemoveSubscription(SubscriptionViewModel? subscription)
    {
        if (subscription is not null)
            Subscriptions.Remove(subscription);
    }
}

public partial class ToolToggleViewModel : ViewModelBase
{
    readonly WorkspaceViewModel _workspace;

    public ToolToggleViewModel(CliTool tool, bool isEnabled, WorkspaceViewModel workspace)
    {
        Tool = tool;
        _workspace = workspace;
        IsEnabled = isEnabled;
    }

    public CliTool Tool { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public string Details
    {
        get
        {
            var variables = Tool.Environment(_workspace.Layout);
            var text = string.Join("   ", variables.Select(kv => $"{kv.Key}={kv.Value}"));
            return Tool.Note is null ? text : text + Environment.NewLine + Tool.Note;
        }
    }

    partial void OnIsEnabledChanged(bool value) => _workspace.Owner.ScheduleSave();

    public void OnLayoutChanged() => OnPropertyChanged(nameof(Details));
}

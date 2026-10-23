using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkspaceOrganizer.Core.Launching;
using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Profiles;
using WorkspaceOrganizer.Core.Storage;

namespace WorkspaceOrganizer.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    readonly JsonDataStore _store;
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly bool _loading;

    public MainViewModel(JsonDataStore store, IReadOnlyList<BrowserInfo> browsers)
    {
        _store = store;
        Browsers = browsers;
        Terminal = ProcessLauncher.DetectTerminal();
        _saveTimer.Tick += (_, _) => SaveNow();

        var data = store.Load(out var warning);
        ProfilesRoot = data.Settings.ProfilesRoot;
        ProfilesRootDraft = ProfilesRoot;
        var problems = new List<string>();
        if (warning is not null)
            problems.Add(warning);
        problems.AddRange(ProfileMigration.MoveLegacyBrowserData(data, ProfileMigration.LegacyProfilesRoot));

        _loading = true;
        foreach (var workspace in data.Workspaces)
            Workspaces.Add(new WorkspaceViewModel(workspace, this, data));
        _loading = false;
        SelectedWorkspace = Workspaces.FirstOrDefault();

        // Make sure every profile folder, marker and activate script exists before anything launches.
        SaveNow();
        problems.AddRange(DeployCli());
        Status = problems.Count > 0
            ? string.Join(" ", problems)
            : browsers.Count == 0
                ? "No supported browser found (Chrome, Edge, Brave, Vivaldi, Firefox)."
                : "Browsers: " + string.Join(", ", browsers.Select(b => b.Name));
    }

    public ObservableCollection<WorkspaceViewModel> Workspaces { get; } = [];
    public IReadOnlyList<BrowserInfo> Browsers { get; }
    public (string? WindowsTerminal, string Shell) Terminal { get; }
    public string DataFilePath => _store.FilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShimsDirectory))]
    public partial string ProfilesRoot { get; private set; }

    [ObservableProperty]
    public partial string ProfilesRootDraft { get; set; }

    [ObservableProperty]
    public partial WorkspaceViewModel? SelectedWorkspace { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [ObservableProperty]
    public partial string? ShimReport { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    /// <summary>The workspace waiting for the user to confirm its removal, if any.</summary>
    [ObservableProperty]
    public partial WorkspaceViewModel? PendingRemoval { get; private set; }

    [ObservableProperty]
    public partial string? PendingRemovalText { get; private set; }

    CancellationTokenSource? _measuring;

    public string ShimsDirectory => ProfileLayout.ShimsDirectory(ProfilesRoot);

    partial void OnSelectedWorkspaceChanged(WorkspaceViewModel? value)
    {
        if (value is not null)
            IsSettingsOpen = false;
        if (value != PendingRemoval)
            CancelRemoval();
    }

    public void ScheduleSave()
    {
        if (_loading)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Persists the data file and brings every profile folder on disk in line with it.</summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        var data = new OrganizerData { Settings = { ProfilesRoot = ProfilesRoot } };
        foreach (var workspace in Workspaces)
            workspace.WriteTo(data);

        try
        {
            _store.Save(data);
            ProfileWriter.WriteAll(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save: {ex.Message}";
        }
    }

    public void Launch(Func<LaunchCommand> plan, string what)
    {
        try
        {
            ProcessLauncher.Start(plan());
            Status = $"Launched {what}.";
        }
        catch (Exception ex) when (ex is ArgumentException or Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Status = $"Could not launch {what}: {ex.Message}";
        }
    }

    [RelayCommand]
    void OpenSettings()
    {
        IsSettingsOpen = true;
        SelectedWorkspace = null;
    }

    [RelayCommand]
    void ApplyProfilesRoot()
    {
        var draft = ProfilesRootDraft.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(draft))
        {
            Status = "The profiles root must be a full path, e.g. C:\\Users\\you\\Profiles.";
            return;
        }
        var root = Path.GetFullPath(draft);
        if (string.Equals(root, ProfilesRoot, StringComparison.OrdinalIgnoreCase))
            return;

        var oldShims = ShimsDirectory;
        ProfilesRoot = root;
        ProfilesRootDraft = root;
        foreach (var workspace in Workspaces)
            workspace.OnLayoutChanged();
        SaveNow();
        var problems = DeployCli();
        Status = $"Profiles now live in {root}. Nothing was moved from the old location"
            + (Directory.Exists(oldShims) ? "; reinstall the shims so they point at the new root." : ".")
            + string.Concat(problems.Select(p => " " + p));
    }

    /// <summary>
    /// Keeps <c>wso</c> and <c>wso-open.exe</c> in the shims folder current. Profiles set BROWSER to
    /// <c>wso-open.exe</c>, so this is needed even when the command shims aren't installed.
    /// </summary>
    IReadOnlyList<string> DeployCli()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        try
        {
            return ShimInstaller.DeployCli(ProfilesRoot, AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"Couldn't set up wso in {ShimsDirectory}, so logins from CLI tools open in the default browser: {ex.Message}"];
        }
    }

    [RelayCommand]
    void InstallShims()
    {
        if (!OperatingSystem.IsWindows())
        {
            ShimReport = "Shims are only supported on Windows for now.";
            return;
        }
        try
        {
            SaveNow();
            var commands = ShimInstaller.CommandsFor(Workspaces.Select(w => w.EnabledToolIds));
            var report = ShimInstaller.Install(ProfilesRoot, AppContext.BaseDirectory, commands);
            ShimReport = $"Installed shims for: {string.Join(", ", report.Commands)}."
                + string.Concat(report.Warnings.Select(w => Environment.NewLine + "• " + w));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShimReport = $"Couldn't install shims: {ex.Message}";
        }
    }

    [RelayCommand]
    void UninstallShims()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var warnings = ShimInstaller.Uninstall(ProfilesRoot);
        ShimReport = "Tool shims removed and the folder taken off PATH (wso and wso-open stay; profiles use wso-open as their browser). Open a new terminal to pick up the change."
            + string.Concat(warnings.Select(w => Environment.NewLine + "• " + w));
    }

    [RelayCommand]
    void AddWorkspace()
    {
        var folder = ProfileLayout.UniqueFolder("new-workspace", Workspaces.Select(w => w.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var workspace = new WorkspaceViewModel(new Workspace { Name = "New workspace", Folder = folder }, this, null);
        Workspaces.Add(workspace);
        SelectedWorkspace = workspace;
        ScheduleSave();
    }

    [RelayCommand]
    void RemoveWorkspace(WorkspaceViewModel? workspace)
    {
        if (workspace is null)
            return;
        if (Workspaces.Count == 1)
        {
            Status = "At least one workspace is required.";
            return;
        }
        if (workspace.Accounts.Count + workspace.Subscriptions.Count > 0)
        {
            Status = $"Move or remove the {workspace.Accounts.Count} account(s) and {workspace.Subscriptions.Count} subscription(s) in \"{workspace.Name}\" first.";
            return;
        }

        BeginRemoval(workspace);
    }

    async void BeginRemoval(WorkspaceViewModel workspace)
    {
        CancelRemoval();
        SelectedWorkspace = workspace;
        PendingRemoval = workspace;
        var folder = workspace.ProfileFolderPath;
        if (!Directory.Exists(folder))
        {
            PendingRemovalText = $"Remove \"{workspace.Name}\"? It has no folder on disk.";
            return;
        }

        PendingRemovalText = RemovalText(workspace, "calculating size…");
        var measuring = _measuring = new CancellationTokenSource();
        try
        {
            var size = await ProfileFolder.MeasureAsync(folder, measuring.Token);
            if (PendingRemoval == workspace && !measuring.IsCancellationRequested)
                PendingRemovalText = RemovalText(workspace, $"{size.Files:N0} files, {FormatBytes(size.Bytes)}");
        }
        catch (OperationCanceledException)
        {
        }
    }

    static string RemovalText(WorkspaceViewModel workspace, string size) =>
        $"Remove \"{workspace.Name}\" and move {workspace.ProfileFolderPath} ({size}) to the Recycle Bin? " +
        "This includes its browser data, CLI logins and any files you keep there.";

    static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    [RelayCommand]
    void ConfirmRemoval()
    {
        if (PendingRemoval is not { } workspace)
            return;

        var folder = workspace.ProfileFolderPath;
        try
        {
            ProfileFolder.Delete(workspace.Layout, toRecycleBin: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
        {
            CancelRemoval();
            Status = $"Couldn't remove {folder}; close terminals, browsers and editors using it and try again. ({ex.Message})";
            return;
        }

        CancelRemoval();
        Workspaces.Remove(workspace);
        SelectedWorkspace = Workspaces[0];
        SaveNow();
        Status = Directory.Exists(folder)
            ? $"Removed \"{workspace.Name}\", but {folder} is still there."
            : $"Removed \"{workspace.Name}\"; its folder is in the Recycle Bin.";
    }

    [RelayCommand]
    void CancelRemoval()
    {
        _measuring?.Cancel();
        _measuring = null;
        PendingRemoval = null;
        PendingRemovalText = null;
    }
}

using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkspaceOrganizer.Core.Models;
using WorkspaceOrganizer.Core.Subscriptions;

namespace WorkspaceOrganizer.App.ViewModels;

public partial class AppShortcutViewModel : EditableViewModel
{
    public AppShortcutViewModel(AppShortcut model, WorkspaceViewModel workspace)
    {
        Workspace = workspace;
        Name = model.Name;
        Path = model.Path;
        Arguments = model.Arguments;
    }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    [ObservableProperty]
    public partial string Arguments { get; set; } = "";

    public WorkspaceViewModel Workspace { get; }

    protected override void OnEdited() => Workspace.Owner.ScheduleSave();

    public AppShortcut ToModel() => new() { Name = Name, Path = Path, Arguments = Arguments };
}

/// <summary>Base for items that can be moved between workspaces from their editor.</summary>
public abstract class MovableItemViewModel<TSelf> : EditableViewModel where TSelf : MovableItemViewModel<TSelf>
{
    WorkspaceViewModel _workspace;

    protected MovableItemViewModel(WorkspaceViewModel workspace) => _workspace = workspace;

    public IReadOnlyList<WorkspaceViewModel> AllWorkspaces => _workspace.Owner.Workspaces;

    public WorkspaceViewModel Workspace
    {
        get => _workspace;
        set
        {
            // The ComboBox pushes null while its DataContext switches; ignore that.
            if (value is null || ReferenceEquals(value, _workspace))
                return;
            var old = _workspace;
            _workspace = value;
            ItemsOf(old).Remove((TSelf)this);
            ItemsOf(value).Add((TSelf)this);
            OnPropertyChanged();
        }
    }

    protected abstract IList<TSelf> ItemsOf(WorkspaceViewModel workspace);

    protected override void OnEdited() => _workspace.Owner.ScheduleSave();
}

public partial class AccountViewModel : MovableItemViewModel<AccountViewModel>
{
    readonly string _id;

    public AccountViewModel(Account model, WorkspaceViewModel workspace) : base(workspace)
    {
        _id = model.Id;
        Service = model.Service;
        Url = model.Url;
        Login = model.Login;
        TwoFactor = model.TwoFactor;
        Notes = model.Notes;
    }

    [ObservableProperty]
    public partial string Service { get; set; } = "";

    [ObservableProperty]
    public partial string Url { get; set; } = "";

    [ObservableProperty]
    public partial string Login { get; set; } = "";

    [ObservableProperty]
    public partial string TwoFactor { get; set; } = "";

    [ObservableProperty]
    public partial string Notes { get; set; } = "";

    protected override IList<AccountViewModel> ItemsOf(WorkspaceViewModel workspace) => workspace.Accounts;

    public Account ToModel(string workspaceId) => new()
    {
        Id = _id, WorkspaceId = workspaceId, Service = Service, Url = Url, Login = Login, TwoFactor = TwoFactor, Notes = Notes,
    };
}

public partial class SubscriptionViewModel : MovableItemViewModel<SubscriptionViewModel>
{
    readonly string _id;

    public SubscriptionViewModel(Subscription model, WorkspaceViewModel workspace) : base(workspace)
    {
        _id = model.Id;
        Name = model.Name;
        Cost = model.Cost;
        Currency = model.Currency;
        Cycle = model.Cycle;
        Renewal = model.NextRenewal?.ToDateTime(TimeOnly.MinValue);
        Active = model.Active;
        Notes = model.Notes;
    }

    public static IReadOnlyList<BillingCycle> Cycles { get; } = Enum.GetValues<BillingCycle>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostText))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostText))]
    public partial decimal? Cost { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostText))]
    public partial string Currency { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostText), nameof(RenewalText))]
    public partial BillingCycle Cycle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenewalText))]
    public partial DateTime? Renewal { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenewalText))]
    public partial bool Active { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; } = "";

    public string CostText => $"{Cost ?? 0:0.00} {Currency} / {Cycle.ToString().ToLowerInvariant()}";

    public string RenewalText => !Active
        ? "inactive"
        : Renewal is { } date
            ? "renews " + SubscriptionMath.NextOccurrence(DateOnly.FromDateTime(date), Cycle, DateOnly.FromDateTime(DateTime.Today)).ToString("d")
            : "no renewal date";

    protected override IList<SubscriptionViewModel> ItemsOf(WorkspaceViewModel workspace) => workspace.Subscriptions;

    protected override void OnEdited()
    {
        Workspace.RefreshSummary();
        base.OnEdited();
    }

    public Subscription ToModel(string workspaceId) => new()
    {
        Id = _id,
        WorkspaceId = workspaceId,
        Name = Name,
        Cost = Cost ?? 0,
        Currency = Currency,
        Cycle = Cycle,
        NextRenewal = Renewal is { } date ? DateOnly.FromDateTime(date) : null,
        Active = Active,
        Notes = Notes,
    };
}

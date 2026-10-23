using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkspaceOrganizer.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

/// <summary>Calls <see cref="OnEdited"/> after every property change so the owner can auto-save.</summary>
public abstract class EditableViewModel : ViewModelBase
{
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        OnEdited();
    }

    protected abstract void OnEdited();
}

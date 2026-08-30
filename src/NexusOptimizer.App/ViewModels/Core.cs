using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NexusOptimizer.App.ViewModels;

public interface IPageLifecycle
{
    void Activate();
    void Deactivate();
}

/// <summary>Base INPC minimale (CommunityToolkit.Mvvm verrà introdotto in FASE 3+).</summary>
public abstract class ObservableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>ICommand sincrono con supporto canExecute.</summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecute()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}



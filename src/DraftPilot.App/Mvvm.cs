using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DraftPilot.App;

/// <summary>Minimal change notification. A framework would be more than this app needs.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? property = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>Assigns and notifies only when the value actually changed.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Raise(property);
        return true;
    }
}

/// <summary>A command backed by a delegate.</summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Grows or shrinks a collection to a target size, reusing the existing items.
/// <para>
/// The draft refreshes on every client event. Clearing and refilling would rebuild every row, which
/// costs allocations and throws away scroll position and expander state several times a second.
/// </para>
/// </summary>
public static class ObservableCollectionExtensions
{
    public static void Resize<T>(this ObservableCollection<T> target, int size, Func<T> create)
    {
        while (target.Count > size)
            target.RemoveAt(target.Count - 1);

        while (target.Count < size)
            target.Add(create());
    }

    /// <summary>
    /// Replaces the contents, but only writes the positions that actually differ.
    /// <para>
    /// An unconditional assignment raises Replace on every render pass and makes the ItemsControl
    /// rebuild its containers — visible as flickering chips, and it drops a tooltip the moment the
    /// user hovers one. Requires value equality on <typeparamref name="T"/>; the chip types are
    /// records for exactly that reason.
    /// </para>
    /// </summary>
    public static void ReplaceAll<T>(this ObservableCollection<T> target, IReadOnlyList<T> values)
    {
        while (target.Count > values.Count)
            target.RemoveAt(target.Count - 1);

        for (var i = 0; i < target.Count; i++)
        {
            if (!Equals(target[i], values[i]))
                target[i] = values[i];
        }

        for (var i = target.Count; i < values.Count; i++)
            target.Add(values[i]);
    }
}

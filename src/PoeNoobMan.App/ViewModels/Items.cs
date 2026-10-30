using System.ComponentModel;
using System.Runtime.CompilerServices;
using PoeNoobMan.Core.Guides;

namespace PoeNoobMan.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class StepItem(GuideStep step) : ObservableObject
{
    private bool _isDone;
    private bool _isCurrent;
    private bool _isBlocker;

    public string Id => step.Id;
    public string Act => $"A{step.Act}";
    public string Text => step.Text;
    public string? Note => step.Note;
    public bool HasNote => !string.IsNullOrWhiteSpace(step.Note);
    public string? WhenText => step.When is null ? null : step.When.ToString();

    public bool IsDone { get => _isDone; set => Set(ref _isDone, value); }
    public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
    public bool IsBlocker { get => _isBlocker; set => Set(ref _isBlocker, value); }
}

public sealed class NodeItem(TreeNode node) : ObservableObject
{
    private bool _isTaken;
    private bool _isNow;
    private bool _isNext;

    public string Id => node.Id;
    public string Name => node.Refund ? $"Зняти: {node.Name}" : node.Name;
    public string Condition => node.DescribeCondition();

    public bool IsTaken { get => _isTaken; set => Set(ref _isTaken, value); }
    public bool IsNow { get => _isNow; set => Set(ref _isNow, value); }
    public bool IsNext { get => _isNext; set => Set(ref _isNext, value); }
}

public sealed class HintItem
{
    public required string Act { get; init; }
    public required string Text { get; init; }
    public bool IsWaiting { get; init; }
}

public sealed class SplitRowItem
{
    public required string Label { get; init; }
    public required string Current { get; init; }
    public required string Last { get; init; }
    public required string Best { get; init; }
    public required string Delta { get; init; }
    public bool Ahead { get; init; }
    public bool Behind { get; init; }
}

public sealed class OptionItem : ObservableObject
{
    private string _selected = "";

    public required string Id { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<OptionChoice> Choices { get; init; }
    public string Selected { get => _selected; set => Set(ref _selected, value); }
}

public sealed class HotkeyItem : ObservableObject
{
    private string _value = "";

    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Value { get => _value; set => Set(ref _value, value); }
}

public static class TimeFormat
{
    public static string Clock(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    public static string Clock(double? seconds) => seconds is { } s ? Clock(TimeSpan.FromSeconds(Math.Floor(s))) : "—";

    /// <summary>"−0:45", "+1:12:03".</summary>
    public static string Delta(TimeSpan d)
    {
        var sign = d < TimeSpan.Zero ? "−" : "+";
        var a = d.Duration();
        return a.TotalHours >= 1 ? $"{sign}{(int)a.TotalHours}:{a.Minutes:00}:{a.Seconds:00}" : $"{sign}{a.Minutes}:{a.Seconds:00}";
    }
}

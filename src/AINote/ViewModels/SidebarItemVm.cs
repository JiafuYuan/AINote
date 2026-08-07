using System.Windows.Input;
using Avalonia.Media;

namespace AINote.ViewModels;

public sealed class SidebarItemVm : ObservableObject
{
    private int _count;
    private bool _isSelected;

    public SidebarItemVm(string key, string label, ICommand command)
    {
        Key = key;
        Label = label;
        Command = command;
    }

    public string Key { get; }
    public string Label { get; }
    public ICommand Command { get; }
    public string CountText => Count > 0 ? Count.ToString() : "";

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(CountText));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
                OnPropertyChanged(nameof(ForegroundBrush));
            }
        }
    }

    public IBrush BackgroundBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(30, 41, 59))
        : Brushes.Transparent;

    public IBrush ForegroundBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(248, 250, 252))
        : new SolidColorBrush(Color.FromRgb(203, 213, 225));
}

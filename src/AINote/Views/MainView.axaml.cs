using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AINote.ViewModels;

namespace AINote.Views;

public partial class MainView : UserControl
{
    private MainWindowViewModel? _vm;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as MainWindowViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyLayout();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm is null) return;
        _vm.SetLayoutWidth(e.NewSize.Width);
        ApplyLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsNarrow)
            or nameof(MainWindowViewModel.SidebarOpen)
            or nameof(MainWindowViewModel.SelectedNote))
        {
            ApplyLayout();
        }
    }

    private void ApplyLayout()
    {
        if (_vm is null) return;

        if (_vm.IsNarrow)
        {
            Grid.SetColumn(SidebarPanel, 0);
            Grid.SetColumnSpan(SidebarPanel, 3);
            SidebarPanel.ZIndex = 10;
            Grid.SetColumn(DetailPanel, 0);
            Grid.SetColumnSpan(DetailPanel, 3);
            DetailPanel.ZIndex = 20;

            SidebarPanel.IsVisible = _vm.SidebarOpen;
            DetailPanel.IsVisible = _vm.SelectedNote is not null;
            MainListPanel.IsVisible = !_vm.SidebarOpen && _vm.SelectedNote is null;
        }
        else
        {
            Grid.SetColumn(SidebarPanel, 0);
            Grid.SetColumnSpan(SidebarPanel, 1);
            SidebarPanel.ZIndex = 0;
            Grid.SetColumn(DetailPanel, 2);
            Grid.SetColumnSpan(DetailPanel, 1);
            DetailPanel.ZIndex = 0;

            SidebarPanel.IsVisible = true;
            DetailPanel.IsVisible = true;
            MainListPanel.IsVisible = true;
        }
    }

    private void OnQuickInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        if (_vm.QuickAddCommand.CanExecute(null))
        {
            _vm.QuickAddCommand.Execute(null);
            e.Handled = true;
        }
    }
}

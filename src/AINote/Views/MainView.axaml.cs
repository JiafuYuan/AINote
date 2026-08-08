using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
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
            or nameof(MainWindowViewModel.SelectedNote)
            or nameof(MainWindowViewModel.IsAddNoteClosed))
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
            Grid.SetColumn(MainListPanel, 0);
            Grid.SetColumnSpan(MainListPanel, 3);
            MainListPanel.ZIndex = 5;

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
            Grid.SetColumn(MainListPanel, 1);
            Grid.SetColumnSpan(MainListPanel, 1);
            MainListPanel.ZIndex = 0;

            SidebarPanel.IsVisible = true;
            DetailPanel.IsVisible = true;
            MainListPanel.IsVisible = true;
        }

        ToggleClass(AddDialog, "wide", !_vm.IsNarrow);
        ToggleClass(AddNoteEditor, "wide", !_vm.IsNarrow);
        ApplyAddDialogLayout();
        FabPanel.IsVisible = _vm.IsAddNoteClosed && (!_vm.IsNarrow || !_vm.SidebarOpen);
    }

    private void ApplyAddDialogLayout()
    {
        if (_vm!.IsNarrow)
        {
            AddDialog.HorizontalAlignment = HorizontalAlignment.Stretch;
            AddDialog.VerticalAlignment = VerticalAlignment.Bottom;
            AddDialog.Margin = new Thickness(0);
            AddDialog.MaxWidth = double.PositiveInfinity;
            AddDialog.CornerRadius = new CornerRadius(24, 24, 0, 0);
            AddDialog.Padding = new Thickness(20, 22, 20, 26);
        }
        else
        {
            AddDialog.HorizontalAlignment = HorizontalAlignment.Center;
            AddDialog.VerticalAlignment = VerticalAlignment.Center;
            AddDialog.Margin = new Thickness(20, 24);
            AddDialog.MaxWidth = 760;
            AddDialog.CornerRadius = new CornerRadius(20);
            AddDialog.Padding = new Thickness(26);
        }
    }

    private static void ToggleClass(StyledElement element, string className, bool enabled)
    {
        if (enabled)
        {
            if (!element.Classes.Contains(className))
                element.Classes.Add(className);
        }
        else
        {
            element.Classes.Remove(className);
        }
    }

    private void OnAddNoteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
        if (_vm.AddNoteCommand.CanExecute(null))
        {
            _vm.AddNoteCommand.Execute(null);
            e.Handled = true;
        }
    }
}

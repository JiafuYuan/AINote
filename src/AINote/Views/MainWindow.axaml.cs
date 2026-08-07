using Avalonia.Controls;

namespace AINote.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
#if !ANDROID
        Width = 1280;
        Height = 820;
        MinWidth = 900;
        MinHeight = 620;
#endif
    }
}

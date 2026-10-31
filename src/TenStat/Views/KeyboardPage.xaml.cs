using Microsoft.UI.Xaml.Controls;
using TenStat.ViewModels;

namespace TenStat.Views;

public sealed partial class KeyboardPage : Page
{
    public KeyboardViewModel ViewModel { get; } = new();

    public KeyboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Attach(DispatcherQueue);
        Unloaded += (_, _) => ViewModel.Detach();
    }
}

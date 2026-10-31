using Microsoft.UI.Xaml.Controls;
using TenStat.ViewModels;

namespace TenStat.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new();

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Attach(DispatcherQueue);
        Unloaded += (_, _) => ViewModel.Detach();
    }
}

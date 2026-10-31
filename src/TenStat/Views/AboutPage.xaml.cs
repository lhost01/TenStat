using Microsoft.UI.Xaml.Controls;
using TenStat.Services;

namespace TenStat.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var version = typeof(AboutPage).Assembly.GetName().Version;
        VersionTextBlock.Text = "版本 " + (version?.ToString(3) ?? "1.0.0");

        if (AppServices.IsInitialized)
        {
            DataPathText.Text = AppServices.Storage.DatabasePath;
        }
    }
}

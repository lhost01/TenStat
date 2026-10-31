using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TenStat.Helpers;

/// <summary>危险操作的统一二次确认对话框。</summary>
public static class DialogHelper
{
    public static async Task<bool> ConfirmAsync(
        XamlRoot? xamlRoot,
        string title,
        string content,
        string primaryText = "确认",
        string closeText = "取消",
        bool destructive = true)
    {
        if (xamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        };

        if (destructive)
        {
            dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
        }

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public static async Task ShowAsync(XamlRoot? xamlRoot, string title, string content, string closeText = "知道了")
    {
        if (xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
    }
}

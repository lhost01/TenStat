using CommunityToolkit.Mvvm.ComponentModel;

namespace TenStat.ViewModels;

/// <summary>仪表盘/历史页的概览卡片。</summary>
public sealed partial class StatCardItem : ObservableObject
{
    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string value = "0";

    [ObservableProperty]
    private string subtitle = string.Empty;

    [ObservableProperty]
    private string glyph = "\uE8A0";

    public StatCardItem(string title, string glyph)
    {
        this.title = title;
        this.glyph = glyph;
    }

    public void Update(string value, string subtitle)
    {
        Value = value;
        Subtitle = subtitle;
    }
}

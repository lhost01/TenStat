using System.Collections.Generic;
using System.Text;

namespace TenStat.Helpers;

/// <summary>
/// 虚拟键码 → 可读名称。只做“名称映射”，永远不读取、不保存任何输入内容。
/// 未覆盖的键统一显示 VK_XX（十六进制）。
/// </summary>
public static class KeyNameMapper
{
    private static readonly Dictionary<int, string> Map = Build();

    public static string GetName(int vk)
    {
        if (Map.TryGetValue(vk, out var name)) return name;
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);              // F1-F24
        if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);            // 小键盘 0-9
        return "VK_" + vk.ToString("X2");
    }

    /// <summary>热键显示用：修饰键 + 主键。</summary>
    public static string DescribeHotkey(uint modifiers, uint vk)
    {
        var sb = new StringBuilder();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) sb.Append("Ctrl+");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) sb.Append("Alt+");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) sb.Append("Shift+");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) sb.Append("Win+");
        sb.Append(GetName((int)vk));
        return sb.ToString();
    }

    private static Dictionary<int, string> Build()
    {
        var m = new Dictionary<int, string>
        {
            [0x01] = "鼠标左键",
            [0x02] = "鼠标右键",
            [0x03] = "Cancel",
            [0x04] = "鼠标中键",
            [0x05] = "鼠标侧键1",
            [0x06] = "鼠标侧键2",
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0C] = "Clear",
            [0x0D] = "Enter",
            [0x10] = "Shift",
            [0x11] = "Ctrl",
            [0x12] = "Alt",
            [0x13] = "Pause",
            [0x14] = "CapsLock",
            [0x15] = "Kana",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "←",
            [0x26] = "↑",
            [0x27] = "→",
            [0x28] = "↓",
            [0x29] = "Select",
            [0x2A] = "Print",
            [0x2B] = "Execute",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x2F] = "Help",
            [0x5B] = "Win",
            [0x5C] = "Win(右)",
            [0x5D] = "Menu",
            [0x5F] = "Sleep",
            [0x6A] = "Num*",
            [0x6B] = "Num+",
            [0x6C] = "NumEnter",
            [0x6D] = "Num-",
            [0x6E] = "Num.",
            [0x6F] = "Num/",
            [0x90] = "NumLock",
            [0x91] = "ScrollLock",
            [0xA0] = "LShift",
            [0xA1] = "RShift",
            [0xA2] = "LCtrl",
            [0xA3] = "RCtrl",
            [0xA4] = "LAlt",
            [0xA5] = "RAlt",
            [0xA6] = "浏览器后退",
            [0xA7] = "浏览器前进",
            [0xA8] = "浏览器刷新",
            [0xA9] = "浏览器停止",
            [0xAA] = "浏览器搜索",
            [0xAB] = "浏览器收藏",
            [0xAC] = "浏览器主页",
            [0xAD] = "静音",
            [0xAE] = "音量-",
            [0xAF] = "音量+",
            [0xB0] = "下一曲",
            [0xB1] = "上一曲",
            [0xB2] = "停止播放",
            [0xB3] = "播放/暂停",
            [0xB4] = "启动邮件",
            [0xB5] = "启动媒体",
            [0xB6] = "启动应用1",
            [0xB7] = "启动应用2",
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = ",",
            [0xBD] = "-",
            [0xBE] = ".",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "'",
            [0xE2] = "OEM102",
            [0xE5] = "IME Process",
            [0xE7] = "IME 转换",
            [0xF6] = "Attn",
            [0xF7] = "CrSel",
            [0xF8] = "ExSel",
            [0xF9] = "ErEOF",
            [0xFA] = "Play",
            [0xFB] = "Zoom",
            [0xFE] = "Clear",
        };
        return m;
    }
}

using System.Collections.Generic;

namespace TenStat.Helpers;

/// <summary>键盘热力图用的物理布局（ANSI 104 + 常用功能键）。</summary>
public static class KeyboardLayout
{
    public sealed class KeyDef
    {
        public int Vk { get; }
        public string Label { get; }
        /// <summary>相对宽度（1 = 标准键，2 = 双宽，等等）。</summary>
        public double Width { get; }

        public KeyDef(int vk, string label, double width = 1)
        {
            Vk = vk;
            Label = label;
            Width = width;
        }
    }

    /// <summary>返回若干行，每行若干键。</summary>
    public static IReadOnlyList<IReadOnlyList<KeyDef>> Rows { get; } = Build();

    private static IReadOnlyList<IReadOnlyList<KeyDef>> Build()
    {
        var rows = new List<IReadOnlyList<KeyDef>>
        {
            new List<KeyDef>
            {
                new(0x1B, "Esc"), new(0x70, "F1"), new(0x71, "F2"), new(0x72, "F3"), new(0x73, "F4"),
                new(0x74, "F5"), new(0x75, "F6"), new(0x76, "F7"), new(0x77, "F8"),
                new(0x78, "F9"), new(0x79, "F10"), new(0x7A, "F11"), new(0x7B, "F12"),
            },
            new List<KeyDef>
            {
                new(0xC0, "`"), new(0x31, "1"), new(0x32, "2"), new(0x33, "3"), new(0x34, "4"),
                new(0x35, "5"), new(0x36, "6"), new(0x37, "7"), new(0x38, "8"), new(0x39, "9"),
                new(0x30, "0"), new(0xBD, "-"), new(0xBB, "="), new(0x08, "Back", 2.0),
            },
            new List<KeyDef>
            {
                new(0x09, "Tab", 1.5), new(0x51, "Q"), new(0x57, "W"), new(0x45, "E"), new(0x52, "R"),
                new(0x54, "T"), new(0x59, "Y"), new(0x55, "U"), new(0x49, "I"), new(0x4F, "O"),
                new(0x50, "P"), new(0xDB, "["), new(0xDD, "]"), new(0xDC, "\\", 1.5),
            },
            new List<KeyDef>
            {
                new(0x14, "Caps", 1.75), new(0x41, "A"), new(0x53, "S"), new(0x44, "D"), new(0x46, "F"),
                new(0x47, "G"), new(0x48, "H"), new(0x4A, "J"), new(0x4B, "K"), new(0x4C, "L"),
                new(0xBA, ";"), new(0xDE, "'"), new(0x0D, "Enter", 2.25),
            },
            new List<KeyDef>
            {
                new(0xA0, "LShift", 2.25), new(0x5A, "Z"), new(0x58, "X"), new(0x43, "C"), new(0x56, "V"),
                new(0x42, "B"), new(0x4E, "N"), new(0x4D, "M"), new(0xBC, ","), new(0xBE, "."),
                new(0xBF, "/"), new(0xA1, "RShift", 2.75),
            },
            new List<KeyDef>
            {
                new(0xA2, "LCtrl", 1.25), new(0x5B, "Win", 1.25), new(0xA4, "LAlt", 1.25),
                new(0x20, "Space", 6.25), new(0xA5, "RAlt", 1.25), new(0x5C, "RWin", 1.25),
                new(0x5D, "Menu", 1.25), new(0xA3, "RCtrl", 1.25),
            },
        };

        // 右侧功能键区（单独一列展示在末尾）
        rows.Add(new List<KeyDef>
        {
            new(0x2C, "PrtSc"), new(0x91, "ScrLk"), new(0x13, "Pause"),
            new(0x2D, "Ins"), new(0x24, "Home"), new(0x21, "PgUp"),
            new(0x2E, "Del"), new(0x23, "End"), new(0x22, "PgDn"),
            new(0x25, "←"), new(0x26, "↑"), new(0x27, "→"), new(0x28, "↓"),
        });

        return rows;
    }
}

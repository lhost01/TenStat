using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TenStat.Services;

/// <summary>
/// 运行时合成 model3.json。
///
/// 背景：模型的 model3.json 里 FileReferences 通常只有 Moc/Textures/Physics，
/// 而目录里实际躺着 motion3 / exp3 文件（VTube 导出的按键表情）。
/// 模型目录是只读引用，所以不改文件 —— 由宿主现算一份 JSON 注入页面。
///
/// 关键点：Cubism 4 里 Motions / Expressions 挂在 FileReferences 下面（不是根上），
/// pixi-live2d-display 0.4.0 读的就是 json.FileReferences.Motions / .Expressions。
/// </summary>
public static class ModelSettingsBuilder
{
    private static readonly string[] IdleCandidates =
    {
        "待机动画124.motion3.json", "待机动画.motion3.json", "idle.motion3.json", "Idle.motion3.json", "idle_00.motion3.json"
    };

    private static readonly string[] IdlePatterns = { "*待机*.motion3.json", "*idle*.motion3.json" };

    private static readonly string[] TapCandidates =
    {
        "手势3比心.motion3.json", "手势.motion3.json", "tap_body.motion3.json", "TapBody.motion3.json"
    };

    private static readonly string[] TapPatterns = { "*比心*.motion3.json", "*手势*.motion3.json", "*tap*.motion3.json" };

    /// <summary>合成一份带 Motions / Expressions 的 model3.json 文本。</summary>
    public static string Build(string modelDir, string modelFile, out string diagnostics)
    {
        var diag = new StringBuilder();
        var modelPath = Path.Combine(modelDir, modelFile);

        var root = JsonNode.Parse(ReadTextFile(modelPath)) as JsonObject
                   ?? throw new InvalidOperationException("model3.json 顶层不是 JSON 对象");

        if (root["FileReferences"] is not JsonObject fileRefs)
        {
            fileRefs = new JsonObject();
            root["FileReferences"] = fileRefs;
        }

        // ---- Motions ----
        var motions = fileRefs["Motions"] as JsonObject;
        if (motions is null)
        {
            motions = new JsonObject();
            fileRefs["Motions"] = motions;
        }

        var motionFiles = ListFiles(modelDir, "*.motion3.json");
        diag.Append("motion3 ").Append(motionFiles.Count).Append(" 个；");

        var idle = FirstExisting(motionFiles, IdleCandidates) ?? FirstMatching(motionFiles, IdlePatterns);
        if (idle is not null && AddMotionGroup(motions, "Idle", idle))
        {
            diag.Append("Idle←").Append(idle).Append("；");
        }

        var tap = FirstExisting(motionFiles, TapCandidates) ?? FirstMatching(motionFiles, TapPatterns);
        if (tap is not null && AddMotionGroup(motions, "TapBody", tap))
        {
            diag.Append("TapBody←").Append(tap).Append("；");
        }

        if (motions.Count == 0) fileRefs.Remove("Motions");

        // ---- Expressions ----
        var expressions = fileRefs["Expressions"] as JsonArray;
        if (expressions is null)
        {
            expressions = new JsonArray();
            fileRefs["Expressions"] = expressions;
        }

        var used = new HashSet<string>(
            expressions.OfType<JsonObject>()
                       .Select(o => (string?)o["Name"])
                       .Where(n => !string.IsNullOrEmpty(n))!,
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var name in ListFiles(modelDir, "*.exp3.json"))
        {
            var display = name.EndsWith(".exp3.json", StringComparison.OrdinalIgnoreCase)
                ? name[..^".exp3.json".Length]
                : name;
            if (string.IsNullOrWhiteSpace(display) || !used.Add(display)) continue;

            expressions.Add(new JsonObject { ["Name"] = display, ["File"] = name });
            added++;
        }

        diag.Append("exp3 新增 ").Append(added).Append(" 个");
        if (expressions.Count == 0) fileRefs.Remove("Expressions");

        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        diagnostics = diag.ToString();
        return root.ToJsonString(options);
    }

    public static string ReadTextFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    public static List<string> ListFiles(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly)
                .Select(f => Path.GetFileName(f) ?? string.Empty)
                .Where(n => n.Length > 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool AddMotionGroup(JsonObject motions, string group, string file)
    {
        if (motions[group] is not JsonArray arr)
        {
            arr = new JsonArray();
            motions[group] = arr;
        }

        if (arr.OfType<JsonObject>().Any(o => string.Equals((string?)o["File"], file, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        arr.Add(new JsonObject { ["File"] = file });
        return true;
    }

    private static string? FirstExisting(IReadOnlyList<string> files, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var hit = files.FirstOrDefault(f => string.Equals(f, candidate, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }

    private static string? FirstMatching(IReadOnlyList<string> files, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var hit = files.FirstOrDefault(f => regex.IsMatch(f));
            if (hit is not null) return hit;
        }
        return null;
    }
}

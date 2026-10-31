using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace TenStat.Services;

/// <summary>
/// 内嵌桌宠的数据侧：定位模型目录、合成 model3.json、提供 WebView2 环境。
/// 渲染在主窗口里的 <see cref="Controls.Live2DView"/> 完成，**不开第二个进程**。
/// </summary>
public sealed class Live2DPetService
{
    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _envGate = new(1, 1);

    private CoreWebView2Environment? _environment;
    private string _cachedKey = string.Empty;
    private string _cachedJson = string.Empty;

    public Live2DPetService(StorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;
    }

    /// <summary>页面与渲染库所在目录（随程序输出）。</summary>
    public string AppAssetDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Live2D");

    /// <summary>WebView2 的用户数据目录（浏览器缓存、localStorage 等）。</summary>
    public string WebViewUserDataFolder => Path.Combine(_storage.DataDirectory, "WebView2");

    public bool IsPageAvailable => File.Exists(Path.Combine(AppAssetDirectory, "pet.html"));

    /// <summary>模型里可用的表情名（从合成后的 model3.json 里取，点击时随机换）。</summary>
    public IReadOnlyList<string> ExpressionNames { get; private set; } = Array.Empty<string>();

    public event EventHandler<string>? Failed;

    /// <summary>
    /// 模型目录：优先设置里的；否则在程序目录附近找 xiaoyue 模型（开发期方便）。
    /// </summary>
    public string? ResolveModelDirectory()
    {
        var configured = _settings.PetModelDirectory;
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            try
            {
                if (!Directory.Exists(dir.FullName)) continue;
                foreach (var sub in Directory.GetDirectories(dir.FullName, "xiaoyue*"))
                {
                    var inner = Path.Combine(sub, "xiaoyue");
                    if (File.Exists(Path.Combine(inner, "xiaoyue.model3.json"))) return Remember(inner);
                    if (File.Exists(Path.Combine(sub, "xiaoyue.model3.json"))) return Remember(sub);
                }
            }
            catch
            {
                // 目录不可读就跳过
            }
        }

        return null;

        string Remember(string path)
        {
            _settings.PetModelDirectory = path;
            return path;
        }
    }

    /// <summary>目录里的第一个 *.model3.json。</summary>
    public string? ResolveModelFile(string modelDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(modelDirectory, "*.model3.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            AppLog.Warn("枚举模型文件失败：" + ex.Message);
            return null;
        }
    }

    /// <summary>合成（带缓存，模型文件时间戳变了才重算）。</summary>
    public string? TryBuildSettingsJson(string modelDirectory, string modelFile, out string diagnostics)
    {
        diagnostics = string.Empty;
        try
        {
            var path = Path.Combine(modelDirectory, modelFile);
            var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (key == _cachedKey) return _cachedJson;

            var json = ModelSettingsBuilder.Build(modelDirectory, modelFile, out diagnostics);
            _cachedKey = key;
            _cachedJson = json;
            ExpressionNames = ParseExpressionNames(json);
            return json;
        }
        catch (Exception ex)
        {
            diagnostics = ex.Message;
            AppLog.Error("合成 model3.json 失败", ex);
            Failed?.Invoke(this, "模型配置合成失败：" + ex.Message);
            return null;
        }
    }

    private static IReadOnlyList<string> ParseExpressionNames(string settingsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty("FileReferences", out var refs)) return Array.Empty<string>();
            if (!refs.TryGetProperty("Expressions", out var arr) || arr.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var list = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("Name", out var name))
                {
                    var value = name.GetString();
                    if (!string.IsNullOrEmpty(value)) list.Add(value!);
                }
            }
            return list;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>创建（或复用）WebView2 环境。首次会初始化用户数据目录。</summary>
    public async Task<CoreWebView2Environment?> GetEnvironmentAsync()
    {
        if (_environment is not null) return _environment;

        await _envGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_environment is not null) return _environment;

            Directory.CreateDirectory(WebViewUserDataFolder);
            _environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: WebViewUserDataFolder,
                options: null);

            AppLog.Info("WebView2 环境就绪：" + WebViewUserDataFolder);
            return _environment;
        }
        catch (Exception ex)
        {
            AppLog.Error("WebView2 环境初始化失败", ex);
            Failed?.Invoke(this, "WebView2 初始化失败（是否缺少 WebView2 Runtime？）：" + ex.Message);
            return null;
        }
        finally
        {
            _envGate.Release();
        }
    }
}

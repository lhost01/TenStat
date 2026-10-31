using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TenStat.Models;

namespace TenStat.Services;

/// <summary>
/// 设置项：AppSettings 表里的 key/value，外面包一层强类型属性。
/// 任何一次 set 都会“内存立刻生效 + 后台写库”，界面不需要等待。
/// </summary>
public sealed class SettingsService
{
    private readonly StorageService _storage;
    private readonly object _gate = new();
    private Dictionary<string, string> _values;

    public event EventHandler? Changed;

    public SettingsService(StorageService storage)
    {
        _storage = storage;
        _values = _storage.LoadAllSettings();
        _values.TryAdd(KeyTheme, "System");
        _values.TryAdd(KeyMinimizeToTray, "True");
        _values.TryAdd(KeyStartWithWindows, "False");
        _values.TryAdd(KeyCountKeyRepeat, "False");
        _values.TryAdd(KeyDistanceUnit, "Meters");
        _values.TryAdd(KeyDpiCalibration, "1.0");
        _values.TryAdd(KeyAutoSaveSeconds, "15");
        _values.TryAdd(KeyPauseHotkey, "Ctrl+Alt+P");
        _values.TryAdd(KeyPetEnabled, "True");
        _values.TryAdd(KeyPetScale, "0.9");
        _values.TryAdd(KeyPetStayInside, "True");
        _values.TryAdd(KeyPrivacyAccepted, "False");
        _values.TryAdd(KeyExclusionEnabled, "True");
    }

    // ---------------------------------------------------------------- keys
    private const string KeyTheme = "Theme";
    private const string KeyMinimizeToTray = "MinimizeToTray";
    private const string KeyStartWithWindows = "StartWithWindows";
    private const string KeyCountKeyRepeat = "CountKeyRepeat";
    private const string KeyDistanceUnit = "DistanceUnit";
    private const string KeyDpiCalibration = "DpiCalibration";
    private const string KeyAutoSaveSeconds = "AutoSaveIntervalSeconds";
    private const string KeyPauseHotkey = "PauseHotkey";
    private const string KeyPetEnabled = "PetEnabled";
    private const string KeyPetModelDir = "PetModelDir";
    private const string KeyPetScale = "PetScale";
    private const string KeyPetStayInside = "PetStayInsideApp";
    private const string KeyPetX = "PetX";
    private const string KeyPetY = "PetY";
    private const string KeyPrivacyAccepted = "PrivacyAccepted";
    private const string KeyExclusionEnabled = "ExclusionEnabled";
    private const string KeyCurrentSessionId = "CurrentSessionId";

    // ---------------------------------------------------------------- 主题
    public string Theme
    {
        get => Get(KeyTheme, "System");
        set => Set(KeyTheme, value);
    }

    // ---------------------------------------------------------------- 常规
    public bool MinimizeToTray
    {
        get => GetBool(KeyMinimizeToTray, true);
        set => Set(KeyMinimizeToTray, value);
    }

    public bool StartWithWindows
    {
        get => GetBool(KeyStartWithWindows);
        set => Set(KeyStartWithWindows, value);
    }

    // ---------------------------------------------------------------- 统计
    public bool CountKeyRepeat
    {
        get => GetBool(KeyCountKeyRepeat);
        set => Set(KeyCountKeyRepeat, value);
    }

    /// <summary>Pixels 或 Meters。</summary>
    public string DistanceUnit
    {
        get => Get(KeyDistanceUnit, "Meters");
        set => Set(KeyDistanceUnit, value);
    }

    public double DpiCalibration
    {
        get => GetDouble(KeyDpiCalibration, 1.0);
        set => Set(KeyDpiCalibration, Math.Clamp(value, 0.2, 5.0));
    }

    public int AutoSaveIntervalSeconds
    {
        get => Math.Clamp(GetInt(KeyAutoSaveSeconds, 15), 5, 300);
        set => Set(KeyAutoSaveSeconds, Math.Clamp(value, 5, 300));
    }

    // ---------------------------------------------------------------- 隐私
    public string PauseHotkey
    {
        get => Get(KeyPauseHotkey, "Ctrl+Alt+P");
        set => Set(KeyPauseHotkey, value);
    }

    public bool ExclusionEnabled
    {
        get => GetBool(KeyExclusionEnabled, true);
        set => Set(KeyExclusionEnabled, value);
    }

    public bool PrivacyAccepted
    {
        get => GetBool(KeyPrivacyAccepted);
        set => Set(KeyPrivacyAccepted, value);
    }

    // ---------------------------------------------------------------- 桌宠
    public bool PetEnabled
    {
        get => GetBool(KeyPetEnabled, true);
        set => Set(KeyPetEnabled, value);
    }

    public string PetModelDirectory
    {
        get => Get(KeyPetModelDir, string.Empty);
        set => Set(KeyPetModelDir, value);
    }

    public double PetScale
    {
        get => Math.Clamp(GetDouble(KeyPetScale, 0.9), 0.6, 1.8);
        set => Set(KeyPetScale, Math.Clamp(value, 0.6, 1.8));
    }

    /// <summary>true = 只在本程序前台时显示桌宠（切到别的应用/玩游戏就隐藏），拖动也限定在窗口内。</summary>
    public bool PetStayInsideApp
    {
        get => GetBool(KeyPetStayInside, true);
        set => Set(KeyPetStayInside, value);
    }

    public int PetX
    {
        get => GetInt(KeyPetX, -1);
        set => Set(KeyPetX, value);
    }

    public int PetY
    {
        get => GetInt(KeyPetY, -1);
        set => Set(KeyPetY, value);
    }

    // ---------------------------------------------------------------- 会话
    public string CurrentSessionId
    {
        get => Get(KeyCurrentSessionId, string.Empty);
        set => Set(KeyCurrentSessionId, value);
    }

    // ---------------------------------------------------------------- 通用读写
    public string Get(string key, string fallback = "")
        => _values.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
        }
        try { _storage.SetSetting(key, value); }
        catch (Exception ex) { AppLog.Error("写入设置失败: " + key, ex); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool GetBool(string key, bool fallback = false)
        => bool.TryParse(Get(key, fallback ? "True" : "False"), out var b) ? b : fallback;

    public void Set(string key, bool value) => Set(key, value ? "True" : "False");

    public int GetInt(string key, int fallback = 0)
        => int.TryParse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

    public void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

    public double GetDouble(string key, double fallback = 0)
        => double.TryParse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    public void Set(string key, double value) => Set(key, value.ToString("0.###", CultureInfo.InvariantCulture));

    /// <summary>恢复默认设置（不动统计数据）。</summary>
    public void RestoreDefaults()
    {
        Theme = "System";
        MinimizeToTray = true;
        StartWithWindows = false;
        CountKeyRepeat = false;
        DistanceUnit = "Meters";
        DpiCalibration = 1.0;
        AutoSaveIntervalSeconds = 15;
        PauseHotkey = "Ctrl+Alt+P";
        ExclusionEnabled = true;
        PetScale = 1.0;
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate) return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TenStat.Helpers;
using TenStat.Models;

namespace TenStat.Services;

/// <summary>内存里某个键的累计（今天的 / 本次会话的）。</summary>
public sealed class KeyAgg
{
    public long Count;
    public DateTimeOffset LastPressedAt;
}

/// <summary>某个口径下的一屏数字。</summary>
public sealed class StatSnapshot
{
    public long LeftClicks { get; init; }
    public long RightClicks { get; init; }
    public double MovePixels { get; init; }
    public long KeyPresses { get; init; }
    public long ActiveSeconds { get; init; }
    public long KeyKinds { get; init; }

    public long TotalClicks => LeftClicks + RightClicks;
    public bool HasData => TotalClicks > 0 || MovePixels > 0.5 || KeyPresses > 0;
}

/// <summary>最近一小时趋势的一个点（按分钟聚合）。</summary>
public readonly record struct TrendPoint(DateTime Minute, long Clicks, long Keys);

/// <summary>
/// 统计中枢。钩子 → Drain → 内存累计 → 定时落盘，UI 只读这里的内存快照。
/// 线程模型：
///   - Tick() 由 UI 线程的 DispatcherQueueTimer 驱动（300ms）；
///   - 落盘在 Task.Run 里做，不阻塞 UI；
///   - 钩子回调只往 hook service 自己的缓冲里加数。
/// </summary>
public sealed class StatsService : IDisposable
{
    private const int KeyFlushEventThreshold = 100;
    private const int ActiveWindowSeconds = 15;
    private const int TrendMinutes = 60;

    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    private readonly MouseHookService _mouse;
    private readonly KeyboardHookService _keyboard;
    private readonly ExclusionService _exclusion;

    private readonly object _unflushedGate = new();
    private readonly Dictionary<int, (long Count, DateTimeOffset Last)> _unflushedKeys = new();
    private readonly Dictionary<DateTime, (long Clicks, long Keys)> _trend = new();

    private DispatcherQueueTimer? _timer;
    private bool _ticking;
    private bool _flushing;
    private long _pendingEvents;
    private DateTimeOffset _lastFlushUtc = DateTimeOffset.Now;
    private DateTimeOffset _lastTickUtc = DateTimeOffset.Now;
    private DateTimeOffset _lastInputUtc = DateTimeOffset.MinValue;
    private double _activeFraction;

    // 今天（含未落盘）
    private Dictionary<int, KeyAgg> _todayKeys = new();
    private long _todayLeft, _todayRight, _todayKeysTotal, _todayActive;
    private double _todayPixels;

    // 本次会话
    private Dictionary<int, KeyAgg> _sessionKeys = new();
    private long _sessionLeft, _sessionRight, _sessionKeysTotal, _sessionActive;
    private double _sessionPixels;

    // 总计基线（数据库里已经落盘的）+ 未落盘
    private DailyStat _totalBase = new();
    private Counter _unflushed = new();

    private List<KeyStat> _totalKeyRows = new();
    private DateTimeOffset _totalKeysCachedAt = DateTimeOffset.MinValue;

    // ---------------------------------------------------------------- 状态

    public DateOnly Today { get; private set; } = DateOnly.FromDateTime(DateTime.Now);
    public bool IsPaused { get; private set; }
    public DateTimeOffset? PauseUntil { get; private set; }
    public bool IsExcluded { get; private set; }
    public string? ExcludedProcess { get; private set; }

    /// <summary>前台是本程序时也照常统计（保留字段以便以后加"排除自己"的开关）。</summary>
    public bool IsSuppressedBySelf { get; private set; }

    public bool IsCounting => !IsPaused && !IsExcluded && !IsSuppressedBySelf;
    public DateTimeOffset SessionStartedAt => _sessionStartedAt;
    private DateTimeOffset _sessionStartedAt = DateTimeOffset.Now;
    public string SessionId { get; private set; } = string.Empty;
    public long DroppedJumps => _mouse.DroppedJumps;

    /// <summary>「已暂停」的剩余时间（UI 倒计时用）。</summary>
    public TimeSpan? PauseRemaining => PauseUntil is { } until
        ? (until - DateTimeOffset.Now) is { TotalSeconds: > 0 } left ? left : TimeSpan.Zero
        : null;

    public event EventHandler? Updated;
    public event EventHandler? StateChanged;
    public event EventHandler<string>? ErrorRaised;

    public StatsService(
        StorageService storage,
        SettingsService settings,
        MouseHookService mouse,
        KeyboardHookService keyboard,
        ExclusionService exclusion)
    {
        _storage = storage;
        _settings = settings;
        _mouse = mouse;
        _keyboard = keyboard;
        _exclusion = exclusion;
    }

    // ---------------------------------------------------------------- 初始化

    public async Task InitializeAsync()
    {
        Today = DateOnly.FromDateTime(DateTime.Now);

        var today = Today;
        var daily = await Task.Run(() => _storage.GetDaily(today));
        _todayLeft = daily.LeftClicks;
        _todayRight = daily.RightClicks;
        _todayPixels = daily.MovePixels;
        _todayKeysTotal = daily.KeyPresses;
        _todayActive = daily.ActiveSeconds;

        var todayKeys = await Task.Run(() => _storage.GetKeyStats(today));
        _todayKeys = new Dictionary<int, KeyAgg>(todayKeys.Count);
        foreach (var k in todayKeys)
        {
            _todayKeys[k.VirtualKeyCode] = new KeyAgg { Count = k.Count, LastPressedAt = k.LastPressedAt ?? DateTimeOffset.Now };
        }

        SessionId = Guid.NewGuid().ToString("N");
        _sessionStartedAt = DateTimeOffset.Now;
        _settings.CurrentSessionId = SessionId;

        await Task.Run(() =>
        {
            _storage.CloseDanglingSessions();
            _storage.UpsertSession(new SessionStat
            {
                SessionId = SessionId,
                StartedAt = _sessionStartedAt,
            });
        });

        _totalBase = await Task.Run(() => _storage.GetTotals());
        AppLog.Info($"统计初始化完成：今日左键 {_todayLeft} / 右键 {_todayRight} / 按键 {_todayKeysTotal}；总计按键 {_totalBase.KeyPresses}");
    }

    public void Start(DispatcherQueue dispatcher)
    {
        _timer ??= dispatcher.CreateTimer();
        if (_timer is null) return;
        _timer.Interval = TimeSpan.FromMilliseconds(300);
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        _timer.Start();
        _lastTickUtc = DateTimeOffset.Now;
        _lastFlushUtc = DateTimeOffset.Now;
    }

    public void Stop()
    {
        if (_timer is null) return;
        _timer.Tick -= OnTimerTick;
        _timer.Stop();
    }

    // ---------------------------------------------------------------- 暂停 / 恢复

    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;
        PauseUntil = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PauseFor(TimeSpan duration)
    {
        IsPaused = true;
        PauseUntil = DateTimeOffset.Now + duration;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        PauseUntil = null;
        _lastTickUtc = DateTimeOffset.Now;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    // ---------------------------------------------------------------- 快照

    public StatSnapshot GetSnapshot(StatScope scope) => scope switch
    {
        StatScope.Session => new StatSnapshot
        {
            LeftClicks = _sessionLeft,
            RightClicks = _sessionRight,
            MovePixels = _sessionPixels,
            KeyPresses = _sessionKeysTotal,
            ActiveSeconds = _sessionActive,
            KeyKinds = _sessionKeys.Count,
        },
        StatScope.Total => new StatSnapshot
        {
            LeftClicks = _totalBase.LeftClicks + _unflushed.Left,
            RightClicks = _totalBase.RightClicks + _unflushed.Right,
            MovePixels = _totalBase.MovePixels + _unflushed.Pixels,
            KeyPresses = _totalBase.KeyPresses + _unflushed.Keys,
            ActiveSeconds = _totalBase.ActiveSeconds + _unflushed.Active,
            KeyKinds = Math.Max(_totalKeyRows.Count, _todayKeys.Count),
        },
        _ => new StatSnapshot
        {
            LeftClicks = _todayLeft,
            RightClicks = _todayRight,
            MovePixels = _todayPixels,
            KeyPresses = _todayKeysTotal,
            ActiveSeconds = _todayActive,
            KeyKinds = _todayKeys.Count,
        },
    };

    /// <summary>某个口径下的按键排行（只含 Count &gt; 0 的键）。</summary>
    public IReadOnlyList<KeyStat> GetKeyRows(StatScope scope)
    {
        switch (scope)
        {
            case StatScope.Session:
                return _sessionKeys
                    .Select(kv => new KeyStat
                    {
                        Date = Today,
                        VirtualKeyCode = kv.Key,
                        KeyName = KeyNameMapper.GetName(kv.Key),
                        Count = kv.Value.Count,
                        LastPressedAt = kv.Value.LastPressedAt,
                    })
                    .Where(k => k.Count > 0)
                    .OrderByDescending(k => k.Count)
                    .ToList();

            case StatScope.Total:
            {
                var map = new Dictionary<int, KeyStat>();
                foreach (var row in _totalKeyRows)
                {
                    map[row.VirtualKeyCode] = new KeyStat
                    {
                        VirtualKeyCode = row.VirtualKeyCode,
                        KeyName = row.KeyName,
                        Count = row.Count,
                        LastPressedAt = row.LastPressedAt,
                    };
                }
                lock (_unflushedGate)
                {
                    foreach (var kv in _unflushedKeys)
                    {
                        if (map.TryGetValue(kv.Key, out var existing))
                        {
                            existing.Count += kv.Value.Count;
                            if (kv.Value.Last > (existing.LastPressedAt ?? DateTimeOffset.MinValue))
                                existing.LastPressedAt = kv.Value.Last;
                        }
                        else
                        {
                            map[kv.Key] = new KeyStat
                            {
                                VirtualKeyCode = kv.Key,
                                KeyName = KeyNameMapper.GetName(kv.Key),
                                Count = kv.Value.Count,
                                LastPressedAt = kv.Value.Last,
                            };
                        }
                    }
                }
                return map.Values.Where(k => k.Count > 0).OrderByDescending(k => k.Count).ToList();
            }

            default:
                return _todayKeys
                    .Select(kv => new KeyStat
                    {
                        Date = Today,
                        VirtualKeyCode = kv.Key,
                        KeyName = KeyNameMapper.GetName(kv.Key),
                        Count = kv.Value.Count,
                        LastPressedAt = kv.Value.LastPressedAt,
                    })
                    .Where(k => k.Count > 0)
                    .OrderByDescending(k => k.Count)
                    .ToList();
        }
    }

    /// <summary>最近 60 分钟的趋势（缺的分钟补 0）。</summary>
    public IReadOnlyList<TrendPoint> GetRecentTrend()
    {
        var now = DateTime.Now;
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        var list = new List<TrendPoint>(TrendMinutes);
        for (var i = TrendMinutes - 1; i >= 0; i--)
        {
            var minute = currentMinute.AddMinutes(-i);
            _trend.TryGetValue(minute, out var bucket);
            list.Add(new TrendPoint(minute, bucket.Clicks, bucket.Keys));
        }
        return list;
    }

    /// <summary>刷新总计口径的按键数据（切到“总计”页签或落盘后调用）。</summary>
    public async Task RefreshTotalKeysAsync(bool force = false)
    {
        if (!force && DateTimeOffset.Now - _totalKeysCachedAt < TimeSpan.FromSeconds(5)) return;
        _totalKeyRows = await Task.Run(() => _storage.GetKeyStatsTotal());
        _totalKeysCachedAt = DateTimeOffset.Now;
    }

    // ---------------------------------------------------------------- tick

    private void OnTimerTick(DispatcherQueueTimer sender, object args) => _ = TickAsync();

    public async Task TickAsync()
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            var now = DateTimeOffset.Now;
            var today = DateOnly.FromDateTime(now.LocalDateTime);

            if (today != Today)
            {
                await RolloverAsync(today, now);
            }

            if (IsPaused && PauseUntil is { } until && now >= until)
            {
                Resume();
            }

            var mouseDelta = _mouse.Drain();
            var keyDeltas = _keyboard.Drain();
            UpdateExclusion();

            var hasInput = mouseDelta.LeftClicks > 0 || mouseDelta.RightClicks > 0
                           || mouseDelta.MovePixels > 0 || keyDeltas.Count > 0;

            if (hasInput && IsCounting)
            {
                ApplyInput(now, mouseDelta, keyDeltas);
                _lastInputUtc = now;
            }

            if (IsCounting)
            {
                AccumulateActiveSeconds(now);
            }

            await MaybeFlushAsync(now);
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("统计 tick 异常", ex);
            ErrorRaised?.Invoke(this, ex.Message);
        }
        finally
        {
            _ticking = false;
        }
    }

    private void ApplyInput(DateTimeOffset now, MouseDelta mouse, List<KeyDelta> keys)
    {
        // 今天
        _todayLeft += mouse.LeftClicks;
        _todayRight += mouse.RightClicks;
        _todayPixels += mouse.MovePixels;

        // 会话
        _sessionLeft += mouse.LeftClicks;
        _sessionRight += mouse.RightClicks;
        _sessionPixels += mouse.MovePixels;

        long keyTotal = 0;
        foreach (var k in keys)
        {
            keyTotal += k.Count;

            if (_todayKeys.TryGetValue(k.VirtualKeyCode, out var todayAgg))
            {
                todayAgg.Count += k.Count;
                todayAgg.LastPressedAt = k.LastPressedAt;
            }
            else
            {
                _todayKeys[k.VirtualKeyCode] = new KeyAgg { Count = k.Count, LastPressedAt = k.LastPressedAt };
            }

            if (_sessionKeys.TryGetValue(k.VirtualKeyCode, out var sessionAgg))
            {
                sessionAgg.Count += k.Count;
                sessionAgg.LastPressedAt = k.LastPressedAt;
            }
            else
            {
                _sessionKeys[k.VirtualKeyCode] = new KeyAgg { Count = k.Count, LastPressedAt = k.LastPressedAt };
            }
        }
        _todayKeysTotal += keyTotal;
        _sessionKeysTotal += keyTotal;

        // 未落盘
        lock (_unflushedGate)
        {
            _unflushed.Left += mouse.LeftClicks;
            _unflushed.Right += mouse.RightClicks;
            _unflushed.Pixels += mouse.MovePixels;
            _unflushed.Keys += keyTotal;
            foreach (var k in keys)
            {
                if (_unflushedKeys.TryGetValue(k.VirtualKeyCode, out var agg))
                {
                    _unflushedKeys[k.VirtualKeyCode] = (agg.Count + k.Count, k.LastPressedAt);
                }
                else
                {
                    _unflushedKeys[k.VirtualKeyCode] = (k.Count, k.LastPressedAt);
                }
            }
        }

        _pendingEvents += mouse.LeftClicks + mouse.RightClicks + keyTotal;

        // 趋势：按分钟聚合
        var minute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
        _trend.TryGetValue(minute, out var bucket);
        _trend[minute] = (bucket.Clicks + mouse.LeftClicks + mouse.RightClicks, bucket.Keys + keyTotal);
        TrimTrend(minute);
    }

    private void TrimTrend(DateTime currentMinute)
    {
        if (_trend.Count <= TrendMinutes * 3) return;
        var cutoff = currentMinute.AddMinutes(-TrendMinutes * 2);
        var stale = _trend.Keys.Where(k => k < cutoff).ToList();
        foreach (var key in stale) _trend.Remove(key);
    }

    private void AccumulateActiveSeconds(DateTimeOffset now)
    {
        var elapsed = (now - _lastTickUtc).TotalSeconds;
        _lastTickUtc = now;

        if (elapsed <= 0 || elapsed > 5) return; // 睡眠/挂起后跳过的空档不算活跃
        if ((now - _lastInputUtc).TotalSeconds > ActiveWindowSeconds) return;

        _activeFraction += elapsed;
        var whole = (long)_activeFraction;
        if (whole <= 0) return;
        _activeFraction -= whole;

        _todayActive += whole;
        _sessionActive += whole;
        lock (_unflushedGate) { _unflushed.Active += whole; }
    }

    private void UpdateExclusion()
    {
        var excluded = _exclusion.IsForegroundExcluded(out var process);
        var selfForeground = _exclusion.IsForegroundAlwaysExcluded(out _);
        var changed = false;

        if (selfForeground != IsSuppressedBySelf)
        {
            IsSuppressedBySelf = selfForeground;
            changed = true;
        }

        if (excluded != IsExcluded || !string.Equals(process, ExcludedProcess, StringComparison.OrdinalIgnoreCase))
        {
            IsExcluded = excluded;
            ExcludedProcess = process;
            changed = true;
        }

        if (changed) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- 落盘

    private bool ShouldFlush(DateTimeOffset now)
        => _pendingEvents >= KeyFlushEventThreshold
           || (now - _lastFlushUtc).TotalSeconds >= _settings.AutoSaveIntervalSeconds;

    private async Task MaybeFlushAsync(DateTimeOffset now)
    {
        lock (_unflushedGate)
        {
            if (_unflushed.IsEmpty && _pendingEvents == 0 && !ShouldFlush(now)) return;
        }

        if (_pendingEvents == 0 && (now - _lastFlushUtc).TotalSeconds < _settings.AutoSaveIntervalSeconds)
        {
            return;
        }

        await FlushAsync();
    }

    /// <summary>把内存里未落盘的增量写进 SQLite。</summary>
    public async Task FlushAsync()
    {
        if (_flushing) return;
        _flushing = true;

        long left, right, keys, active;
        double pixels;
        List<(int Vk, long Count, DateTimeOffset Last)> keyList;
        var date = Today;
        SessionStat sessionSnapshot;

        lock (_unflushedGate)
        {
            left = _unflushed.Left;
            right = _unflushed.Right;
            pixels = _unflushed.Pixels;
            keys = _unflushed.Keys;
            active = _unflushed.Active;
            keyList = _unflushedKeys.Select(kv => (kv.Key, kv.Value.Count, kv.Value.Last)).ToList();

            _unflushed = new Counter();
            _unflushedKeys.Clear();
        }

        _pendingEvents = 0;
        _lastFlushUtc = DateTimeOffset.Now;

        // 内存里的“总计基线”立刻加上，保证 UI 的总数不会因为落盘而回跳
        _totalBase.LeftClicks += left;
        _totalBase.RightClicks += right;
        _totalBase.MovePixels += pixels;
        _totalBase.KeyPresses += keys;
        _totalBase.ActiveSeconds += active;

        sessionSnapshot = new SessionStat
        {
            SessionId = SessionId,
            StartedAt = _sessionStartedAt,
            LeftClicks = _sessionLeft,
            RightClicks = _sessionRight,
            MovePixels = _sessionPixels,
            KeyPresses = _sessionKeysTotal,
            ActiveSeconds = _sessionActive,
        };

        if (left == 0 && right == 0 && pixels <= 0 && keys == 0 && active == 0 && _sessionLeft == 0 && _sessionRight == 0 && _sessionPixels <= 0 && _sessionKeysTotal == 0)
        {
            _flushing = false;
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                _storage.AddDailyDelta(date, left, right, pixels, keys, active);
                foreach (var (vk, count, last) in keyList)
                {
                    _storage.AddKeyDelta(date, vk, KeyNameMapper.GetName(vk), count, last);
                }
                _storage.UpsertSession(sessionSnapshot);
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("落盘失败，增量已回滚到内存等待下次重试", ex);
            ErrorRaised?.Invoke(this, "保存到数据库失败：" + ex.Message);

            // 回滚：把增量放回未落盘缓冲，避免丢数据
            lock (_unflushedGate)
            {
                _unflushed.Left += left;
                _unflushed.Right += right;
                _unflushed.Pixels += pixels;
                _unflushed.Keys += keys;
                _unflushed.Active += active;
                foreach (var (vk, count, last) in keyList)
                {
                    if (_unflushedKeys.TryGetValue(vk, out var agg))
                        _unflushedKeys[vk] = (agg.Count + count, last);
                    else
                        _unflushedKeys[vk] = (count, last);
                }
            }
            _totalBase.LeftClicks -= left;
            _totalBase.RightClicks -= right;
            _totalBase.MovePixels -= pixels;
            _totalBase.KeyPresses -= keys;
            _totalBase.ActiveSeconds -= active;
        }
        finally
        {
            _flushing = false;
        }

        if (keys > 0) await RefreshTotalKeysAsync(force: true);
    }

    // ---------------------------------------------------------------- 日期切换 / 重置

    private async Task RolloverAsync(DateOnly newDay, DateTimeOffset now)
    {
        AppLog.Info($"跨天：{Today} → {newDay}，先落盘再重置今日计数");
        await FlushAsync();

        Today = newDay;
        _todayLeft = 0;
        _todayRight = 0;
        _todayPixels = 0;
        _todayKeysTotal = 0;
        _todayActive = 0;
        _todayKeys = new Dictionary<int, KeyAgg>();
        _trend.Clear();
        _lastFlushUtc = now;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>清空今日（危险操作，调用方负责二次确认）。</summary>
    public async Task ResetTodayAsync()
    {
        await FlushAsync();
        var date = Today;
        await Task.Run(() => _storage.ClearDate(date));

        lock (_unflushedGate)
        {
            _unflushed = new Counter();
            _unflushedKeys.Clear();
        }
        _todayLeft = 0;
        _todayRight = 0;
        _todayPixels = 0;
        _todayKeysTotal = 0;
        _todayActive = 0;
        _todayKeys.Clear();
        _trend.Clear();
        _totalKeyRows = new List<KeyStat>();
        _totalBase = await Task.Run(() => _storage.GetTotals());
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>只重置今天的鼠标三列（左键/右键/移动距离），键盘数据不动。</summary>
    public async Task ResetTodayMouseAsync()
    {
        await FlushAsync();
        var date = Today;
        await Task.Run(() => _storage.ResetMouseColumns(date));

        _todayLeft = 0;
        _todayRight = 0;
        _todayPixels = 0;
        _totalBase = await Task.Run(() => _storage.GetTotals());
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>清空全部（危险操作，调用方负责二次确认）。</summary>
    public async Task ResetAllAsync()
    {
        await FlushAsync();
        await Task.Run(() => _storage.ClearAll());

        lock (_unflushedGate)
        {
            _unflushed = new Counter();
            _unflushedKeys.Clear();
        }
        _todayLeft = _todayRight = _todayKeysTotal = _todayActive = 0;
        _todayPixels = 0;
        _todayKeys.Clear();
        _sessionLeft = _sessionRight = _sessionKeysTotal = _sessionActive = 0;
        _sessionPixels = 0;
        _sessionKeys.Clear();
        _trend.Clear();
        _totalKeyRows = new List<KeyStat>();
        _totalBase = new DailyStat();
        _pendingEvents = 0;

        SessionId = Guid.NewGuid().ToString("N");
        _sessionStartedAt = DateTimeOffset.Now;
        _settings.CurrentSessionId = SessionId;
        await Task.Run(() => _storage.UpsertSession(new SessionStat { SessionId = SessionId, StartedAt = _sessionStartedAt }));

        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>退出前调用：保存所有未落盘数据。</summary>
    public async Task ShutdownAsync()
    {
        Stop();
        await FlushAsync();
        AppLog.Info("退出前落盘完成");
    }

    private sealed class Counter
    {
        public long Left;
        public long Right;
        public double Pixels;
        public long Keys;
        public long Active;
        public bool IsEmpty => Left == 0 && Right == 0 && Pixels <= 0 && Keys == 0 && Active == 0;
    }

    public void Dispose()
    {
        Stop();
        _mouse.Dispose();
        _keyboard.Dispose();
    }
}

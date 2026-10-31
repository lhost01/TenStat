using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using TenStat.Helpers;
using TenStat.Models;

namespace TenStat.Services;

/// <summary>
/// 全部 SQLite 访问都走这里。数据库文件：%LocalAppData%\TenStat\stats.db
/// 设计要点：
///  - 单连接 + gate 锁：SQLite 是本地单进程，别开连接池把句柄打散；
///  - WAL + synchronous=NORMAL：写盘快，崩溃后能自动恢复；
///  - 所有累加都是 UPDATE ... SET x = x + @delta，天然幂等/抗并发；
///  - 只存计数与距离，不存任何输入内容。
/// </summary>
public sealed class StorageService : IDisposable
{
    private readonly object _gate = new();
    private SqliteConnection? _connection;

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string ExportDirectory => Path.Combine(DataDirectory, "exports");

    public StorageService(string? dataDirectory = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (dataDirectory is null)
        {
            // 老版本程序叫 MouseKeyboardStats，第一次用新名字启动时把数据目录整体搬过来（含设置与统计数据）
            MigrateLegacyDirectory(localAppData);
        }

        DataDirectory = dataDirectory ?? Path.Combine(localAppData, "TenStat");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ExportDirectory);
        DatabasePath = Path.Combine(DataDirectory, "stats.db");
    }

    /// <summary>
    /// 老版本程序叫 MouseKeyboardStats，第一次用新名字启动时把数据搬过来。
    /// 只搬数据文件：WebView2 缓存很大而且可能被占用，不搬（会自动重建）。
    /// </summary>
    private static void MigrateLegacyDirectory(string localAppData)
    {
        try
        {
            var legacy = Path.Combine(localAppData, "MouseKeyboardStats");
            var current = Path.Combine(localAppData, "TenStat");
            if (!Directory.Exists(legacy) || Directory.Exists(current)) return;

            Directory.CreateDirectory(current);

            foreach (var name in new[] { "stats.db", "stats.db-wal", "stats.db-shm", "app.log", "pet-window.json" })
            {
                var from = Path.Combine(legacy, name);
                if (!File.Exists(from)) continue;

                try
                {
                    File.Copy(from, Path.Combine(current, name), overwrite: false);
                }
                catch (Exception ex)
                {
                    AppLog.Warn("迁移 " + name + " 失败：" + ex.Message);
                }
            }

            try
            {
                var exportsFrom = Path.Combine(legacy, "exports");
                var exportsTo = Path.Combine(current, "exports");
                if (Directory.Exists(exportsFrom) && !Directory.Exists(exportsTo))
                {
                    Directory.CreateDirectory(exportsTo);
                    foreach (var file in Directory.GetFiles(exportsFrom))
                    {
                        File.Copy(file, Path.Combine(exportsTo, Path.GetFileName(file)), overwrite: false);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("迁移导出目录失败：" + ex.Message);
            }

            AppLog.Info("已把旧数据目录（MouseKeyboardStats）里的数据迁移到 " + current);
        }
        catch (Exception ex)
        {
            AppLog.Warn("旧数据目录迁移失败（不影响新目录启动）：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- schema

    public void Initialize()
    {
        lock (_gate)
        {
            RepairIfNeededLocked();
            _connection = OpenLocked();
            ExecuteLocked(SchemaSql);
            MigrateLocked();
        }
    }

    private SqliteConnection OpenLocked()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            """;
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>启动时的自愈：WAL 落盘 + quick_check，坏了就把旧库改名留档重建。</summary>
    private void RepairIfNeededLocked()
    {
        if (!File.Exists(DatabasePath)) return;

        try
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            conn.Open();

            using (var checkpoint = conn.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                try { checkpoint.ExecuteNonQuery(); } catch { /* 没有 WAL 也无所谓 */ }
            }

            using var check = conn.CreateCommand();
            check.CommandText = "PRAGMA quick_check;";
            var result = check.ExecuteScalar() as string;
            if (!string.IsNullOrEmpty(result) && result.Equals("ok", StringComparison.OrdinalIgnoreCase)) return;
            throw new InvalidDataException("quick_check failed: " + result);
        }
        catch (Exception ex)
        {
            var backup = DatabasePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(DatabasePath, backup, true); } catch { /* 尽力而为 */ }
            try { File.Delete(DatabasePath + "-wal"); } catch { }
            try { File.Delete(DatabasePath + "-shm"); } catch { }
            AppLog.Warn("数据库损坏，已备份为 " + backup + "，原因：" + ex.Message);
        }
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS DailyStat (
            Date          TEXT    PRIMARY KEY,
            LeftClicks    INTEGER NOT NULL DEFAULT 0,
            RightClicks   INTEGER NOT NULL DEFAULT 0,
            MovePixels    REAL    NOT NULL DEFAULT 0,
            KeyPresses    INTEGER NOT NULL DEFAULT 0,
            ActiveSeconds INTEGER NOT NULL DEFAULT 0,
            UpdatedAt     TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS KeyStat (
            Date           TEXT    NOT NULL,
            VirtualKeyCode INTEGER NOT NULL,
            KeyName        TEXT    NOT NULL,
            Count          INTEGER NOT NULL DEFAULT 0,
            LastPressedAt  TEXT,
            PRIMARY KEY (Date, VirtualKeyCode)
        );

        CREATE TABLE IF NOT EXISTS SessionStat (
            SessionId     TEXT    PRIMARY KEY,
            StartedAt     TEXT    NOT NULL,
            EndedAt       TEXT,
            LeftClicks    INTEGER NOT NULL DEFAULT 0,
            RightClicks   INTEGER NOT NULL DEFAULT 0,
            MovePixels    REAL    NOT NULL DEFAULT 0,
            KeyPresses    INTEGER NOT NULL DEFAULT 0,
            ActiveSeconds INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS AppSettings (
            Key   TEXT PRIMARY KEY,
            Value TEXT
        );

        CREATE TABLE IF NOT EXISTS ExcludedApp (
            ProcessName TEXT    PRIMARY KEY,
            DisplayName TEXT,
            Enabled     INTEGER NOT NULL DEFAULT 1
        );

        CREATE INDEX IF NOT EXISTS IX_KeyStat_Date ON KeyStat(Date);
        CREATE INDEX IF NOT EXISTS IX_DailyStat_Date ON DailyStat(Date);
        """;

    private void MigrateLocked()
    {
        // 预留迁移位：以后加列时在这里判断 PRAGMA table_info
        SetSettingLocked("SchemaVersion", "1");
    }

    // ---------------------------------------------------------------- helpers

    private void ExecuteLocked(string sql)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand CommandLocked(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return cmd;
    }

    private T WithConnection<T>(Func<SqliteConnection, T> action)
    {
        lock (_gate)
        {
            if (_connection is null) throw new InvalidOperationException("StorageService 尚未 Initialize()");
            return action(_connection);
        }
    }

    private void WithConnection(Action<SqliteConnection> action)
        => WithConnection<object?>(conn => { action(conn); return null; });

    // ---------------------------------------------------------------- DailyStat

    public DailyStat GetDaily(DateOnly date) => WithConnection(_ =>
    {
        using var cmd = CommandLocked("SELECT Date, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds, UpdatedAt FROM DailyStat WHERE Date = @d", ("@d", Formatters.DateKey(date)));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new DailyStat { Date = date };
        return new DailyStat
        {
            Date = date,
            LeftClicks = r.GetInt64(1),
            RightClicks = r.GetInt64(2),
            MovePixels = r.GetDouble(3),
            KeyPresses = r.GetInt64(4),
            ActiveSeconds = r.GetInt64(5),
            UpdatedAt = DateTimeOffset.TryParse(r.GetString(6), out var ts) ? ts : DateTimeOffset.Now,
        };
    });

    /// <summary>把一批增量累加到某天（一条 SQL，事务内由调用方保证）。</summary>
    public void AddDailyDelta(DateOnly date, long left, long right, double pixels, long keys, long activeSeconds)
    {
        if (left == 0 && right == 0 && pixels <= 0 && keys == 0 && activeSeconds == 0) return;
        WithConnection(_ =>
        {
            using var cmd = CommandLocked("""
                INSERT INTO DailyStat (Date, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds, UpdatedAt)
                VALUES (@d, @left, @right, @pixels, @keys, @active, @now)
                ON CONFLICT(Date) DO UPDATE SET
                    LeftClicks    = LeftClicks    + excluded.LeftClicks,
                    RightClicks   = RightClicks   + excluded.RightClicks,
                    MovePixels    = MovePixels    + excluded.MovePixels,
                    KeyPresses    = KeyPresses    + excluded.KeyPresses,
                    ActiveSeconds = ActiveSeconds + excluded.ActiveSeconds,
                    UpdatedAt     = excluded.UpdatedAt;
                """,
                ("@d", Formatters.DateKey(date)), ("@left", left), ("@right", right), ("@pixels", pixels),
                ("@keys", keys), ("@active", activeSeconds), ("@now", DateTimeOffset.Now.ToString("O")));
            cmd.ExecuteNonQuery();
        });
    }

    public List<DailyStat> GetDailyRange(DateOnly from, DateOnly to) => WithConnection(_ =>
    {
        var list = new List<DailyStat>();
        using var cmd = CommandLocked("SELECT Date, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds, UpdatedAt FROM DailyStat WHERE Date >= @f AND Date <= @t ORDER BY Date DESC",
            ("@f", Formatters.DateKey(from)), ("@t", Formatters.DateKey(to)));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DailyStat
            {
                Date = DateOnly.Parse(r.GetString(0)),
                LeftClicks = r.GetInt64(1),
                RightClicks = r.GetInt64(2),
                MovePixels = r.GetDouble(3),
                KeyPresses = r.GetInt64(4),
                ActiveSeconds = r.GetInt64(5),
                UpdatedAt = DateTimeOffset.TryParse(r.GetString(6), out var ts) ? ts : DateTimeOffset.Now,
            });
        }
        return list;
    });

    public List<DailyStat> GetRecentDaily(int days) => WithConnection(_ =>
    {
        var list = new List<DailyStat>();
        using var cmd = CommandLocked("SELECT Date, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds, UpdatedAt FROM DailyStat ORDER BY Date DESC LIMIT @n", ("@n", days));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DailyStat
            {
                Date = DateOnly.Parse(r.GetString(0)),
                LeftClicks = r.GetInt64(1),
                RightClicks = r.GetInt64(2),
                MovePixels = r.GetDouble(3),
                KeyPresses = r.GetInt64(4),
                ActiveSeconds = r.GetInt64(5),
                UpdatedAt = DateTimeOffset.TryParse(r.GetString(6), out var ts) ? ts : DateTimeOffset.Now,
            });
        }
        return list;
    });

    public DailyStat GetTotals() => WithConnection(_ =>
    {
        using var cmd = CommandLocked("SELECT IFNULL(SUM(LeftClicks),0), IFNULL(SUM(RightClicks),0), IFNULL(SUM(MovePixels),0), IFNULL(SUM(KeyPresses),0), IFNULL(SUM(ActiveSeconds),0) FROM DailyStat");
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new DailyStat();
        return new DailyStat
        {
            LeftClicks = r.GetInt64(0),
            RightClicks = r.GetInt64(1),
            MovePixels = r.GetDouble(2),
            KeyPresses = r.GetInt64(3),
            ActiveSeconds = r.GetInt64(4),
            UpdatedAt = DateTimeOffset.Now,
        };
    });

    // ---------------------------------------------------------------- KeyStat

    public List<KeyStat> GetKeyStats(DateOnly date) => WithConnection(_ =>
    {
        var list = new List<KeyStat>();
        using var cmd = CommandLocked("SELECT VirtualKeyCode, KeyName, Count, LastPressedAt FROM KeyStat WHERE Date = @d AND Count > 0 ORDER BY Count DESC",
            ("@d", Formatters.DateKey(date)));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new KeyStat
            {
                Date = date,
                VirtualKeyCode = r.GetInt32(0),
                KeyName = r.GetString(1),
                Count = r.GetInt64(2),
                LastPressedAt = r.IsDBNull(3) ? null : DateTimeOffset.TryParse(r.GetString(3), out var ts) ? ts : null,
            });
        }
        return list;
    });

    /// <summary>总计口径：所有日期按按键合并。</summary>
    public List<KeyStat> GetKeyStatsTotal() => WithConnection(_ =>
    {
        var list = new List<KeyStat>();
        using var cmd = CommandLocked("""
            SELECT VirtualKeyCode, MAX(KeyName) AS KeyName, SUM(Count) AS Total, MAX(LastPressedAt) AS LastAt
            FROM KeyStat GROUP BY VirtualKeyCode HAVING Total > 0 ORDER BY Total DESC
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new KeyStat
            {
                VirtualKeyCode = r.GetInt32(0),
                KeyName = r.GetString(1),
                Count = r.GetInt64(2),
                LastPressedAt = r.IsDBNull(3) ? null : DateTimeOffset.TryParse(r.GetString(3), out var ts) ? ts : null,
            });
        }
        return list;
    });

    public void AddKeyDelta(DateOnly date, int vk, string name, long count, DateTimeOffset lastPressedAt)
    {
        if (count <= 0) return;
        WithConnection(_ =>
        {
            using var cmd = CommandLocked("""
                INSERT INTO KeyStat (Date, VirtualKeyCode, KeyName, Count, LastPressedAt)
                VALUES (@d, @vk, @name, @count, @last)
                ON CONFLICT(Date, VirtualKeyCode) DO UPDATE SET
                    Count         = Count + excluded.Count,
                    KeyName       = excluded.KeyName,
                    LastPressedAt = excluded.LastPressedAt;
                """,
                ("@d", Formatters.DateKey(date)), ("@vk", vk), ("@name", name), ("@count", count),
                ("@last", lastPressedAt.ToString("O")));
            cmd.ExecuteNonQuery();
        });
    }

    // ---------------------------------------------------------------- Session

    public SessionStat GetSession(string sessionId) => WithConnection(_ =>
    {
        using var cmd = CommandLocked("SELECT SessionId, StartedAt, EndedAt, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds FROM SessionStat WHERE SessionId = @id",
            ("@id", sessionId));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return new SessionStat { SessionId = sessionId, StartedAt = DateTimeOffset.Now };
        return new SessionStat
        {
            SessionId = r.GetString(0),
            StartedAt = DateTimeOffset.TryParse(r.GetString(1), out var st) ? st : DateTimeOffset.Now,
            EndedAt = r.IsDBNull(2) ? null : DateTimeOffset.TryParse(r.GetString(2), out var en) ? en : null,
            LeftClicks = r.GetInt64(3),
            RightClicks = r.GetInt64(4),
            MovePixels = r.GetDouble(5),
            KeyPresses = r.GetInt64(6),
            ActiveSeconds = r.GetInt64(7),
        };
    });

    public void UpsertSession(SessionStat session)
    {
        WithConnection(_ =>
        {
            using var cmd = CommandLocked("""
                INSERT INTO SessionStat (SessionId, StartedAt, EndedAt, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds)
                VALUES (@id, @start, @end, @left, @right, @pixels, @keys, @active)
                ON CONFLICT(SessionId) DO UPDATE SET
                    EndedAt       = excluded.EndedAt,
                    LeftClicks    = excluded.LeftClicks,
                    RightClicks   = excluded.RightClicks,
                    MovePixels    = excluded.MovePixels,
                    KeyPresses    = excluded.KeyPresses,
                    ActiveSeconds = excluded.ActiveSeconds;
                """,
                ("@id", session.SessionId), ("@start", session.StartedAt.ToString("O")),
                ("@end", session.EndedAt?.ToString("O")),
                ("@left", session.LeftClicks), ("@right", session.RightClicks),
                ("@pixels", session.MovePixels), ("@keys", session.KeyPresses),
                ("@active", session.ActiveSeconds));
            cmd.ExecuteNonQuery();
        });
    }

    /// <summary>上次运行没来得及收尾的会话，把 EndedAt 补上。</summary>
    public void CloseDanglingSessions()
    {
        WithConnection(_ =>
        {
            using var cmd = CommandLocked("UPDATE SessionStat SET EndedAt = @now WHERE EndedAt IS NULL AND StartedAt < @today",
                ("@now", DateTimeOffset.Now.ToString("O")),
                ("@today", DateTimeOffset.Now.Date.ToString("O")));
            cmd.ExecuteNonQuery();
        });
    }

    // ---------------------------------------------------------------- Settings

    public Dictionary<string, string> LoadAllSettings() => WithConnection(_ =>
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = CommandLocked("SELECT Key, Value FROM AppSettings");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (!r.IsDBNull(0)) dict[r.GetString(0)] = r.IsDBNull(1) ? string.Empty : r.GetString(1);
        }
        return dict;
    });

    public void SetSetting(string key, string? value)
    {
        WithConnection(_ => SetSettingLocked(key, value));
    }

    private void SetSettingLocked(string key, string? value)
    {
        using var cmd = CommandLocked("INSERT INTO AppSettings (Key, Value) VALUES (@k, @v) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value",
            ("@k", key), ("@v", value));
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- Excluded apps

    public List<ExcludedApp> LoadExcludedApps() => WithConnection(_ =>
    {
        var list = new List<ExcludedApp>();
        using var cmd = CommandLocked("SELECT ProcessName, DisplayName, Enabled FROM ExcludedApp ORDER BY ProcessName");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ExcludedApp(r.GetString(0), r.IsDBNull(1) ? string.Empty : r.GetString(1), r.GetInt64(2) != 0));
        }
        return list;
    });

    public void UpsertExcludedApp(ExcludedApp app)
    {
        WithConnection(_ =>
        {
            using var cmd = CommandLocked("INSERT INTO ExcludedApp (ProcessName, DisplayName, Enabled) VALUES (@p, @d, @e) ON CONFLICT(ProcessName) DO UPDATE SET DisplayName = excluded.DisplayName, Enabled = excluded.Enabled",
                ("@p", app.ProcessName), ("@d", app.DisplayName), ("@e", app.Enabled ? 1 : 0));
            cmd.ExecuteNonQuery();
        });
    }

    public void DeleteExcludedApp(string processName)
    {
        WithConnection(_ =>
        {
            using var cmd = CommandLocked("DELETE FROM ExcludedApp WHERE ProcessName = @p", ("@p", processName));
            cmd.ExecuteNonQuery();
        });
    }

    // ---------------------------------------------------------------- 维护

    public void ClearDate(DateOnly date)
    {
        WithConnection(conn =>
        {
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM DailyStat WHERE Date = @d; DELETE FROM KeyStat WHERE Date = @d;";
                cmd.Parameters.AddWithValue("@d", Formatters.DateKey(date));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        });
    }

    /// <summary>只清掉某天的鼠标三列（左键/右键/移动距离），键盘数据保留。</summary>
    public void ResetMouseColumns(DateOnly date)
    {
        WithConnection(_ =>
        {
            using var cmd = CommandLocked("""
                UPDATE DailyStat
                   SET LeftClicks = 0, RightClicks = 0, MovePixels = 0, UpdatedAt = @now
                 WHERE Date = @d;
                """,
                ("@d", Formatters.DateKey(date)), ("@now", DateTimeOffset.Now.ToString("O")));
            cmd.ExecuteNonQuery();
        });
    }

    public void ClearAll()
    {
        WithConnection(conn =>
        {
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM DailyStat; DELETE FROM KeyStat; DELETE FROM SessionStat;";
            cmd.ExecuteNonQuery();
            tx.Commit();
        });
    }

    /// <summary>把一批增量放进一个事务里写，减少 fsync 次数。</summary>
    public void WriteBatch(Action<SqliteConnection, SqliteTransaction> action)
    {
        WithConnection(conn =>
        {
            using var tx = conn.BeginTransaction();
            action(conn, tx);
            tx.Commit();
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _connection?.Dispose(); } catch { }
            _connection = null;
        }
    }
}

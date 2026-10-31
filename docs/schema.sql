-- ============================================================================
-- TenStat（TenStat）SQLite 结构
-- 文件位置：%LocalAppData%\TenStat\stats.db
-- 启动时由 StorageService.Initialize() 以 CREATE TABLE IF NOT EXISTS 执行，
-- 幂等，可重复执行。PRAGMA：journal_mode=WAL / synchronous=NORMAL / busy_timeout=5000。
--
-- 设计前提：只存"次数、距离、时间戳"，任何字段都不保存输入内容。
-- ============================================================================

-- 每一天一行；累加都是 UPDATE ... SET x = x + delta
CREATE TABLE IF NOT EXISTS DailyStat (
    Date          TEXT    PRIMARY KEY,          -- yyyy-MM-dd（本地日期）
    LeftClicks    INTEGER NOT NULL DEFAULT 0,   -- 鼠标左键次数
    RightClicks   INTEGER NOT NULL DEFAULT 0,   -- 鼠标右键次数
    MovePixels    REAL    NOT NULL DEFAULT 0,   -- 移动距离（像素，欧氏距离累加）
    KeyPresses    INTEGER NOT NULL DEFAULT 0,   -- 键盘按下总次数
    ActiveSeconds INTEGER NOT NULL DEFAULT 0,   -- 活跃秒数（有输入的时间片）
    UpdatedAt     TEXT    NOT NULL              -- ISO 8601 带偏移
);

-- 每个"按过的键"一行；没按过的键不会出现在这里
CREATE TABLE IF NOT EXISTS KeyStat (
    Date           TEXT    NOT NULL,            -- yyyy-MM-dd
    VirtualKeyCode INTEGER NOT NULL,            -- Win32 虚拟键码（0x41 = A）
    KeyName        TEXT    NOT NULL,            -- 可读名（A / Enter / VK_6A）
    Count          INTEGER NOT NULL DEFAULT 0,  -- 当天次数
    LastPressedAt  TEXT,                        -- 最后一次按下（ISO 8601）
    PRIMARY KEY (Date, VirtualKeyCode)
);

-- 一次运行会话；"本次会话"口径的数据源
CREATE TABLE IF NOT EXISTS SessionStat (
    SessionId     TEXT    PRIMARY KEY,          -- GUID(N)
    StartedAt     TEXT    NOT NULL,
    EndedAt       TEXT,                         -- 正常退出时补；崩溃留空，下次启动收尾
    LeftClicks    INTEGER NOT NULL DEFAULT 0,
    RightClicks   INTEGER NOT NULL DEFAULT 0,
    MovePixels    REAL    NOT NULL DEFAULT 0,
    KeyPresses    INTEGER NOT NULL DEFAULT 0,
    ActiveSeconds INTEGER NOT NULL DEFAULT 0
);

-- 键值设置（主题、保存间隔、快捷键、桌宠参数……）
CREATE TABLE IF NOT EXISTS AppSettings (
    Key   TEXT PRIMARY KEY,
    Value TEXT
);

-- 排除应用：前台是这个进程时不统计
CREATE TABLE IF NOT EXISTS ExcludedApp (
    ProcessName TEXT    PRIMARY KEY,            -- 不带 .exe，忽略大小写比较
    DisplayName TEXT,
    Enabled     INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX IF NOT EXISTS IX_KeyStat_Date   ON KeyStat(Date);
CREATE INDEX IF NOT EXISTS IX_DailyStat_Date ON DailyStat(Date);

-- ============================================================================
-- 关键 SQL（与代码一一对应）
-- ============================================================================

-- 1) 批量累加某天的所有计数（StorageService.AddDailyDelta）
-- INSERT INTO DailyStat (Date, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds, UpdatedAt)
-- VALUES (@d, @left, @right, @pixels, @keys, @active, @now)
-- ON CONFLICT(Date) DO UPDATE SET
--     LeftClicks    = LeftClicks    + excluded.LeftClicks,
--     RightClicks   = RightClicks   + excluded.RightClicks,
--     MovePixels    = MovePixels    + excluded.MovePixels,
--     KeyPresses    = KeyPresses    + excluded.KeyPresses,
--     ActiveSeconds = ActiveSeconds + excluded.ActiveSeconds,
--     UpdatedAt     = excluded.UpdatedAt;

-- 2) 按键累加（StorageService.AddKeyDelta）
-- INSERT INTO KeyStat (Date, VirtualKeyCode, KeyName, Count, LastPressedAt)
-- VALUES (@d, @vk, @name, @count, @last)
-- ON CONFLICT(Date, VirtualKeyCode) DO UPDATE SET
--     Count         = Count + excluded.Count,
--     KeyName       = excluded.KeyName,
--     LastPressedAt = excluded.LastPressedAt;

-- 3) 当天按键排行（只取 Count > 0）
-- SELECT VirtualKeyCode, KeyName, Count, LastPressedAt
--   FROM KeyStat WHERE Date = @d AND Count > 0 ORDER BY Count DESC;

-- 4) 历史总计排行（所有日期按虚拟键合并）
-- SELECT VirtualKeyCode, MAX(KeyName) AS KeyName, SUM(Count) AS Total, MAX(LastPressedAt) AS LastAt
--   FROM KeyStat GROUP BY VirtualKeyCode HAVING Total > 0 ORDER BY Total DESC;

-- 5) 历史列表
-- SELECT Date, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds, UpdatedAt
--   FROM DailyStat ORDER BY Date DESC LIMIT @n;

-- 6) 总计口径
-- SELECT IFNULL(SUM(LeftClicks),0), IFNULL(SUM(RightClicks),0), IFNULL(SUM(MovePixels),0),
--        IFNULL(SUM(KeyPresses),0), IFNULL(SUM(ActiveSeconds),0) FROM DailyStat;

-- 7) 清空今日 / 清空全部（二次确认后调用）
-- DELETE FROM DailyStat WHERE Date = @d; DELETE FROM KeyStat WHERE Date = @d;
-- DELETE FROM DailyStat; DELETE FROM KeyStat; DELETE FROM SessionStat;

-- 8) 只重置今天的鼠标三列（键盘数据保留）
-- UPDATE DailyStat SET LeftClicks = 0, RightClicks = 0, MovePixels = 0, UpdatedAt = @now
--  WHERE Date = @d;

-- 9) 崩溃遗留会话收尾（启动时）
-- UPDATE SessionStat SET EndedAt = @now WHERE EndedAt IS NULL AND StartedAt < @today;

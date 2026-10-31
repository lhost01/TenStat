using System.Text;
using Microsoft.Data.Sqlite;

// 开发期排查用的小工具（不参与主程序发布）：
//   dotnet run --project tools/DbTool -- dump
//   dotnet run --project tools/DbTool -- set PrivacyAccepted True
//   dotnet run --project tools/DbTool -- counts
// 只读/改写本地 stats.db，不发网络请求。

Console.OutputEncoding = Encoding.UTF8;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "TenStat",
    "stats.db");

if (!File.Exists(dbPath))
{
    Console.WriteLine("数据库不存在：" + dbPath);
    return 2;
}

using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadWrite,
    Pooling = false,
}.ToString());
conn.Open();

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "dump";

switch (command)
{
    case "set":
    {
        if (args.Length < 3) { Console.WriteLine("用法: set <key> <value>"); return 1; }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES (@k, @v) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value";
        cmd.Parameters.AddWithValue("@k", args[1]);
        cmd.Parameters.AddWithValue("@v", string.Join(' ', args.Skip(2)));
        Console.WriteLine("ok: " + cmd.ExecuteNonQuery());
        break;
    }
    case "get":
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @k";
        cmd.Parameters.AddWithValue("@k", args.Length > 1 ? args[1] : "");
        var value = cmd.ExecuteScalar();
        Console.WriteLine(value is null ? "(null)" : value.ToString());
        break;
    }
    case "clear":
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DailyStat; DELETE FROM KeyStat; DELETE FROM SessionStat;";
        Console.WriteLine("deleted: " + cmd.ExecuteNonQuery());
        break;
    }
    case "counts":
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT IFNULL(SUM(LeftClicks),0) FROM DailyStat),
                   (SELECT IFNULL(SUM(RightClicks),0) FROM DailyStat),
                   (SELECT IFNULL(SUM(MovePixels),0) FROM DailyStat),
                   (SELECT IFNULL(SUM(KeyPresses),0) FROM DailyStat),
                   (SELECT COUNT(DISTINCT VirtualKeyCode) FROM KeyStat),
                   (SELECT COUNT(*) FROM SessionStat);
            """;
        using var r = cmd.ExecuteReader();
        r.Read();
        Console.WriteLine($"left={r.GetInt64(0)} right={r.GetInt64(1)} pixels={r.GetDouble(2):0.0} keys={r.GetInt64(3)} kinds={r.GetInt64(4)} sessions={r.GetInt64(5)}");
        break;
    }
    default:
    {
        using (var daily = conn.CreateCommand())
        {
            daily.CommandText = "SELECT Date, LeftClicks, RightClicks, MovePixels, KeyPresses, ActiveSeconds FROM DailyStat ORDER BY Date DESC LIMIT 10";
            using var r = daily.ExecuteReader();
            Console.WriteLine("[DailyStat]");
            while (r.Read())
            {
                Console.WriteLine($"  {r.GetString(0)}  L={r.GetInt64(1),5}  R={r.GetInt64(2),5}  px={r.GetDouble(3),10:0.0}  keys={r.GetInt64(4),6}  active={r.GetInt64(5)}s");
            }
        }

        using var keys = conn.CreateCommand();
        keys.CommandText = "SELECT Date, VirtualKeyCode, KeyName, Count FROM KeyStat ORDER BY Date DESC, Count DESC LIMIT 20";
        using var kr = keys.ExecuteReader();
        Console.WriteLine("[KeyStat]");
        while (kr.Read())
        {
            Console.WriteLine($"  {kr.GetString(0)}  vk={kr.GetInt32(1),3}  {kr.GetString(2),-12} x{kr.GetInt64(3)}");
        }
        break;
    }
}

return 0;

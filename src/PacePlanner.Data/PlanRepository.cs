using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PacePlanner.Data;

/// <summary>Stores plans in a local SQLite file. The schema is created on first use.</summary>
public sealed class PlanRepository
{
    private const string Columns =
        "name, gpx_file_name, gpx_content, race_distance, distance_km, target_seconds, strategy, " +
        "negative_split_seconds, aid_mode, aid_interval_km, aid_custom_kms, aid_stop_seconds, updated_at";

    private readonly string _connectionString;

    public PlanRepository(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = Open();
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS plans (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                gpx_file_name TEXT NULL,
                gpx_content TEXT NULL,
                race_distance INTEGER NOT NULL,
                distance_km REAL NOT NULL,
                target_seconds INTEGER NOT NULL,
                strategy INTEGER NOT NULL,
                negative_split_seconds INTEGER NOT NULL,
                aid_mode INTEGER NOT NULL,
                aid_interval_km REAL NOT NULL,
                aid_custom_kms TEXT NOT NULL,
                aid_stop_seconds INTEGER NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
    }

    /// <summary>%LocalAppData%\PacePlanner\plans.db</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PacePlanner", "plans.db");

    public IReadOnlyList<PlanSummary> List()
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, updated_at FROM plans ORDER BY updated_at DESC, id DESC";
        using var reader = cmd.ExecuteReader();
        var result = new List<PlanSummary>();
        while (reader.Read())
        {
            result.Add(new PlanSummary(reader.GetInt64(0), reader.GetString(1), ParseTime(reader.GetString(2))));
        }

        return result;
    }

    public SavedPlan? Get(long id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT id, {Columns} FROM plans WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }

        return new SavedPlan
        {
            Id = r.GetInt64(0),
            Name = r.GetString(1),
            GpxFileName = r.IsDBNull(2) ? null : r.GetString(2),
            GpxContent = r.IsDBNull(3) ? null : r.GetString(3),
            RaceDistance = (Core.Pacing.RaceDistance)r.GetInt32(4),
            DistanceKm = r.GetDouble(5),
            TargetSeconds = r.GetInt32(6),
            Strategy = (Core.Pacing.PaceStrategy)r.GetInt32(7),
            NegativeSplitSeconds = r.GetInt32(8),
            AidMode = (Core.Pacing.AidStationMode)r.GetInt32(9),
            AidIntervalKm = r.GetDouble(10),
            AidCustomKms = ParseKms(r.GetString(11)),
            AidStopSeconds = r.GetInt32(12),
            UpdatedAt = ParseTime(r.GetString(13)),
        };
    }

    public long Insert(SavedPlan plan)
    {
        var name = ValidName(plan.Name);
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"INSERT INTO plans ({Columns}) VALUES ({ParameterList()}); SELECT last_insert_rowid();";
        Bind(cmd, plan with { Name = name });
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(SavedPlan plan)
    {
        var name = ValidName(plan.Name);
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        var assignments = string.Join(", ", Columns.Split(", ").Select(c => $"{c} = ${c}"));
        cmd.CommandText = $"UPDATE plans SET {assignments} WHERE id = $id";
        Bind(cmd, plan with { Name = name });
        cmd.Parameters.AddWithValue("$id", plan.Id);
        EnsureFound(cmd.ExecuteNonQuery(), plan.Id);
    }

    public long Duplicate(long id, string newName)
    {
        var original = Get(id) ?? throw new KeyNotFoundException($"找不到方案（id={id}）。");
        return Insert(original with { Id = 0, Name = newName });
    }

    public void Rename(long id, string newName)
    {
        var name = ValidName(newName);
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE plans SET name = $name, updated_at = $updated_at WHERE id = $id";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$updated_at", Now());
        cmd.Parameters.AddWithValue("$id", id);
        EnsureFound(cmd.ExecuteNonQuery(), id);
    }

    public void Delete(long id)
    {
        using var connection = Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM plans WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string ParameterList() => string.Join(", ", Columns.Split(", ").Select(c => "$" + c));

    private static void Bind(SqliteCommand cmd, SavedPlan p)
    {
        var values = new object?[]
        {
            p.Name, p.GpxFileName, p.GpxContent, (int)p.RaceDistance, p.DistanceKm, p.TargetSeconds, (int)p.Strategy,
            p.NegativeSplitSeconds, (int)p.AidMode, p.AidIntervalKm, FormatKms(p.AidCustomKms), p.AidStopSeconds, Now(),
        };
        foreach (var (column, value) in Columns.Split(", ").Zip(values))
        {
            cmd.Parameters.AddWithValue("$" + column, value ?? DBNull.Value);
        }
    }

    private static string ValidName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("方案名稱不可為空白。", nameof(name)) : name.Trim();

    private static void EnsureFound(int affected, long id)
    {
        if (affected == 0)
        {
            throw new KeyNotFoundException($"找不到方案（id={id}）。");
        }
    }

    // Microsecond precision keeps "most recently updated first" ordering stable within a second.
    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);

    private static DateTime ParseTime(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static string FormatKms(IEnumerable<double> kms) =>
        string.Join(';', kms.Select(k => k.ToString("R", CultureInfo.InvariantCulture)));

    private static IReadOnlyList<double> ParseKms(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.Parse(s, CultureInfo.InvariantCulture))
            .ToList();
}

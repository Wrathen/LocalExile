using Microsoft.Data.Sqlite;

namespace GameServer.Storage;

/// <summary>
/// The SQLite overlay for server-side game data: mod stat roll ranges and item limits. The extracted
/// <c>data/tables</c> JSON stays the reference; this store only records overrides the Gateway applies in memory at
/// startup and after a command. One connection is shared by the Gateway's writers and readers and every write is a
/// transaction; the schema lives beside the save tables in <c>data/saves.db</c> (or <c>Game_SAVES_DB</c>).
/// </summary>
public sealed class GameDataStore : IDisposable
{
    public sealed record ModStatOverride(string ModId, int StatIndex, int Minimum, int Maximum);
    public sealed record LimitOverride(string Key, int Value);
    public sealed record CatalogRow(string Kind, string Key, string Payload, bool Enabled);
    public sealed record SettingRow(string Key, double Value);
    public sealed record StatRow(string Id, int Row, uint Hash32, bool IsLocal, bool IsPublic, bool IsWeaponLocal,
        int Category);
    public sealed record ModRow(string Id, int Hash16, string Name, int Domain, int GenerationType, int Level,
        string ModType);
    public sealed record ModStatRow(string ModId, int StatIndex, string StatId, int Minimum, int Maximum, bool Local);

    private readonly SqliteConnection _db;
    private readonly object _gate = new();
    private SqliteTransaction? _tx;

    public string Path { get; }

    public GameDataStore(string path)
    {
        Path = path;
        if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _db.Open();
        try
        {
            Execute("PRAGMA busy_timeout=10000");
            Execute("""
                CREATE TABLE IF NOT EXISTS game_data_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                ) STRICT;
                CREATE TABLE IF NOT EXISTS mod_stat_overrides (
                    mod_id TEXT NOT NULL,
                    stat_index INTEGER NOT NULL,
                    minimum INTEGER NOT NULL,
                    maximum INTEGER NOT NULL,
                    updated_utc TEXT NOT NULL,
                    PRIMARY KEY (mod_id, stat_index),
                    CHECK (minimum <= maximum)
                ) STRICT;
                CREATE TABLE IF NOT EXISTS limit_overrides (
                    key TEXT PRIMARY KEY,
                    value INTEGER NOT NULL,
                    updated_utc TEXT NOT NULL,
                    CHECK (value >= 0)
                ) STRICT;
                CREATE TABLE IF NOT EXISTS system_settings (
                    key TEXT PRIMARY KEY,
                    value REAL NOT NULL,
                    updated_utc TEXT NOT NULL
                ) STRICT;
                CREATE TABLE IF NOT EXISTS stats (
                    id TEXT PRIMARY KEY,
                    row INTEGER NOT NULL,
                    hash32 INTEGER NOT NULL,
                    is_local INTEGER NOT NULL,
                    is_public INTEGER NOT NULL,
                    is_weapon_local INTEGER NOT NULL,
                    category INTEGER NOT NULL
                ) STRICT;
                CREATE TABLE IF NOT EXISTS mods (
                    id TEXT PRIMARY KEY,
                    hash16 INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    domain INTEGER NOT NULL,
                    generation_type INTEGER NOT NULL,
                    level INTEGER NOT NULL,
                    mod_type TEXT NOT NULL
                ) STRICT;
                CREATE TABLE IF NOT EXISTS mod_stats (
                    mod_id TEXT NOT NULL,
                    stat_index INTEGER NOT NULL,
                    stat_id TEXT NOT NULL,
                    minimum INTEGER NOT NULL,
                    maximum INTEGER NOT NULL,
                    "local" INTEGER NOT NULL,
                    PRIMARY KEY (mod_id, stat_index)
                ) STRICT;
                CREATE INDEX IF NOT EXISTS mod_stats_stat_id ON mod_stats (stat_id);
                CREATE INDEX IF NOT EXISTS mods_generation_type ON mods (generation_type, level);
                CREATE TABLE IF NOT EXISTS catalog_entries (
                    kind TEXT NOT NULL,
                    key TEXT NOT NULL,
                    payload TEXT NOT NULL,
                    enabled INTEGER NOT NULL,
                    updated_utc TEXT NOT NULL,
                    PRIMARY KEY (kind, key)
                ) STRICT;
                CREATE TABLE IF NOT EXISTS catalog_seed_keys (
                    kind TEXT NOT NULL,
                    key TEXT NOT NULL,
                    PRIMARY KEY (kind, key)
                ) STRICT;
                CREATE VIEW IF NOT EXISTS effective_mod_stats AS
                    SELECT m.mod_id, m.stat_index, m.stat_id,
                           COALESCE(o.minimum, m.minimum) AS minimum,
                           COALESCE(o.maximum, m.maximum) AS maximum,
                           m."local"
                    FROM mod_stats m
                    LEFT JOIN mod_stat_overrides o ON o.mod_id = m.mod_id AND o.stat_index = m.stat_index;
                CREATE TABLE IF NOT EXISTS item_templates (
                    id TEXT PRIMARY KEY,
                    label TEXT NOT NULL,
                    base_type TEXT NOT NULL,
                    rarity INTEGER NOT NULL,
                    item_level INTEGER NOT NULL,
                    quality INTEGER NOT NULL,
                    custom_name TEXT,
                    name_words TEXT,
                    visual INTEGER,
                    corrupted INTEGER NOT NULL,
                    mirrored INTEGER NOT NULL,
                    mods TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                ) STRICT;
                """);
            Execute("INSERT OR IGNORE INTO game_data_meta (key, value) VALUES ('schema_version', '2')");
            Execute("UPDATE game_data_meta SET value = '2' WHERE key = 'schema_version' AND value = '1'");
        }
        catch
        {
            _db.Dispose();
            throw;
        }
    }

    public IReadOnlyList<ModStatOverride> LoadModRanges()
    {
        lock (_gate)
        {
            using var command = Command("SELECT mod_id, stat_index, minimum, maximum FROM mod_stat_overrides ORDER BY mod_id, stat_index");
            using var reader = command.ExecuteReader();
            var result = new List<ModStatOverride>();
            while (reader.Read())
                result.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
            return result;
        }
    }

    public void SetModRange(string modId, int statIndex, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("mod id is empty", nameof(modId));
        if (statIndex < 0) throw new ArgumentOutOfRangeException(nameof(statIndex));
        if (minimum > maximum) throw new ArgumentException("minimum is above maximum");
        Atomically(() =>
        {
            using var command = Command("""
                INSERT INTO mod_stat_overrides (mod_id, stat_index, minimum, maximum, updated_utc)
                VALUES ($mod, $index, $minimum, $maximum, $utc)
                ON CONFLICT(mod_id, stat_index) DO UPDATE SET
                    minimum = excluded.minimum,
                    maximum = excluded.maximum,
                    updated_utc = excluded.updated_utc
                """, ("$mod", modId), ("$index", statIndex), ("$minimum", minimum), ("$maximum", maximum),
                ("$utc", DateTime.UtcNow.ToString("O")));
            command.ExecuteNonQuery();
        });
    }

    public void ClearModRange(string modId, int? statIndex = null)
    {
        Atomically(() =>
        {
            using var command = statIndex is int index
                ? Command("DELETE FROM mod_stat_overrides WHERE mod_id = $mod AND stat_index = $index", ("$mod", modId), ("$index", index))
                : Command("DELETE FROM mod_stat_overrides WHERE mod_id = $mod", ("$mod", modId));
            command.ExecuteNonQuery();
        });
    }

    public IReadOnlyList<LimitOverride> LoadLimits()
    {
        lock (_gate)
        {
            using var command = Command("SELECT key, value FROM limit_overrides ORDER BY key");
            using var reader = command.ExecuteReader();
            var result = new List<LimitOverride>();
            while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetInt32(1)));
            return result;
        }
    }

    public void SetLimit(string key, int value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("limit key is empty", nameof(key));
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Atomically(() =>
        {
            using var command = Command("""
                INSERT INTO limit_overrides (key, value, updated_utc)
                VALUES ($key, $value, $utc)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc
                """, ("$key", key), ("$value", value), ("$utc", DateTime.UtcNow.ToString("O")));
            command.ExecuteNonQuery();
        });
    }

    public void ClearLimit(string key)
    {
        Atomically(() =>
        {
            using var command = Command("DELETE FROM limit_overrides WHERE key = $key", ("$key", key));
            command.ExecuteNonQuery();
        });
    }

    public bool ReferenceSeeded() => Meta("reference_seeded") is not null;

    public void MarkReferenceSeeded() => SetMeta("reference_seeded", DateTime.UtcNow.ToString("O"));

    /// <summary>The rows the first Gateway start writes from data/tables; 0 when the extraction is missing.</summary>
    public (int Stats, int Mods, int ModStats) ReferenceCounts()
    {
        lock (_gate)
        {
            int Count(string table)
            {
                using var command = Command($"SELECT COUNT(*) FROM {table}");
                return Convert.ToInt32(command.ExecuteScalar());
            }
            return (Count("stats"), Count("mods"), Count("mod_stats"));
        }
    }

    /// <summary>Replace the reference tables in one transaction. This is the startup seed from the extracted
    /// Stats/Mods tables; an admin edit lives in <c>mod_stat_overrides</c> and is not touched.</summary>
    public void SeedReference(IReadOnlyList<StatRow> stats, IReadOnlyList<ModRow> mods,
        IReadOnlyList<ModStatRow> modStats)
    {
        Atomically(() =>
        {
            Execute("DELETE FROM mod_stats");
            Execute("DELETE FROM mods");
            Execute("DELETE FROM stats");

            using (var command = Command("""
                INSERT INTO stats (id, row, hash32, is_local, is_public, is_weapon_local, category)
                VALUES ($id, $row, $hash, $local, $public, $weapon, $category)
                """, ("$id", ""), ("$row", 0), ("$hash", 0L), ("$local", 0), ("$public", 0), ("$weapon", 0),
                ("$category", -1)))
            {
                command.Prepare();
                foreach (var row in stats)
                {
                    command.Parameters["$id"].Value = row.Id;
                    command.Parameters["$row"].Value = row.Row;
                    command.Parameters["$hash"].Value = (long)row.Hash32;
                    command.Parameters["$local"].Value = row.IsLocal ? 1 : 0;
                    command.Parameters["$public"].Value = row.IsPublic ? 1 : 0;
                    command.Parameters["$weapon"].Value = row.IsWeaponLocal ? 1 : 0;
                    command.Parameters["$category"].Value = row.Category;
                    command.ExecuteNonQuery();
                }
            }

            using (var command = Command("""
                INSERT INTO mods (id, hash16, name, domain, generation_type, level, mod_type)
                VALUES ($id, $hash, $name, $domain, $generation, $level, $type)
                """, ("$id", ""), ("$hash", 0), ("$name", ""), ("$domain", 1), ("$generation", 3), ("$level", 1),
                ("$type", "")))
            {
                command.Prepare();
                foreach (var row in mods)
                {
                    command.Parameters["$id"].Value = row.Id;
                    command.Parameters["$hash"].Value = row.Hash16;
                    command.Parameters["$name"].Value = row.Name;
                    command.Parameters["$domain"].Value = row.Domain;
                    command.Parameters["$generation"].Value = row.GenerationType;
                    command.Parameters["$level"].Value = row.Level;
                    command.Parameters["$type"].Value = row.ModType;
                    command.ExecuteNonQuery();
                }
            }

            using (var command = Command("""
                INSERT INTO mod_stats (mod_id, stat_index, stat_id, minimum, maximum, "local")
                VALUES ($mod, $index, $stat, $minimum, $maximum, $local)
                """, ("$mod", ""), ("$index", 0), ("$stat", ""), ("$minimum", 0), ("$maximum", 0), ("$local", 0)))
            {
                command.Prepare();
                foreach (var row in modStats)
                {
                    command.Parameters["$mod"].Value = row.ModId;
                    command.Parameters["$index"].Value = row.StatIndex;
                    command.Parameters["$stat"].Value = row.StatId;
                    command.Parameters["$minimum"].Value = row.Minimum;
                    command.Parameters["$maximum"].Value = row.Maximum;
                    command.Parameters["$local"].Value = row.Local ? 1 : 0;
                    command.ExecuteNonQuery();
                }
            }
        });
        MarkReferenceSeeded();
    }

    public IReadOnlyList<SettingRow> LoadSettings()
    {
        lock (_gate)
        {
            using var command = Command("SELECT key, value FROM system_settings ORDER BY key");
            using var reader = command.ExecuteReader();
            var result = new List<SettingRow>();
            while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetDouble(1)));
            return result;
        }
    }

    public void SetSetting(string key, double value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("setting key is empty", nameof(key));
        if (!double.IsFinite(value)) throw new ArgumentException("setting value is not finite", nameof(value));
        Atomically(() =>
        {
            using var command = Command("""
                INSERT INTO system_settings (key, value, updated_utc)
                VALUES ($key, $value, $utc)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc
                """, ("$key", key), ("$value", value), ("$utc", DateTime.UtcNow.ToString("O")));
            command.ExecuteNonQuery();
        });
    }

    public void DeleteSetting(string key)
    {
        Atomically(() =>
        {
            using var command = Command("DELETE FROM system_settings WHERE key = $key", ("$key", key));
            command.ExecuteNonQuery();
        });
    }

    public bool SettingsSeeded() => Meta("settings_seeded") is not null;

    public void MarkSettingsSeeded() => SetMeta("settings_seeded", DateTime.UtcNow.ToString("O"));

    public IReadOnlyList<CatalogRow> LoadCatalog(string kind)
    {
        lock (_gate)
        {
            using var command = Command("SELECT kind, key, payload, enabled FROM catalog_entries WHERE kind = $kind ORDER BY key",
                ("$kind", kind));
            using var reader = command.ExecuteReader();
            var result = new List<CatalogRow>();
            while (reader.Read())
                result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0));
            return result;
        }
    }

    /// <summary>The keys of a kind's rows last written no later than <paramref name="since"/> (a round-trip UTC time):
    /// every edit rewrites a row's time, so these are the rows nobody changed after that moment.</summary>
    public IReadOnlySet<string> CatalogKeysWrittenBy(string kind, string since)
    {
        lock (_gate)
        {
            using var command = Command("SELECT key FROM catalog_entries WHERE kind = $kind AND updated_utc <= $since",
                ("$kind", kind), ("$since", since));
            using var reader = command.ExecuteReader();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read()) keys.Add(reader.GetString(0));
            return keys;
        }
    }

    public void PutCatalogRow(string kind, string key, string payload, bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("catalog kind is empty", nameof(kind));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("catalog key is empty", nameof(key));
        Atomically(() =>
        {
            using var command = Command("""
                INSERT INTO catalog_entries (kind, key, payload, enabled, updated_utc)
                VALUES ($kind, $key, $payload, $enabled, $utc)
                ON CONFLICT(kind, key) DO UPDATE SET
                    payload = excluded.payload,
                    enabled = excluded.enabled,
                    updated_utc = excluded.updated_utc
                """, ("$kind", kind), ("$key", key), ("$payload", payload), ("$enabled", enabled ? 1 : 0),
                ("$utc", DateTime.UtcNow.ToString("O")));
            command.ExecuteNonQuery();
        });
    }

    public void DeleteCatalogRow(string kind, string key)
    {
        Atomically(() =>
        {
            using var command = Command("DELETE FROM catalog_entries WHERE kind = $kind AND key = $key",
                ("$kind", kind), ("$key", key));
            command.ExecuteNonQuery();
        });
    }

    /// <summary>The seed keys last recorded for a kind. A missing key in the catalogue that is in this set was
    /// deleted by an admin, not omitted by an older seed.</summary>
    public IReadOnlySet<string> LoadSeededKeys(string kind)
    {
        lock (_gate)
        {
            using var command = Command("SELECT key FROM catalog_seed_keys WHERE kind = $kind", ("$kind", kind));
            using var reader = command.ExecuteReader();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read()) keys.Add(reader.GetString(0));
            return keys;
        }
    }

    public void ReplaceSeededKeys(string kind, IEnumerable<string> keys)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("catalog kind is empty", nameof(kind));
        Atomically(() =>
        {
            using (var delete = Command("DELETE FROM catalog_seed_keys WHERE kind = $kind", ("$kind", kind)))
                delete.ExecuteNonQuery();
            using var insert = Command("INSERT INTO catalog_seed_keys (kind, key) VALUES ($kind, $key)",
                ("$kind", kind), ("$key", ""));
            insert.Prepare();
            foreach (string key in keys)
            {
                insert.Parameters["$key"].Value = key;
                insert.ExecuteNonQuery();
            }
        });
    }

    public bool CatalogSeeded(string kind) => Meta("catalog_seeded:" + kind) is not null;

    public void MarkCatalogSeeded(string kind) => SetMeta("catalog_seeded:" + kind, DateTime.UtcNow.ToString("O"));

    public string? Meta(string key)
    {
        lock (_gate)
        {
            using var command = Command("SELECT value FROM game_data_meta WHERE key = $key", ("$key", key));
            return command.ExecuteScalar() as string;
        }
    }

    public void SetMeta(string key, string value)
    {
        Atomically(() =>
        {
            using var command = Command("""
                INSERT INTO game_data_meta (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """, ("$key", key), ("$value", value));
            command.ExecuteNonQuery();
        });
    }

    private void Atomically(Action work)
    {
        lock (_gate)
        {
            using var transaction = _db.BeginTransaction();
            _tx = transaction;
            try
            {
                work();
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            finally
            {
                _tx = null;
            }
        }
    }

    private SqliteCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var command = _db.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _tx;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private void Execute(string sql)
    {
        using var command = Command(sql);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_gate) _db.Dispose();
    }
}

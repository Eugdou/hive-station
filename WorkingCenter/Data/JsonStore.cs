using System.IO;
using System.Text.Json;
using WorkingCenter.Core;
using WorkingCenter.Models;

namespace WorkingCenter.Data;

/// <summary>JSON snapshot store (System.Text.Json — in-box for net10.0-windows,
/// so still zero extra NuGet). Reliability strategy (spec §七):
///  - Atomic write: serialize to hive-station.json.tmp, then File.Move(overwrite).
///    On NTFS same-volume move is atomic, so a crash mid-write can't corrupt the
///    previous good file.
///  - Write-before-backup: before each Save, copy the current good file to
///    hive-station.json.prev, so if the new write is somehow unusable we still
///    have the last good snapshot.
///  - Crash recovery on Load: if the main file fails to parse, fall back to
///    .prev, then backups from newest to oldest, then to seeded defaults.
///  - Schema versioning: older snapshots are migrated to the current schema
///    through a chain of migration functions before use.
///  - Periodic auto-backup: every N saves, also copy into data/backups/.
///
/// NOTE ON ENCRYPTION: this file is plain JSON on disk. It is NOT encrypted and
/// must not be described as such. Any confidentiality relies on the OS file
/// system permissions (or EFS/BitLocker), not on the store. SQLite would be the
/// same — a plain SQLite db is not "naturally encrypted".</summary>
internal sealed class JsonStore : IStore
{
    private readonly string _dir;
    private readonly string _backupsDir;
    private readonly string _path;       // hive-station.json  (current)
    private readonly string _prevPath;   // hive-station.json.prev (last good)
    private readonly string _tmp;        // hive-station.json.tmp (in-flight write)
    private int _savesSinceBackup;
    private bool _mainFileUsable;
    private const int AutoBackupEvery = 50;

    public string LoadNotice { get; private set; } = "";

    public JsonStore()
    {
        _dir = Path.Combine(AppContext.BaseDirectory, "data");
        _backupsDir = Path.Combine(_dir, "backups");
        _path = Path.Combine(_dir, "hive-station.json");
        _prevPath = _path + ".prev";
        _tmp = _path + ".tmp";
    }

    public AppData Load()
    {
        Directory.CreateDirectory(_dir);

        LoadNotice = "";
        _mainFileUsable = false;
        if (TryLoadFile(_path, out var data, out var err1))
        {
            _mainFileUsable = true;
            return Migrate(data);
        }
        Logger.Info($"JsonStore: main file unusable ({err1}); trying .prev");
        if (File.Exists(_prevPath) && TryLoadFile(_prevPath, out data, out _))
        {
            LoadNotice = "主数据文件无法读取，已从上一份快照恢复。";
            return Migrate(data);
        }
        Logger.Info("JsonStore: .prev unusable/missing; trying backups from newest to oldest");
        var backups = BackupCandidates();
        foreach (var backup in backups)
        {
            if (!TryLoadFile(backup, out data, out _)) continue;
            LoadNotice = $"主数据文件无法读取，已从备份 {Path.GetFileName(backup)} 恢复。";
            Logger.Info($"JsonStore: recovered from {Path.GetFileName(backup)}");
            return Migrate(data);
        }

        if (File.Exists(_path) || File.Exists(_prevPath) || backups.Length > 0)
            LoadNotice = "数据文件及现有备份均无法读取，当前显示默认数据。请检查备份后再继续编辑。";

        Logger.Info("JsonStore: no usable data, seeding defaults");
        return AppData.CreateDefault();
    }

    public void Save(AppData snapshot)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            snapshot.SchemaVersion = AppData.CurrentSchemaVersion;

            // 1. keep the last good file as .prev before overwriting
            if (_mainFileUsable && File.Exists(_path))
            {
                try { File.Copy(_path, _prevPath, overwrite: true); }
                catch { /* best-effort; the atomic move below still protects us */ }
            }

            // 2. write to a temp file, then atomically move into place
            var json = JsonSerializer.Serialize(snapshot, SnapshotJson.Options);
            File.WriteAllText(_tmp, json);
            File.Move(_tmp, _path, overwrite: true);   // atomic on NTFS same volume
            _mainFileUsable = true;

            // 3. periodic auto-backup
            if (++_savesSinceBackup >= AutoBackupEvery)
            {
                _savesSinceBackup = 0;
                AutoBackup();
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"JsonStore: save failed ({ex.Message})");
            throw;
        }
    }

    /// <summary>Copy the current file into data/backups/ with a timestamp.</summary>
    private void AutoBackup()
    {
        try
        {
            if (!File.Exists(_path)) return;
            Directory.CreateDirectory(_backupsDir);
            var name = "auto-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
            File.Copy(_path, Path.Combine(_backupsDir, name), overwrite: true);
            PruneBackups();
        }
        catch { /* best-effort */ }
    }

    /// <summary>Keep at most the 20 newest auto-backups.</summary>
    private void PruneBackups()
    {
        try
        {
            var files = Directory.GetFiles(_backupsDir, "auto-*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Skip(20);
            foreach (var f in files)
            {
                try { f.Delete(); } catch { }
            }
        }
        catch { }
    }

    private string[] BackupCandidates()
    {
        try
        {
            if (!Directory.Exists(_backupsDir)) return Array.Empty<string>();
            return Directory.GetFiles(_backupsDir, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .Select(f => f.FullName)
                .ToArray();
        }
        catch (Exception ex)
        {
            Logger.Info($"JsonStore: backup enumeration failed ({ex.Message})");
            return Array.Empty<string>();
        }
    }

    private static bool TryLoadFile(string path, out AppData data, out string error)
    {
        data = null!;
        error = "";
        try
        {
            if (!File.Exists(path)) { error = "not found"; return false; }
            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<AppData>(json, SnapshotJson.Options);
            if (parsed == null) { error = "empty/null"; return false; }
            parsed.Projects ??= new();
            parsed.Tasks ??= new();
            parsed.Reminders ??= new();
            parsed.Note ??= new QuickNote { Id = "main", Content = "", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            data = parsed;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>Bring an older-schema snapshot up to the current version. Each
    /// step upgrades from v -> v+1. Unknown future versions are left as-is
    /// (forward-compatible: extra fields are tolerated).</summary>
    private static AppData Migrate(AppData data)
    {
        // v0/missing -> v1/v2: nothing structural to change for v1/v2; the model
        // already had Detail/SortOrder/CompletedAt since stage 3. We just stamp the
        // version so future migrations have a starting point.
        if (data.SchemaVersion < AppData.CurrentSchemaVersion)
        {
            // Insert per-version migration calls here as the schema evolves, e.g.:
            //   if (data.SchemaVersion < 3) { ...add new field defaults...; data.SchemaVersion = 3; }
            data.SchemaVersion = AppData.CurrentSchemaVersion;
        }
        return data;
    }
}

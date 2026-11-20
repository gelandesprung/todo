using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DayTodo.Models;

namespace DayTodo.Services;

/// <summary>
/// Per-day JSON storage.
///
/// Layout (all under DataDir, which can be redirected with DAYTODO_DATA_DIR):
///   days/yyyy-MM-dd.json   one self-contained file per calendar day, keyed by DateCurrent
///   manifest.json          PC-owned index (date, file name, task count, SHA-256)
///   backups/yyyy-MM-dd/    rolling pre-change copies, 7 per day
///
/// The in-memory AppData is still a single object, so the window, the rollover engine
/// and the MCP tools are unchanged. Load merges every day file; Save groups by
/// DateCurrent and rewrites only the files whose content actually changed. That last
/// detail matters for cloud-sync clients: an unchanged save produces zero file writes
/// and therefore no spurious versions or conflict copies for historical days.
///
/// A day whose tasks have all moved away is kept as an empty file rather than deleted:
/// deletes are harder for sync clients to reason about, and the empty file still marks
/// that the day existed.
/// </summary>
public static class DataStore
{
    public const int SchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>DAYTODO_DATA_DIR can redirect storage, e.g. for isolated smoke tests.</summary>
    public static string DataDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("DAYTODO_DATA_DIR");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayTodo")
                : custom;
        }
    }

    public static string DaysDir => Path.Combine(DataDir, "days");

    public static string BackupDir => Path.Combine(DataDir, "backups");

    public static string ManifestPath => Path.Combine(DataDir, "manifest.json");

    /// <summary>The pre-1.0.4 single-file store, kept untouched as a migration source / rollback point.</summary>
    public static string LegacyFilePath => Path.Combine(DataDir, "tasks.json");

    /// <summary>File that holds today's tasks; this is what "open record location" points at.</summary>
    public static string TodayFilePath => PathFor(DateUtil.Today());

    private static string PathFor(string date) => Path.Combine(DaysDir, date + ".json");

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static void EnsureDirs()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(DaysDir);
        Directory.CreateDirectory(BackupDir);
    }

    /// <summary>yyyy-MM-dd.json only; stray files (editor temp files, .tmp, conflict copies) are ignored.</summary>
    private static bool TryParseDayFileName(string name, out string date)
    {
        date = string.Empty;
        if (name.Length != 15 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        var candidate = name.Substring(0, 10);
        if (!DateUtil.IsValid(candidate)) return false;
        date = candidate;
        return true;
    }

    private static IEnumerable<string> EnumerateDayFiles() =>
        Directory.EnumerateFiles(DaysDir, "*.json")
            .Where(p => TryParseDayFileName(Path.GetFileName(p), out _))
            .OrderBy(p => p, StringComparer.Ordinal);

    /// <summary>Used by the --verify smoke test to report how many day files exist.</summary>
    public static int DayFileCount() => EnumerateDayFiles().Count();

    private static void AtomicWrite(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }

    // ---------------- Load ----------------

    public static AppData Load()
    {
        try
        {
            EnsureDirs();
            MigrateLegacyIfNeeded();

            var data = new AppData();
            var seen = new HashSet<Guid>();
            var loadedAny = false;

            foreach (var path in EnumerateDayFiles())
            {
                TryParseDayFileName(Path.GetFileName(path), out var date);

                DayFile? day;
                try
                {
                    day = JsonSerializer.Deserialize<DayFile>(
                        File.ReadAllText(path, Encoding.UTF8), JsonOptions);
                    if (day == null) throw new JsonException("empty day file");
                }
                catch (Exception)
                {
                    // One bad day must never take the rest of the history down with it.
                    day = RecoverDay(date);
                    if (day == null) continue;
                }

                loadedAny = true;
                day.Tasks ??= new List<TaskItem>();
                foreach (var task in day.Tasks)
                {
                    if (task == null) continue;
                    // Trust the folder/file date when the payload is stale or malformed.
                    if (!DateUtil.IsValid(task.DateCurrent)) task.DateCurrent = date;
                    if (!seen.Add(task.Id)) continue;
                    data.Tasks.Add(task);
                }
            }

            // Only fall back to the legacy file when there is literally nothing else.
            // A legitimately empty store still has day files, so it never reaches here.
            if (!loadedAny)
            {
                var fallback = LoadLegacyFallback();
                if (fallback != null) data.Tasks.AddRange(fallback.Tasks);
            }

            Sanitize(data);
            return data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("DayTodo 读取按天数据失败：" + ex.Message);
            return LoadLegacyFallback() ?? new AppData();
        }
    }

    /// <summary>
    /// Quarantines one corrupt day file and restores that day from its newest backup.
    /// Returns null when no usable backup exists; the caller simply skips that day.
    /// </summary>
    private static DayFile? RecoverDay(string date)
    {
        var path = PathFor(date);
        try
        {
            if (File.Exists(path))
            {
                var corruptDir = Path.Combine(BackupDir, "corrupt");
                Directory.CreateDirectory(corruptDir);
                File.Move(path, Path.Combine(corruptDir,
                    date + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("DayTodo 隔离损坏文件失败：" + ex.Message);
        }

        try
        {
            var dir = Path.Combine(BackupDir, date);
            if (!Directory.Exists(dir)) return null;

            foreach (var backup in Directory.GetFiles(dir, "*.json")
                         .OrderByDescending(f => f, StringComparer.Ordinal))
            {
                try
                {
                    var recovered = JsonSerializer.Deserialize<DayFile>(
                        File.ReadAllText(backup, Encoding.UTF8), JsonOptions);
                    if (recovered == null) continue;
                    recovered.Date = date;
                    recovered.Tasks ??= new List<TaskItem>();
                    AtomicWrite(path, Serialize(recovered));
                    return recovered;
                }
                catch (Exception) { /* an older backup may still be readable */ }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("DayTodo 恢复单日备份失败：" + ex.Message);
        }

        return null;
    }

    private static void Sanitize(AppData data)
    {
        var today = DateUtil.Today();
        data.Tasks ??= new List<TaskItem>();
        data.Tasks.RemoveAll(t => t == null || string.IsNullOrWhiteSpace(t.Title));
        foreach (var t in data.Tasks)
        {
            if (t.Id == Guid.Empty) t.Id = Guid.NewGuid();
            if (!DateUtil.IsValid(t.DateOrigin)) t.DateOrigin = today;
            if (!DateUtil.IsValid(t.DateCurrent)) t.DateCurrent = t.DateOrigin;
            if (t.DateCompleted != null && !DateUtil.IsValid(t.DateCompleted)) t.DateCompleted = null;
            if (t.Priority < 0 || t.Priority > 2) t.Priority = 1;
            if (t.PostponeDays < 0) t.PostponeDays = 0;
            t.Tags ??= new List<string>();
        }
    }

    // ---------------- Legacy migration ----------------

    /// <summary>
    /// One-off upgrade from the old all-in-one tasks.json. The original file is kept
    /// verbatim as a rollback point; manifest.json marks the migration as done.
    /// </summary>
    private static void MigrateLegacyIfNeeded()
    {
        if (File.Exists(ManifestPath)) return;

        AppData? legacy = null;
        if (File.Exists(LegacyFilePath))
        {
            try
            {
                legacy = JsonSerializer.Deserialize<AppData>(
                    File.ReadAllText(LegacyFilePath, Encoding.UTF8), JsonOptions);
            }
            catch (Exception)
            {
                legacy = RestoreLatestLegacyBackup();
            }
        }

        if (legacy != null)
        {
            Sanitize(legacy);
            foreach (var group in legacy.Tasks.GroupBy(t => t.DateCurrent, StringComparer.Ordinal))
            {
                try
                {
                    AtomicWrite(PathFor(group.Key), Serialize(new DayFile
                    {
                        Date = group.Key,
                        Tasks = group.ToList()
                    }));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("DayTodo 迁移 " + group.Key + " 失败：" + ex.Message);
                }
            }
        }

        // Always leave a manifest, so a broken legacy file cannot be retried forever.
        try { RefreshManifest(); }
        catch (Exception ex) { Debug.WriteLine("DayTodo 生成 manifest 失败：" + ex.Message); }
    }

    private static AppData? RestoreLatestLegacyBackup()
    {
        try
        {
            var latest = Directory.GetFiles(BackupDir, "tasks-*.json")
                .OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (latest == null) return null;
            return JsonSerializer.Deserialize<AppData>(
                File.ReadAllText(latest, Encoding.UTF8), JsonOptions);
        }
        catch (Exception) { return null; }
    }

    private static AppData? LoadLegacyFallback()
    {
        try
        {
            if (File.Exists(LegacyFilePath))
            {
                var direct = JsonSerializer.Deserialize<AppData>(
                    File.ReadAllText(LegacyFilePath, Encoding.UTF8), JsonOptions);
                if (direct != null) { Sanitize(direct); return direct; }
            }
        }
        catch (Exception) { }

        var backup = RestoreLatestLegacyBackup();
        if (backup != null) Sanitize(backup);
        return backup;
    }

    // ---------------- Save ----------------

    public static void Save(AppData data)
    {
        EnsureDirs();
        Sanitize(data);

        var groups = data.Tasks
            .GroupBy(t => t.DateCurrent, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Days that already exist on disk must be written too, even when empty:
        // that is how a rolled-over or rescheduled task leaves its old file.
        foreach (var path in EnumerateDayFiles())
        {
            TryParseDayFileName(Path.GetFileName(path), out var date);
            if (!groups.ContainsKey(date)) groups[date] = new List<TaskItem>();
        }

        // A fresh install should still have a real "today" file to look at.
        var today = DateUtil.Today();
        if (!groups.ContainsKey(today)) groups[today] = new List<TaskItem>();

        var failures = new List<string>();
        foreach (var pair in groups.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            try
            {
                var json = Serialize(new DayFile { Date = pair.Key, Tasks = pair.Value });
                var path = PathFor(pair.Key);
                var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;

                // Dirty check: identical text means no write, no backup, no sync churn.
                if (string.Equals(existing, json, StringComparison.Ordinal)) continue;

                if (existing != null) BackupDay(pair.Key, existing);
                AtomicWrite(path, json);
            }
            catch (Exception ex)
            {
                failures.Add(pair.Key + ": " + ex.Message);
            }
        }

        try { RefreshManifest(); }
        catch (Exception ex) { failures.Add("manifest: " + ex.Message); }

        // Keep writing the other days even if one failed, but never fail silently:
        // the shell would otherwise show stale data while believing it was saved.
        if (failures.Count > 0)
            throw new IOException("保存任务失败：" + string.Join("; ", failures));
    }

    /// <summary>Saves the pre-change content of one day; keeps 7 copies per day.</summary>
    private static void BackupDay(string date, string content)
    {
        try
        {
            var dir = Path.Combine(BackupDir, date);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, date + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            if (File.Exists(path)) return;
            File.WriteAllText(path, content, new UTF8Encoding(false));

            var old = Directory.GetFiles(dir, "*.json")
                .OrderByDescending(f => f, StringComparer.Ordinal).Skip(7).ToArray();
            foreach (var f in old)
            {
                try { File.Delete(f); }
                catch (Exception) { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("DayTodo 备份 " + date + " 失败：" + ex.Message);
        }
    }

    /// <summary>
    /// Rebuilds the index from the actual bytes on disk. No timestamp field: the same
    /// content always serializes to the same text, so this is a no-op on unchanged saves.
    /// </summary>
    private static void RefreshManifest()
    {
        var manifest = new StoreManifest();

        foreach (var path in EnumerateDayFiles())
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var count = 0;
                using (var doc = JsonDocument.Parse(bytes))
                {
                    if (doc.RootElement.TryGetProperty("Tasks", out var array) &&
                        array.ValueKind == JsonValueKind.Array)
                        count = array.GetArrayLength();
                }

                manifest.Days.Add(new ManifestDay
                {
                    Date = Path.GetFileNameWithoutExtension(path),
                    File = Path.GetFileName(path),
                    Count = count,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("DayTodo 索引 " + Path.GetFileName(path) + " 失败：" + ex.Message);
            }
        }

        var json = Serialize(manifest);
        var existing = File.Exists(ManifestPath)
            ? File.ReadAllText(ManifestPath, Encoding.UTF8)
            : null;
        if (string.Equals(existing, json, StringComparison.Ordinal)) return;
        AtomicWrite(ManifestPath, json);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;
using DayTodo.Models;

namespace DayTodo.Services;

internal static class DateUtil
{
    public const string Format = "yyyy-MM-dd";

    public static string From(DateTime d) => d.ToString(Format, CultureInfo.InvariantCulture);

    public static string Today() => From(DateTime.Today);

    public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

    public static bool IsValid(string? s) => DateOnly.TryParseExact(s, Format,
        CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static int DaysBetween(string from, string to) =>
        DateOnly.ParseExact(to, Format, CultureInfo.InvariantCulture).DayNumber -
        DateOnly.ParseExact(from, Format, CultureInfo.InvariantCulture).DayNumber;

    public static string Short(string date)
    {
        var d = DateOnly.ParseExact(date, Format, CultureInfo.InvariantCulture);
        return $"{d.Month} 月 {d.Day} 日";
    }
}

public static class DataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayTodo");

    public static string DataFilePath => Path.Combine(DataDir, "tasks.json");

    private static string BackupDir => Path.Combine(DataDir, "backups");

    public static AppData Load()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            if (!File.Exists(DataFilePath)) return new AppData();

            var json = File.ReadAllText(DataFilePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions) ?? new AppData();
            Sanitize(data);
            return data;
        }
        catch (Exception)
        {
            QuarantineCorruptFile();
            return RestoreLatestBackup() ?? new AppData();
        }
    }

    private static void Sanitize(AppData data)
    {
        var today = DateUtil.Today();
        data.Tasks ??= new List<TaskItem>();
        data.Tasks.RemoveAll(t => string.IsNullOrWhiteSpace(t.Title));
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

    private static void QuarantineCorruptFile()
    {
        try
        {
            var bad = Path.Combine(DataDir, "tasks.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.Move(DataFilePath, bad, true);
        }
        catch (Exception) { }
    }

    private static AppData? RestoreLatestBackup()
    {
        try
        {
            if (!Directory.Exists(BackupDir)) return null;
            var latest = Directory.GetFiles(BackupDir, "tasks-*.json")
                .OrderByDescending(f => f).FirstOrDefault();
            if (latest == null) return null;
            var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(latest, Encoding.UTF8), JsonOptions);
            if (data == null) return null;
            Sanitize(data);
            return data;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Atomic replace + keep the 7 most recent backups.</summary>
    public static void Save(AppData data)
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupDir);

        var tmp = DataFilePath + ".tmp";
        var json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(tmp, json, new UTF8Encoding(false));

        if (File.Exists(DataFilePath))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            var backup = Path.Combine(BackupDir, "tasks-" + stamp + ".json");
            if (!File.Exists(backup))
            {
                try { File.Copy(DataFilePath, backup, true); }
                catch (Exception) { }
            }

            try
            {
                var old = Directory.GetFiles(BackupDir, "tasks-*.json")
                    .OrderByDescending(f => f).Skip(7).ToArray();
                foreach (var f in old) File.Delete(f);
            }
            catch (Exception) { }
        }

        File.Move(tmp, DataFilePath, true);
    }
}

public static class RolloverService
{
    /// <summary>
    /// Unfinished tasks scheduled before today are moved to today and accumulate
    /// carry-over days. Tasks planned in the future are left alone.
    /// </summary>
    public static int Run(AppData data, string today)
    {
        var changed = 0;
        foreach (var t in data.Tasks)
        {
            if (t.DateCompleted != null) continue;
            if (DateUtil.Compare(t.DateCurrent, today) >= 0) continue;

            var delta = DateUtil.DaysBetween(t.DateCurrent, today);
            if (delta <= 0) continue;

            t.PostponeDays += delta;
            t.DateCurrent = today;
            changed++;
        }
        data.LastRolloverDate = today;
        return changed;
    }
}

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DayTodo";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception) { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key == null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DayTodo.exe");
                key.SetValue(ValueName, "\"" + exe + "\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception) { }
    }
}

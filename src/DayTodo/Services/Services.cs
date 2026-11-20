using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

public static class RolloverService
{
    /// <summary>
    /// Unfinished tasks scheduled before today are moved to today and accumulate
    /// carry-over days. Tasks planned in the future are left alone.
    ///
    /// Idempotent: it only looks at DateCurrent, so the old "last rollover date"
    /// global field was redundant and is gone with the per-day storage layout.
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

/// <summary>Reschedules an unfinished task to today or a future day.</summary>
public static class TaskScheduleService
{
    /// <summary>
    /// A move is a reschedule: DateOrigin and DateCurrent both become the target day and
    /// accumulated carry-over days are cleared, so the task leaves its old day entirely.
    /// Past dates and completed tasks are rejected.
    /// </summary>
    public static bool Move(TaskItem task, string target, string today)
    {
        if (task.DateCompleted != null) return false;
        if (!DateUtil.IsValid(target)) return false;
        if (DateUtil.Compare(target, today) < 0) return false;
        if (task.DateOrigin == target && task.DateCurrent == target) return false;

        task.DateOrigin = target;
        task.DateCurrent = target;
        task.PostponeDays = 0;
        return true;
    }

    /// <summary>True when the task is scheduled on a day after today and can be pulled back.</summary>
    public static bool IsFuture(TaskItem task, string today) =>
        task.DateCompleted == null && DateUtil.Compare(task.DateOrigin, today) > 0;
}

/// <summary>Reveals the raw task data in Explorer.</summary>
public static class DataLocationService
{
    public static string BackupDir => DataStore.BackupDir;

    /// <summary>Selects today's yyyy-MM-dd.json, or opens the days folder if it is not there yet.</summary>
    public static void OpenDataLocation()
    {
        Directory.CreateDirectory(DataStore.DaysDir);
        var file = DataStore.TodayFilePath;
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = File.Exists(file)
                ? "/select,\"" + file + "\""
                : "\"" + DataStore.DaysDir + "\""
        };
        Process.Start(psi);
    }

    public static void OpenBackupLocation()
    {
        Directory.CreateDirectory(BackupDir);
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = "\"" + BackupDir + "\""
        };
        Process.Start(psi);
    }
}

using System;
using System.Collections.Generic;

namespace DayTodo.Models;

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    /// <summary>First day this task belonged to. Never changes.</summary>
    public string DateOrigin { get; set; } = string.Empty;

    /// <summary>Day it is currently scheduled on. Rolled forward to today for unfinished tasks.</summary>
    public string DateCurrent { get; set; } = string.Empty;

    /// <summary>Day it was completed. null means still open.</summary>
    public string? DateCompleted { get; set; }

    /// <summary>0 = low, 1 = normal, 2 = high.</summary>
    public int Priority { get; set; } = 1;

    public List<string> Tags { get; set; } = new();

    /// <summary>Total number of days this task has been carried over.</summary>
    public int PostponeDays { get; set; }
}

/// <summary>
/// The in-memory shape shared by the window and the MCP tools. It is never written
/// to disk as a whole: DataStore splits it into one file per day.
/// </summary>
public sealed class AppData
{
    public List<TaskItem> Tasks { get; set; } = new();
}

/// <summary>One calendar day = one self-contained file: days/yyyy-MM-dd.json.</summary>
public sealed class DayFile
{
    public int SchemaVersion { get; set; } = 2;

    /// <summary>yyyy-MM-dd; matches the file name.</summary>
    public string Date { get; set; } = string.Empty;

    public List<TaskItem> Tasks { get; set; } = new();
}

/// <summary>
/// PC-owned index of the day files. A future reader (web/mobile) can fetch this one
/// small file and use Sha256 to work out which days it still has to download.
/// It deliberately has no timestamp: identical content means an identical file, so
/// unchanged saves never touch it and cloud-sync clients see no spurious versions.
/// </summary>
public sealed class StoreManifest
{
    public int SchemaVersion { get; set; } = 2;

    public string Layout { get; set; } = "per-day";

    public List<ManifestDay> Days { get; set; } = new();
}

public sealed class ManifestDay
{
    public string Date { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;

    public int Count { get; set; }

    /// <summary>Lowercase hex SHA-256 of the raw file bytes.</summary>
    public string Sha256 { get; set; } = string.Empty;
}

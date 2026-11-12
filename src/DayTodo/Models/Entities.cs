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

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;

    public List<TaskItem> Tasks { get; set; } = new();

    public string LastRolloverDate { get; set; } = string.Empty;
}

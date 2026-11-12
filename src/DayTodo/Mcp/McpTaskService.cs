using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;
using DayTodo.Models;
using DayTodo.Services;

namespace DayTodo.Mcp;

/// <summary>
/// MCP tools now run inside the WPF process and operate on the very same AppData
/// instance as the window. No second copy of the store, no file lock, no watcher:
/// every mutation is marshalled onto the UI dispatcher and goes through the normal
/// Save + Rebuild path, so the list updates immediately and nothing can overwrite
/// anything.
/// </summary>
public sealed class McpTaskService
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly AppData _data;
    private readonly Dispatcher _dispatcher;
    private readonly Action _commit;

    public McpTaskService(AppData data, Dispatcher dispatcher, Action commit)
    {
        _data = data;
        _dispatcher = dispatcher;
        _commit = commit;
    }

    private static string Today() => DateTime.Today.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static bool IsValidDate(string? s) => DateOnly.TryParseExact(
        s, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static int Compare(string a, string b) => string.CompareOrdinal(a, b);

    /// <summary>All writes happen on the UI thread, exactly like a user clicking a button.</summary>
    private T Mutate<T>(Func<AppData, T> apply) => _dispatcher.Invoke(() =>
    {
        RolloverService.Run(_data, DateUtil.Today());
        var result = apply(_data);
        _commit();
        return result;
    });

    private List<TaskItem> Snapshot() => _dispatcher.Invoke(() => _data.Tasks.ToList());

    public TaskItem CreateTask(string? title, string? date, int priority, IEnumerable<string>? tags)
    {
        var text = (title ?? string.Empty).Trim();
        if (text.Length == 0) throw new ArgumentException("任务标题不能为空");
        if (text.Length > 500) throw new ArgumentException("任务标题过长（上限 500 字）");

        var day = string.IsNullOrWhiteSpace(date) ? Today() : date.Trim();
        if (!IsValidDate(day)) throw new ArgumentException("日期必须是 yyyy-MM-dd，例如 " + Today());
        if (Compare(day, Today()) < 0)
            throw new ArgumentException("不能给过去的日期创建任务，DayTodo 的历史日期是只读的");

        if (priority < 0 || priority > 2)
            throw new ArgumentException("priority 只能是 low / normal / high（或 0 / 1 / 2）");

        var cleaned = (tags ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();

        return Mutate(data =>
        {
            var item = new TaskItem
            {
                Title = text,
                DateOrigin = day,
                DateCurrent = day,
                Priority = priority,
                Tags = cleaned
            };
            data.Tasks.Add(item);
            return item;
        });
    }

    /// <summary>Same visibility rule as MainViewModel.AppearsOn.</summary>
    private static bool AppearsOn(TaskItem t, string sel, string today)
    {
        if (Compare(t.DateOrigin, sel) > 0) return false;
        if (t.DateCompleted != null) return Compare(t.DateCompleted, sel) >= 0;
        if (Compare(sel, today) <= 0) return true;
        return Compare(t.DateOrigin, sel) == 0;
    }

    public List<TaskItem> ListTasks(string? date, bool includeCompleted, bool all)
    {
        var today = Today();
        var sel = string.IsNullOrWhiteSpace(date) ? today : date.Trim();
        if (!IsValidDate(sel)) throw new ArgumentException("日期必须是 yyyy-MM-dd，例如 " + Today());

        return Snapshot()
            .Where(t => all || AppearsOn(t, sel, today))
            .Where(t => includeCompleted || t.DateCompleted == null)
            .OrderBy(t => t.DateCompleted != null ? 1 : 0)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DateOrigin, StringComparer.Ordinal)
            .ToList();
    }

    public TaskItem CompleteTask(string? id, string? title, bool undo) => Mutate(data =>
    {
        var match = FindOne(data, id, title);
        match.DateCompleted = undo ? null : Today();
        return match;
    });

    public TaskItem DeleteTask(string? id, string? title) => Mutate(data =>
    {
        var match = FindOne(data, id, title);
        data.Tasks.Remove(match);
        return match;
    });

    private static TaskItem FindOne(AppData data, string? id, string? title)
    {
        List<TaskItem> matches;
        if (!string.IsNullOrWhiteSpace(id))
        {
            if (!Guid.TryParse(id.Trim(), out var guid))
                throw new ArgumentException("id 不是有效的 GUID，可从 list_tasks 获取");
            matches = data.Tasks.Where(t => t.Id == guid).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            var key = title.Trim();
            matches = data.Tasks
                .Where(t => string.Equals(t.Title, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                matches = data.Tasks
                    .Where(t => t.Title.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else throw new ArgumentException("需要提供 id 或 title");

        if (matches.Count == 0) throw new ArgumentException("没有找到匹配的任务");
        if (matches.Count > 1)
            throw new ArgumentException($"匹配到 {matches.Count} 条任务，请先用 list_tasks 取得 id 再指定");
        return matches[0];
    }

    public object GetOverview()
    {
        var today = Today();
        var tasks = Snapshot().Where(t => AppearsOn(t, today, today)).ToList();
        return new
        {
            date = today,
            total = tasks.Count,
            completed = tasks.Count(t => t.DateCompleted != null),
            open = tasks.Count(t => t.DateCompleted == null),
            carriedOver = tasks.Count(t => t.DateCompleted == null && t.PostponeDays > 0)
        };
    }
}

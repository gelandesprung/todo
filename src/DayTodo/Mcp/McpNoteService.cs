using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;
using DayTodo.Models;
using DayTodo.Services;

namespace DayTodo.Mcp;

/// <summary>A reference requested by an MCP client. Resolved to a real note before saving.</summary>
public sealed record NoteRefInput(string Id, string? Date = null, string? Text = null);

/// <summary>
/// MCP-side note operations. Like McpTaskService this runs inside the WPF process on
/// the UI dispatcher and mutates the very same AppData the window displays, so an
/// agent edit shows up immediately and goes through the normal Save path.
///
/// Notes are day-bound: writes are accepted for today only. Past notes are immutable
/// through every entry point, including MCP.
/// </summary>
public sealed class McpNoteService
{
    private const string DateFormat = "yyyy-MM-dd";
    private const int MaxTextLength = 5000;
    private const int MaxLimit = 500;

    private readonly AppData _data;
    private readonly Dispatcher _dispatcher;
    private readonly Action _commit;

    public McpNoteService(AppData data, Dispatcher dispatcher, Action commit)
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

    private T Read<T>(Func<AppData, T> read) => _dispatcher.Invoke(() => read(_data));

    private static string CleanText(string? text, string label)
    {
        var clean = (text ?? string.Empty).Trim();
        if (clean.Length == 0) throw new ArgumentException(label + "不能为空");
        if (clean.Length > MaxTextLength)
            throw new ArgumentException($"{label}过长（上限 {MaxTextLength} 字）");
        return clean;
    }

    private static int CheckLimit(int limit)
    {
        if (limit < 1 || limit > MaxLimit)
            throw new ArgumentException($"limit 必须在 1 到 {MaxLimit} 之间");
        return limit;
    }

    private static string Snapshot(string text)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 60 ? oneLine : oneLine.Substring(0, 60) + "…";
    }

    /// <summary>Resolve client-supplied ids against real notes; never store dangling links.</summary>
    private static List<NoteReference> ResolveRefs(AppData data, IReadOnlyList<NoteRefInput>? inputs)
    {
        var result = new List<NoteReference>();
        if (inputs == null) return result;

        foreach (var input in inputs)
        {
            var raw = input?.Id?.Trim();
            if (!Guid.TryParse(raw, out var guid))
                throw new ArgumentException("引用的 note_id 不是有效的 GUID：" + (raw ?? "(空)"));

            var source = data.Notes.FirstOrDefault(n => n.Id == guid);
            if (source == null)
                throw new ArgumentException("找不到被引用的随记：" + guid + "，可先用 list_notes 获取 id");

            if (!string.IsNullOrWhiteSpace(input!.Date) && input.Date.Trim() != source.Date)
                throw new ArgumentException($"引用 {guid} 的日期应为 {source.Date}，收到 {input.Date}");

            if (result.Any(r => r.NoteId == guid && r.Date == source.Date)) continue;

            var snapshot = string.IsNullOrWhiteSpace(input.Text)
                ? Snapshot(source.Text)
                : input.Text.Trim();
            result.Add(new NoteReference
            {
                Date = source.Date,
                NoteId = guid,
                Text = snapshot
            });
        }
        return result;
    }

    public NoteItem CreateNote(string? text, IReadOnlyList<NoteRefInput>? references)
    {
        var clean = CleanText(text, "随记内容");
        return Mutate(data =>
        {
            var item = new NoteItem
            {
                Date = Today(),
                CreatedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Text = clean,
                References = ResolveRefs(data, references)
            };
            data.Notes.Add(item);
            return item;
        });
    }

    public List<NoteItem> ListNotes(string? date, bool all, int limit)
    {
        var today = Today();
        var sel = string.IsNullOrWhiteSpace(date) ? today : date.Trim();
        if (!IsValidDate(sel)) throw new ArgumentException("日期必须是 yyyy-MM-dd，例如 " + today);
        var take = CheckLimit(limit);

        return Read(data => data.Notes
            .Where(n => all || n.Date == sel)
            .OrderBy(n => n.Date, StringComparer.Ordinal)
            .ThenBy(n => n.CreatedAt, StringComparer.Ordinal)
            .Take(take)
            .ToList());
    }

    /// <summary>Case-insensitive substring search over note text and reference snapshots.</summary>
    public List<NoteItem> SearchNotes(string? query, string? from, string? to, int limit)
    {
        var needle = CleanText(query, "搜索关键词");
        var take = CheckLimit(limit);

        if (!string.IsNullOrWhiteSpace(from) && !IsValidDate(from.Trim()))
            throw new ArgumentException("from 必须是 yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(to) && !IsValidDate(to.Trim()))
            throw new ArgumentException("to 必须是 yyyy-MM-dd");
        var fromDate = string.IsNullOrWhiteSpace(from) ? null : from.Trim();
        var toDate = string.IsNullOrWhiteSpace(to) ? null : to.Trim();
        if (fromDate != null && toDate != null && Compare(fromDate, toDate) > 0)
            throw new ArgumentException("from 不能晚于 to");

        return Read(data => data.Notes
            .Where(n => fromDate == null || Compare(n.Date, fromDate) >= 0)
            .Where(n => toDate == null || Compare(n.Date, toDate) <= 0)
            .Where(n => n.Text.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                        n.References.Any(r => r.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.Date, StringComparer.Ordinal)
            .ThenByDescending(n => n.CreatedAt, StringComparer.Ordinal)
            .Take(take)
            .ToList());
    }

    public NoteItem UpdateNote(string? id, string? text, IReadOnlyList<NoteRefInput>? references,
        bool clearReferences)
    {
        var rawId = id?.Trim();
        if (!Guid.TryParse(rawId, out var guid))
            throw new ArgumentException("note_id 不是有效的 GUID，可先用 list_notes 获取");

        string? clean = null;
        if (text != null) clean = CleanText(text, "随记内容");
        if (clean == null && references == null && !clearReferences)
            throw new ArgumentException("至少需要 text、references 或 clear_references 之一");

        return Mutate(data =>
        {
            var note = data.Notes.FirstOrDefault(n => n.Id == guid);
            if (note == null) throw new ArgumentException("找不到随记：" + guid);
            if (note.Date != Today())
                throw new ArgumentException("历史随记只读，不能修改 " + note.Date + " 的随记");

            if (clean != null) note.Text = clean;
            if (clearReferences) note.References = new List<NoteReference>();
            else if (references != null) note.References = ResolveRefs(data, references);
            return note;
        });
    }

    public NoteItem DeleteNote(string? id)
    {
        var rawId = id?.Trim();
        if (!Guid.TryParse(rawId, out var guid))
            throw new ArgumentException("note_id 不是有效的 GUID，可先用 list_notes 获取");

        return Mutate(data =>
        {
            var note = data.Notes.FirstOrDefault(n => n.Id == guid);
            if (note == null) throw new ArgumentException("找不到随记：" + guid);
            if (note.Date != Today())
                throw new ArgumentException("历史随记只读，不能删除 " + note.Date + " 的随记");
            data.Notes.Remove(note);
            return note;
        });
    }

    /// <summary>Resolve quote text at read time; fall back to the stored snapshot.</summary>
    public static string ResolveQuote(AppData data, NoteReference quote)
    {
        var source = data.Notes.FirstOrDefault(n => n.Id == quote.NoteId && n.Date == quote.Date);
        return source != null ? Snapshot(source.Text) : quote.Text;
    }

    /// <summary>Live title of a mentioned task; the stored snapshot covers deletions.</summary>
    private static string ResolveTaskTitle(AppData data, NoteTaskReference taskRef)
    {
        var task = data.Tasks.FirstOrDefault(t => t.Id == taskRef.TaskId);
        return task != null && task.Title.Length > 0 ? task.Title : taskRef.Title;
    }

    /// <summary>Serializable view for MCP replies. Quote text is resolved on the UI thread.</summary>
    public object Describe(NoteItem note) => _dispatcher.Invoke(() => DescribeCore(note));

    public List<object> Describe(IEnumerable<NoteItem> notes) =>
        _dispatcher.Invoke(() => notes.Select(n => (object)DescribeCore(n)).ToList());

    private object DescribeCore(NoteItem note) => new
    {
        id = note.Id.ToString(),
        date = note.Date,
        time = note.CreatedAt,
        text = note.Text,
        references = note.References.Select(r => new
        {
            date = r.Date,
            note_id = r.NoteId.ToString(),
            text = ResolveQuote(_data, r)
        }).ToList(),
        task_references = (note.TaskRefs ?? new List<NoteTaskReference>()).Select(r => new
        {
            date = r.Date,
            task_id = r.TaskId.ToString(),
            title = ResolveTaskTitle(_data, r)
        }).ToList()
    };
}

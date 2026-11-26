using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DayTodo.Models;
using DayTodo.Services;

namespace DayTodo.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}

public sealed class MainViewModel : ObservableObject
{
    private static readonly string[] WeekNames =
        { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

    private readonly AppData _data;
    private readonly Action<string>? _notify;
    private readonly Func<DateTime, DateTime?>? _pickDate;
    private readonly Action? _focusEditor;
    private readonly Action? _focusNoteEditor;
    private DateTime _selectedDate = DateTime.Today;
    private string _newTitle = string.Empty;
    private string _newTags = string.Empty;
    private int _newPriority = 1;
    private int _doneCount;
    private int _totalCount;
    private int _carryCount;
    private int _streakDays;
    private double _progress;
    private bool _viewingToday = true;
    private DateTime? _nextFutureDate;
    private int _futureCount;
    private bool _showNotes;
    private string _newNoteText = string.Empty;
    private NoteItem? _editingNote;
    private int _todayNoteCount;

    /// <summary>The task whose "read in full" card is open, or null when it is closed.</summary>
    private TaskRowViewModel? _detailRow;

    /// <summary>Quotes waiting to be attached to the note being written or edited.</summary>
    private readonly List<NoteReference> _pendingQuotes = new();

    /// <summary>Todos the note being written or edited mentions with @.</summary>
    private readonly List<NoteTaskReference> _pendingTaskRefs = new();

    /// <summary>The plan that was recalled and is waiting in the editor to be sent again.</summary>
    private TaskItem? _draft;

    /// <summary>True while the editor holds a recalled plan.</summary>
    private bool _draftPending;

    // ---- 搜索（临时覆盖视图，不改 SelectedDate、不写数据） ----
    private string _searchText = string.Empty;
    private bool _isSearching;
    private int _searchFilter;
    private SearchHit? _currentSearchHit;

    public ObservableCollection<TaskRowViewModel> Rows { get; } = new();
    public ObservableCollection<NoteRowViewModel> Notes { get; } = new();

    public ICommand PrevDayCommand { get; }
    public ICommand NextDayCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand SetPriorityCommand { get; }
    public ICommand GoToFutureCommand { get; }
    public ICommand ShowTasksCommand { get; }
    public ICommand ShowNotesCommand { get; }
    public ICommand SaveNoteCommand { get; }
    public ICommand CancelNoteEditCommand { get; }
    public ICommand ClearQuotesCommand { get; }
    public ICommand OpenSearchCommand { get; }
    public ICommand CloseSearchCommand { get; }
    public ICommand SetSearchFilterCommand { get; }
    public ICommand OpenSearchHitCommand { get; }

    /// <summary>Raised after a hit was jumped to, so the shell can scroll and flash it.</summary>
    public event Action<SearchHit>? SearchHitOpened;

    public MainViewModel(AppData data, Action<string>? notify = null,
        Func<DateTime, DateTime?>? pickDate = null, Action? focusEditor = null,
        Action? focusNoteEditor = null)
    {
        _data = data;
        _notify = notify;
        _pickDate = pickDate;
        _focusEditor = focusEditor;
        _focusNoteEditor = focusNoteEditor;

        PrevDayCommand = new RelayCommand(_ => MoveDay(-1));
        NextDayCommand = new RelayCommand(_ => MoveDay(1));
        TodayCommand = new RelayCommand(_ => SetSelectedDate(DateTime.Today));
        AddCommand = new RelayCommand(_ => AddTask());
        SetPriorityCommand = new RelayCommand(p => SetPriority(p));
        GoToFutureCommand = new RelayCommand(_ => GoToFuture());
        ShowTasksCommand = new RelayCommand(_ => ShowTab(false));
        ShowNotesCommand = new RelayCommand(_ => ShowTab(true));
        SaveNoteCommand = new RelayCommand(_ => SaveNote());
        CancelNoteEditCommand = new RelayCommand(_ => CancelNoteEdit());
        ClearQuotesCommand = new RelayCommand(_ => ClearQuotes());
        OpenSearchCommand = new RelayCommand(_ => OpenSearch());
        CloseSearchCommand = new RelayCommand(_ => CloseSearch());
        SetSearchFilterCommand = new RelayCommand(p => SetSearchFilter(Convert.ToInt32(p)));
        OpenSearchHitCommand = new RelayCommand(p => OpenSearchHit(p as SearchHit));

        Rebuild();
    }

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set => SetSelectedDate(value);
    }

    private void SetSelectedDate(DateTime value)
    {
        if (!Set(ref _selectedDate, value.Date)) return;
        OnPropertyChanged(nameof(SelectedDate));
        _viewingToday = value.Date == DateTime.Today;

        // A draft belongs to one day only. Leaving that day discards it instead of
        // letting a note written yesterday land on today by accident.
        CancelNoteEdit();
        Rebuild();
    }

    public string SelectedDateText => $"{_selectedDate.Month} 月 {_selectedDate.Day} 日";

    public string WeekdayText => WeekNames[(int)_selectedDate.DayOfWeek];

    public bool IsToday => DateUtil.Compare(Sel(), DateUtil.Today()) == 0;
    public bool IsFuture => DateUtil.Compare(Sel(), DateUtil.Today()) > 0;
    public bool IsHistory => DateUtil.Compare(Sel(), DateUtil.Today()) < 0;

    public bool CanEdit => !IsHistory;

    // ---- 随记 ----

    public bool IsTasksTab => !_showNotes;
    public bool IsNotesTab => _showNotes;
    public bool ShowTaskProgress => !_showNotes;

    /// <summary>Notes are writable only on the real today, never on a browsed day.</summary>
    public bool CanWriteNote => IsToday;

    public bool HasNoNotes => Notes.Count == 0;

    public bool NoteReadOnlyVisible => !CanWriteNote;

    public int TodayNoteCount
    {
        get => _todayNoteCount;
        private set => Set(ref _todayNoteCount, value);
    }

    public string NewNoteText
    {
        get => _newNoteText;
        set
        {
            if (!Set(ref _newNoteText, value)) return;
            OnPropertyChanged(nameof(CanSaveNote));
            OnPropertyChanged(nameof(NoteHintVisible));
        }
    }

    public bool CanSaveNote => CanWriteNote && !string.IsNullOrWhiteSpace(_newNoteText);

    public bool NoteHintVisible => string.IsNullOrEmpty(_newNoteText);

    public bool IsEditingNote => _editingNote != null;

    public string NoteEditorHintText => _editingNote == null
        ? string.Empty
        : "正在编辑 " + (_editingNote.CreatedAt.Length >= 5
            ? _editingNote.CreatedAt.Substring(0, 5) : _editingNote.CreatedAt) + " 的随记";

    public bool HasPendingQuotes => _pendingQuotes.Count > 0;

    public string PendingQuoteText => _pendingQuotes.Count == 0
        ? string.Empty
        : "引用 " + _pendingQuotes.Count + " 条 · " + string.Join("、",
            _pendingQuotes.Select(q => ShortDate(q.Date)));

    public string NoteSummaryText => IsToday
        ? $"{WeekdayText} · {Notes.Count} 条随记 · 只有今天可以新增"
        : IsHistory
            ? $"{WeekdayText} · {Notes.Count} 条随记 · 历史只读"
            : $"{WeekdayText} · 未来没有随记";

    public string NoteReadOnlyText => IsHistory
        ? "这一天的随记已封存，只能查看和引用"
        : "未来的随记还不能写，回到今天再记";

    public string HeaderSummaryText => _showNotes ? NoteSummaryText : SummaryText;

    /// <summary>Runtime state of the MCP service, shown under the sidebar switch.</summary>
    public string McpStatusText
    {
        get => _mcpStatusText;
        set => Set(ref _mcpStatusText, value);
    }

    private string _mcpStatusText = "已停止";

    /// <summary>Called by the shell whenever the MCP service state changes.</summary>
    public void SetMcpStatus(string text) => McpStatusText = text;

    public string NewTitle
    {
        get => _newTitle;
        set
        {
            if (!Set(ref _newTitle, value)) return;
            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(TitleHintVisible));

            // Clearing the box by hand abandons the recalled draft: there is
            // nothing left to send again, so the hint must go away as well.
            if (string.IsNullOrWhiteSpace(value) && _draftPending) ApplyDraft(null);
        }
    }

    /// <summary>True while the editor holds a recalled plan waiting to be sent again.</summary>
    public bool HasRecalledDraft => _draftPending;

    /// <summary>Explains the editor content: it came from a recall, not from a new plan.</summary>
    public string RecalledHintText => _draftPending && _draft != null
        ? "已撤回「" + _draft.Title + "」，可修改后重新发送"
        : string.Empty;

    public string NewTags
    {
        get => _newTags;
        set => Set(ref _newTags, value);
    }

    public bool CanAdd => CanEdit && !string.IsNullOrWhiteSpace(_newTitle);

    public bool TitleHintVisible => string.IsNullOrEmpty(_newTitle);

    public bool HasNoTasks => _totalCount == 0;

    /// <summary>Tasks scheduled on a future day (hidden from today's list).</summary>
    public bool HasFutureTasks => _futureCount > 0;
    public int FutureCount => _futureCount;

    public int NewPriority
    {
        get => _newPriority;
        set
        {
            if (!Set(ref _newPriority, value)) return;
            OnPropertyChanged(nameof(HighRing));
            OnPropertyChanged(nameof(NormalRing));
            OnPropertyChanged(nameof(LowRing));
        }
    }

    public Brush HighRing => Ring(2, "#E24B4A");
    public Brush NormalRing => Ring(1, "#EF9F27");
    public Brush LowRing => Ring(0, "#C9C7BE");

    private Brush Ring(int level, string hex) =>
        _newPriority == level ? MakeBrush(hex) : Brushes.Transparent;

    public int DoneCount { get => _doneCount; private set => Set(ref _doneCount, value); }
    public int TotalCount { get => _totalCount; private set => Set(ref _totalCount, value); }
    public int CarryCount { get => _carryCount; private set => Set(ref _carryCount, value); }
    public int StreakDays { get => _streakDays; private set => Set(ref _streakDays, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public string SummaryText
    {
        get
        {
            var baseText = $"{WeekdayText} · {_totalCount} 项任务";
            if (IsToday) return $"{baseText} · {_carryCount} 项由前日顺延";
            if (IsHistory) return $"{baseText} · 历史记录，只读";
            return $"{baseText} · 提前安排";
        }
    }

    private string Sel() => DateUtil.From(_selectedDate);

    private static string ShortDate(string date)
    {
        if (!DateUtil.IsValid(date)) return date;
        var d = DateOnly.ParseExact(date, DateUtil.Format, CultureInfo.InvariantCulture);
        return d.Month + "/" + d.Day;
    }

    private void ShowTab(bool notes)
    {
        if (_showNotes == notes) return;
        _showNotes = notes;
        CloseTaskDetail();
        OnPropertyChanged(nameof(IsTasksTab));
        OnPropertyChanged(nameof(IsNotesTab));
        OnPropertyChanged(nameof(ShowTaskProgress));
        OnPropertyChanged(nameof(HeaderSummaryText));
    }

    private void MoveDay(int delta) => SetSelectedDate(_selectedDate.AddDays(delta));

    private void GoToFuture()
    {
        if (_nextFutureDate != null) SetSelectedDate(_nextFutureDate.Value);
    }

    private void SetPriority(object? parameter)
    {
        if (parameter == null) return;
        if (!int.TryParse(parameter.ToString(), out var p)) return;
        NewPriority = Math.Clamp(p, 0, 2);
    }

    private static bool AppearsOn(TaskItem t, string sel, string today)
    {
        if (DateUtil.Compare(t.DateOrigin, sel) > 0) return false;

        if (t.DateCompleted != null)
            return DateUtil.Compare(t.DateCompleted, sel) >= 0;

        if (DateUtil.Compare(sel, today) <= 0) return true;

        return DateUtil.Compare(t.DateOrigin, sel) == 0;
    }

    private void AddTask()
    {
        if (!CanEdit) return;
        var title = _newTitle.Trim();
        if (title.Length == 0) return;

        var tags = _newTags
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();

        var sel = Sel();
        _data.Tasks.Add(new TaskItem
        {
            Title = title,
            DateOrigin = sel,
            DateCurrent = sel,
            Priority = _newPriority,
            Tags = tags
        });

        NewTitle = string.Empty;
        NewTags = string.Empty;

        // A recalled plan that gets sent again is a plan again: the draft slot is free.
        ApplyDraft(null);

        Commit();
    }

    // ---- 随记操作 ----

    private void SaveNote()
    {
        if (!CanWriteNote) return;
        var text = _newNoteText.Trim();
        if (text.Length == 0) return;

        PruneTaskRefs(text);

        if (_editingNote != null)
        {
            // Editing keeps identity, day and timestamp: a note never moves days.
            _editingNote.Text = text;
            _editingNote.References = _pendingQuotes.ToList();
            _editingNote.TaskRefs = _pendingTaskRefs.ToList();
        }
        else
        {
            _data.Notes.Add(new NoteItem
            {
                Date = DateUtil.Today(),
                CreatedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                Text = text,
                References = _pendingQuotes.ToList(),
                TaskRefs = _pendingTaskRefs.ToList()
            });
        }

        CancelNoteEdit();
        Commit();
    }

    private void EditNote(NoteItem note)
    {
        if (!CanWriteNote || note.Date != DateUtil.Today()) return;

        _editingNote = note;
        _newNoteText = note.Text;
        _pendingQuotes.Clear();
        _pendingQuotes.AddRange(note.References ?? new List<NoteReference>());
        _pendingTaskRefs.Clear();
        _pendingTaskRefs.AddRange(note.TaskRefs ?? new List<NoteTaskReference>());

        OnPropertyChanged(nameof(NewNoteText));
        OnPropertyChanged(nameof(IsEditingNote));
        OnPropertyChanged(nameof(NoteEditorHintText));
        OnPropertyChanged(nameof(CanSaveNote));
        OnPropertyChanged(nameof(NoteHintVisible));
        OnPropertyChanged(nameof(PendingQuoteText));
        OnPropertyChanged(nameof(HasPendingQuotes));
        _focusNoteEditor?.Invoke();
    }

    private void DeleteNote(NoteItem note)
    {
        if (!CanWriteNote || note.Date != DateUtil.Today()) return;
        _data.Notes.Remove(note);
        if (_editingNote == note) CancelNoteEdit();
        Commit();
    }

    /// <summary>
    /// Quotes a note from any day. The target day is switched first so the editor
    /// stays alive, then the quote is attached to today's draft.
    /// </summary>
    private void QuoteNote(NoteItem note)
    {
        if (!ShowTabAndGoToday()) return;
        if (_pendingQuotes.Any(q => q.NoteId == note.Id && q.Date == note.Date))
        {
            _notify?.Invoke("这条随记已经在引用里了");
            return;
        }

        _pendingQuotes.Add(new NoteReference
        {
            Date = note.Date,
            NoteId = note.Id,
            Text = QuoteSnapshot(note.Text)
        });

        OnPropertyChanged(nameof(PendingQuoteText));
        OnPropertyChanged(nameof(HasPendingQuotes));
        _focusNoteEditor?.Invoke();
    }

    /// <summary>Clicking a quote chip jumps to the quoted day with its note tab open.</summary>
    private void OpenQuote(string date)
    {
        if (!DateUtil.IsValid(date)) return;
        ShowTab(true);
        SetSelectedDate(DateTime.ParseExact(date, DateUtil.Format, CultureInfo.InvariantCulture));
    }

    private bool ShowTabAndGoToday()
    {
        ShowTab(true);
        if (!IsToday) SetSelectedDate(DateTime.Today);
        return CanWriteNote;
    }

    private void ClearQuotes()
    {
        _pendingQuotes.Clear();
        OnPropertyChanged(nameof(PendingQuoteText));
        OnPropertyChanged(nameof(HasPendingQuotes));
    }

    private void CancelNoteEdit()
    {
        _editingNote = null;
        _newNoteText = string.Empty;
        _pendingQuotes.Clear();
        _pendingTaskRefs.Clear();
        OnPropertyChanged(nameof(NewNoteText));
        OnPropertyChanged(nameof(IsEditingNote));
        OnPropertyChanged(nameof(NoteEditorHintText));
        OnPropertyChanged(nameof(CanSaveNote));
        OnPropertyChanged(nameof(NoteHintVisible));
        OnPropertyChanged(nameof(PendingQuoteText));
        OnPropertyChanged(nameof(HasPendingQuotes));
    }

    private static string QuoteSnapshot(string text)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length <= 60 ? oneLine : oneLine.Substring(0, 60) + "…";
    }

    internal static string ResolveQuote(AppData data, NoteReference quote)
    {
        var source = data.Notes.FirstOrDefault(n => n.Id == quote.NoteId && n.Date == quote.Date);
        return source != null ? QuoteSnapshot(source.Text) : quote.Text;
    }

    // ---- 随记里的 @ ----

    private const int MentionLimit = 8;

    /// <summary>
    /// Todos that may be mentioned: whatever sits on today or yesterday. The window is
    /// deliberately that narrow - pointing a note at a plan from last month links to
    /// something nobody can act on - and it keeps the popup short enough to pick from
    /// without scrolling.
    /// </summary>
    public IReadOnlyList<NoteMentionItem> FindMentionCandidates(string? query)
    {
        var today = DateUtil.Today();
        var yesterday = DateUtil.From(DateTime.Today.AddDays(-1));
        var needle = (query ?? string.Empty).Trim();

        return _data.Tasks
            .Where(t => AppearsOn(t, today, today) || AppearsOn(t, yesterday, today))
            .Where(t => needle.Length == 0 ||
                        t.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .Select(t => new NoteMentionItem(t.Id,
                AppearsOn(t, today, today) ? today : yesterday,
                t.Title, t.DateCompleted != null))
            .OrderByDescending(m => m.Date == today)
            .ThenBy(m => m.IsDone)
            .ThenBy(m => m.Title, StringComparer.Ordinal)
            .Take(MentionLimit)
            .ToList();
    }

    /// <summary>
    /// Records a mention picked from the popup. Picking the same todo twice keeps one
    /// link: the note text may carry two @titles, but both point at the same task.
    /// </summary>
    public void AddTaskRef(NoteMentionItem? item)
    {
        if (item == null) return;
        _pendingTaskRefs.RemoveAll(r => r.TaskId == item.TaskId);
        _pendingTaskRefs.Add(new NoteTaskReference
        {
            Date = item.Date,
            TaskId = item.TaskId,
            Title = item.Title
        });
    }

    /// <summary>
    /// Drops mentions whose @text is no longer in the note. Deleting the mention is how
    /// the user removes the link, so the chips can never claim more than the text says.
    /// </summary>
    private void PruneTaskRefs(string text)
    {
        _pendingTaskRefs.RemoveAll(r => r.Title.Length == 0 ||
                                        !text.Contains("@" + r.Title, StringComparison.Ordinal));
    }

    /// <summary>Clicking an @chip jumps to the day the todo is actually sitting on now.</summary>
    private void OpenTaskRef(NoteTaskReference taskRef)
    {
        var task = _data.Tasks.FirstOrDefault(t => t.Id == taskRef.TaskId);
        var date = task != null ? task.DateCurrent : taskRef.Date;

        ShowTab(false);
        if (!DateUtil.IsValid(date)) return;
        SetSelectedDate(DateTime.ParseExact(date, DateUtil.Format, CultureInfo.InvariantCulture));
    }

    /// <summary>Title and day label for a chip: live task data first, snapshot as fallback.</summary>
    private (string Text, string Day) DescribeTaskRef(NoteTaskReference taskRef)
    {
        var task = _data.Tasks.FirstOrDefault(t => t.Id == taskRef.TaskId);
        var title = task != null && task.Title.Length > 0 ? task.Title : taskRef.Title;
        var date = task != null ? task.DateCurrent : taskRef.Date;
        return (title, DayLabel(date));
    }

    private static string DayLabel(string date)
    {
        if (!DateUtil.IsValid(date)) return date;
        if (date == DateUtil.Today()) return "今天";
        if (date == DateUtil.From(DateTime.Today.AddDays(-1))) return "昨天";
        return ShortDate(date);
    }

    /// <summary>Public helpers used by the --verify smoke test.</summary>
    public void SaveNoteForVerify(string text)
    {
        NewNoteText = text;
        SaveNote();
    }

    public void QuoteNoteForVerify(NoteItem note) => QuoteNote(note);

    /// <summary>Seeds a note on an arbitrary day for the read-only smoke test.</summary>
    public void SeedNoteForVerify(string date, string text)
    {
        _data.Notes.Add(new NoteItem
        {
            Date = date,
            CreatedAt = "09:00:00",
            Text = text
        });
        Commit();
    }

    /// <summary>
    /// Takes a plan back out of the list and drops its content into the editor.
    ///
    /// The editor holds one draft at a time: recalling another plan replaces the
    /// draft, so after several recalls in a row only the last one can be edited
    /// and sent again. The plans recalled earlier are gone from the list - that is
    /// what the user asked for by clicking 撤回, and there is no draft pile-up
    /// to sort through either.
    /// </summary>
    private bool Recall(TaskRowViewModel row)
    {
        if (!CanEdit) return false;
        var t = row.Model;

        _data.Tasks.Remove(t);

        NewTitle = t.Title;
        NewTags = string.Join(", ", t.Tags);
        NewPriority = t.Priority;
        ApplyDraft(t);

        Commit();

        _focusEditor?.Invoke();
        _notify?.Invoke("已撤回「" + t.Title + "」，可在下方修改后重新发送");
        return true;
    }

    /// <summary>Public entry point used by the --verify smoke test.</summary>
    public bool RecallRow(TaskRowViewModel row) => Recall(row);

    /// <summary>Holds (or releases) the single recalled plan waiting in the editor.</summary>
    private void ApplyDraft(TaskItem? draft)
    {
        _draft = draft;
        _draftPending = draft != null;
        OnPropertyChanged(nameof(HasRecalledDraft));
        OnPropertyChanged(nameof(RecalledHintText));
    }

    private void Toggle(TaskRowViewModel row)
    {
        if (!CanEdit) return;
        var t = row.Model;
        var sel = Sel();
        t.DateCompleted = t.DateCompleted == null ? sel : null;
        Commit();
    }

    private void Delete(TaskRowViewModel row)
    {
        if (!CanEdit) return;
        _data.Tasks.Remove(row.Model);
        Commit();
    }

    /// <summary>Ask the view for a target day (today or later), then reschedule.</summary>
    private void MoveTask(TaskRowViewModel row)
    {
        if (!CanEdit || row.IsDone || _pickDate == null) return;
        var picked = _pickDate(DateTime.Today);
        if (picked == null) return;
        Reschedule(row, picked.Value);
    }

    private void BringToToday(TaskRowViewModel row)
    {
        if (!Reschedule(row, DateTime.Today)) return;
        // Follow the task back, otherwise it just vanishes from the future day's list.
        SetSelectedDate(DateTime.Today);
    }

    private bool Reschedule(TaskRowViewModel row, DateTime target)
    {
        if (!CanEdit || row.IsDone) return false;
        var day = DateUtil.From(target);
        if (!TaskScheduleService.Move(row.Model, day, DateUtil.Today())) return false;
        Commit();
        _notify?.Invoke("任务已移动到 " + DateUtil.Short(day));
        return true;
    }

    private void Commit()
    {
        DataStore.Save(_data);
        Rebuild();
    }

    /// <summary>An MCP tool changed the shared data; persist and refresh like a user action.</summary>
    public void CommitExternalChange() => Commit();

    // ---- 任务全文 ----

    /// <summary>Non-null while the full-text card is open. At most one at a time.</summary>
    public TaskRowViewModel? DetailRow => _detailRow;

    public bool IsDetailOpen => _detailRow != null;

    /// <summary>
    /// Opens the read-in-full card. Only rows whose title was actually trimmed qualify:
    /// for a short title the card would just repeat what the row already shows.
    /// </summary>
    public void OpenTaskDetail(TaskRowViewModel? row)
    {
        if (row == null || !row.IsTruncated) return;
        if (ReferenceEquals(_detailRow, row)) return;
        _detailRow = row;
        OnPropertyChanged(nameof(DetailRow));
        OnPropertyChanged(nameof(IsDetailOpen));
    }

    public void CloseTaskDetail()
    {
        if (_detailRow == null) return;
        _detailRow = null;
        OnPropertyChanged(nameof(DetailRow));
        OnPropertyChanged(nameof(IsDetailOpen));
    }

    /// <summary>Called when the system date rolls over while the app is running.</summary>
    public void OnNewDay()
    {
        var jump = _viewingToday;
        var moved = RolloverService.Run(_data, DateUtil.Today());

        // Yesterday's editor must not survive midnight: notes are day-bound.
        CancelNoteEdit();
        DataStore.Save(_data);

        if (jump) SetSelectedDate(DateTime.Today);
        else Rebuild();

        if (moved > 0) _notify?.Invoke($"{moved} 项未完成任务已顺延到今天");
    }

    private void Rebuild()
    {
        // The card points at one specific row instance; a rebuild replaces every row, so
        // close it instead of leaving it on a task that may have just moved or vanished.
        CloseTaskDetail();

        var sel = Sel();
        var today = DateUtil.Today();
        var editable = CanEdit;

        var rows = _data.Tasks
            .Where(t => AppearsOn(t, sel, today))
            .Select(t => new TaskRowViewModel(t, sel, today, editable,
                Toggle, Delete, row => Recall(row), MoveTask, BringToToday))
            .OrderBy(r => r.IsDone ? 1 : 0)
            .ThenByDescending(r => r.Model.Priority)
            .ThenBy(r => r.Model.DateOrigin, StringComparer.Ordinal)
            .ToList();

        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);

        var noteRows = _data.Notes
            .Where(n => n.Date == sel)
            .OrderBy(n => n.CreatedAt, StringComparer.Ordinal)
            .ThenBy(n => n.Id)
            .Select(n => new NoteRowViewModel(n, IsToday,
                q => ResolveQuote(_data, q), DescribeTaskRef, OpenTaskRef,
                EditNote, DeleteNote, QuoteNote, OpenQuote))
            .ToList();

        Notes.Clear();
        foreach (var n in noteRows) Notes.Add(n);

        TodayNoteCount = _data.Notes.Count(n => n.Date == today);

        DoneCount = rows.Count(r => r.IsDone);
        TotalCount = rows.Count;
        CarryCount = rows.Count(r => !r.IsDone && r.Model.PostponeDays > 0);
        Progress = TotalCount == 0 ? 0 : DoneCount * 100.0 / TotalCount;
        StreakDays = ComputeStreak(today);
        RefreshFutureSummary(today);

        OnPropertyChanged(nameof(SelectedDateText));
        OnPropertyChanged(nameof(WeekdayText));
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsFuture));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(HasNoTasks));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CanWriteNote));
        OnPropertyChanged(nameof(NoteReadOnlyVisible));
        OnPropertyChanged(nameof(CanSaveNote));
        OnPropertyChanged(nameof(HasNoNotes));
        OnPropertyChanged(nameof(NoteSummaryText));
        OnPropertyChanged(nameof(NoteReadOnlyText));
        OnPropertyChanged(nameof(HeaderSummaryText));
        OnPropertyChanged(nameof(TodayNoteCount));

        if (_isSearching) RebuildSearch();
    }

    /// <summary>Keeps the "future plans" entry point in sync with the task list.</summary>
    private void RefreshFutureSummary(string today)
    {
        var future = _data.Tasks
            .Where(t => TaskScheduleService.IsFuture(t, today))
            .OrderBy(t => t.DateOrigin, StringComparer.Ordinal)
            .ToList();

        _futureCount = future.Count;
        _nextFutureDate = future.Count == 0
            ? (DateTime?)null
            : DateTime.ParseExact(future[0].DateOrigin, DateUtil.Format, CultureInfo.InvariantCulture);

        OnPropertyChanged(nameof(FutureCount));
        OnPropertyChanged(nameof(HasFutureTasks));
    }

    private int ComputeStreak(string today)
    {
        var streak = 0;
        for (var i = 0; i < 366; i++)
        {
            var day = DateUtil.From(DateTime.Today.AddDays(-i));
            var any = _data.Tasks.Any(t => AppearsOn(t, day, today));
            if (any) streak++;
            else if (i == 0) continue;
            else break;
        }
        return streak;
    }

    // ---- 搜索：临时覆盖视图，跨天只读检索，跳转后才改 SelectedDate ----

    public bool IsSearching
    {
        get => _isSearching;
        private set => Set(ref _isSearching, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!Set(ref _searchText, value)) return;
            RebuildSearch();
        }
    }

    public ObservableCollection<SearchResultItem> SearchResults { get; } = new();

    public bool HasSearchQuery => SearchTokens(_searchText).Length > 0;

    public int SearchHitCount => SearchResults.OfType<SearchHit>().Count();

    public bool HasNoSearchHits => HasSearchQuery && SearchHitCount == 0;

    public string SearchSummaryText => !HasSearchQuery
        ? "搜索全部日期"
        : $"找到 {SearchHitCount} 条 · 按日期排序";

    public bool FilterAllActive
    {
        get => _searchFilter == 0;
        set { if (value) SetSearchFilter(0); }
    }

    public bool FilterTasksActive
    {
        get => _searchFilter == 1;
        set { if (value) SetSearchFilter(1); }
    }

    public bool FilterNotesActive
    {
        get => _searchFilter == 2;
        set { if (value) SetSearchFilter(2); }
    }

    public bool FilterOpenActive
    {
        get => _searchFilter == 3;
        set { if (value) SetSearchFilter(3); }
    }

    public bool ShowSearchGuide => !HasSearchQuery;

    public SearchHit? CurrentSearchHit => _currentSearchHit;

    public void OpenSearch()
    {
        CloseTaskDetail();
        if (_isSearching) return;
        IsSearching = true;
        RebuildSearch();
    }

    public void CloseSearch()
    {
        if (!_isSearching) return;
        IsSearching = false;
        SearchText = string.Empty;
        SetCurrentHit(null);
        RebuildSearch();
    }

    private void SetSearchFilter(int filter)
    {
        var value = Math.Clamp(filter, 0, 3);
        if (_searchFilter == value) return;
        _searchFilter = value;
        OnPropertyChanged(nameof(FilterAllActive));
        OnPropertyChanged(nameof(FilterTasksActive));
        OnPropertyChanged(nameof(FilterNotesActive));
        OnPropertyChanged(nameof(FilterOpenActive));
        RebuildSearch();
    }

    private static string[] SearchTokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool Matches(string haystack, string[] tokens) =>
        tokens.All(t => haystack.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

    private static string TaskMeta(TaskItem task)
    {
        var tags = task.Tags.Count == 0 ? string.Empty : " · " + string.Join(" ", task.Tags);
        return "任务" + tags + " · " + (task.DateCompleted != null ? "已完成" : "未完成");
    }

    private static string GroupLabel(string date, string today)
    {
        if (!DateOnly.TryParseExact(date, DateUtil.Format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var d)) return date;
        var label = d.Month + " 月 " + d.Day + " 日";
        if (date == today) return "今天 · " + label;
        if (d == DateOnly.FromDateTime(DateTime.Today.AddDays(-1))) return "昨天 · " + label;
        return label + " · " + WeekNames[(int)d.DayOfWeek];
    }

    private void RebuildSearch()
    {
        SearchResults.Clear();
        SetCurrentHit(null);

        var tokens = SearchTokens(_searchText);
        if (!_isSearching || tokens.Length == 0)
        {
            NotifySearchChanged();
            return;
        }

        var hits = new List<SearchHit>();
        if (_searchFilter != 2)
        {
            foreach (var task in _data.Tasks)
            {
                if (_searchFilter == 3 && task.DateCompleted != null) continue;
                if (!Matches(task.Title + " " + string.Join(" ", task.Tags), tokens)) continue;
                var day = string.IsNullOrEmpty(task.DateCurrent) ? task.DateOrigin : task.DateCurrent;
                hits.Add(new SearchHit(task.Id, day, task.Title, true, task.DateCompleted != null,
                    TaskMeta(task), _searchText, OpenSearchHit));
            }
        }

        if (_searchFilter != 1 && _searchFilter != 3)
        {
            foreach (var note in _data.Notes)
            {
                if (!Matches(note.Text, tokens)) continue;
                var time = note.CreatedAt.Length >= 5 ? note.CreatedAt.Substring(0, 5) : note.CreatedAt;
                hits.Add(new SearchHit(note.Id, note.Date, note.Text, false, false,
                    "随记 · " + time, _searchText, OpenSearchHit));
            }
        }

        var today = DateUtil.Today();
        foreach (var group in hits.GroupBy(h => h.Date, StringComparer.Ordinal)
                     .OrderByDescending(g => g.Key, StringComparer.Ordinal))
        {
            SearchResults.Add(new SearchGroupHeader(GroupLabel(group.Key, today), group.Count()));
            foreach (var hit in group.OrderByDescending(h => h.IsTask)
                         .ThenBy(h => h.Text, StringComparer.Ordinal))
                SearchResults.Add(hit);
        }

        NotifySearchChanged();
    }

    private void NotifySearchChanged()
    {
        OnPropertyChanged(nameof(SearchHitCount));
        OnPropertyChanged(nameof(SearchSummaryText));
        OnPropertyChanged(nameof(HasSearchQuery));
        OnPropertyChanged(nameof(HasNoSearchHits));
        OnPropertyChanged(nameof(ShowSearchGuide));
    }

    private void SetCurrentHit(SearchHit? hit)
    {
        if (ReferenceEquals(_currentSearchHit, hit)) return;
        _currentSearchHit?.SetCurrent(false);
        _currentSearchHit = hit;
        hit?.SetCurrent(true);
        OnPropertyChanged(nameof(CurrentSearchHit));
    }

    /// <summary>Moves the keyboard cursor over hits, skipping date headers.</summary>
    public SearchHit? MoveSearchSelection(int delta)
    {
        var hits = SearchResults.OfType<SearchHit>().ToList();
        if (hits.Count == 0) return null;
        var index = _currentSearchHit == null ? (delta > 0 ? -1 : 0) : hits.IndexOf(_currentSearchHit);
        if (index < 0) index = 0;
        var next = hits[Math.Clamp(index + delta, 0, hits.Count - 1)];
        SetCurrentHit(next);
        return next;
    }

    private void OpenSearchHit(SearchHit? hit)
    {
        if (hit == null) return;
        var task = hit.IsTask ? _data.Tasks.FirstOrDefault(t => t.Id == hit.Id) : null;
        var note = hit.IsTask ? null : _data.Notes.FirstOrDefault(n => n.Id == hit.Id);
        if (task == null && note == null) return;
        if (!DateOnly.TryParseExact(hit.Date, DateUtil.Format, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day)) return;

        CloseSearch();
        ShowTab(!hit.IsTask);
        SetSelectedDate(day.ToDateTime(TimeOnly.MinValue));
        SearchHitOpened?.Invoke(hit);
    }

    internal static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public abstract class SearchResultItem : ObservableObject { }

/// <summary>A date divider inside the search results. Never clickable.</summary>
public sealed class SearchGroupHeader : SearchResultItem
{
    public SearchGroupHeader(string text, int count)
    {
        Text = text;
        CountText = count + " 条";
    }

    public string Text { get; }
    public string CountText { get; }
}

/// <summary>One cross-day hit. Read-only: jumping happens in the original day view.</summary>
public sealed class SearchHit : SearchResultItem
{
    private static readonly Brush TaskBrush = MainViewModel.MakeBrush("#378ADD");
    private static readonly Brush NoteBrush = MainViewModel.MakeBrush("#B4B2A9");
    private static readonly Brush DoneBrush = MainViewModel.MakeBrush("#E8E6E0");

    private readonly Action<SearchHit> _open;
    private bool _isCurrent;

    public SearchHit(Guid id, string date, string text, bool isTask, bool isDone,
        string meta, string query, Action<SearchHit> open)
    {
        Id = id;
        Date = date;
        Text = text;
        IsTask = isTask;
        IsDone = isDone;
        MetaText = meta;
        Query = query;
        _open = open;
        OpenCommand = new RelayCommand(_ => _open(this));
    }

    public Guid Id { get; }
    public string Date { get; }
    public string Text { get; }
    public string Query { get; }
    public bool IsTask { get; }
    public bool IsDone { get; }
    public string MetaText { get; }
    public ICommand OpenCommand { get; }
    public Brush BarBrush => IsTask ? (IsDone ? DoneBrush : TaskBrush) : NoteBrush;
    public bool IsCurrent => _isCurrent;

    internal void SetCurrent(bool value)
    {
        if (Set(ref _isCurrent, value)) OnPropertyChanged(nameof(IsCurrent));
    }
}

public sealed class TaskRowViewModel : ObservableObject
{
    private static readonly SolidColorBrush CAccent = MainViewModel.MakeBrush("#378ADD");
    private static readonly SolidColorBrush CHigh = MainViewModel.MakeBrush("#E24B4A");
    private static readonly SolidColorBrush CNormal = MainViewModel.MakeBrush("#EF9F27");
    private static readonly SolidColorBrush CLow = MainViewModel.MakeBrush("#C9C7BE");
    private static readonly SolidColorBrush CDone = MainViewModel.MakeBrush("#E8E6E0");
    private static readonly SolidColorBrush CCheckBorder = MainViewModel.MakeBrush("#C9C7BE");
    private static readonly SolidColorBrush CCheckOverdue = MainViewModel.MakeBrush("#F09595");
    private static readonly SolidColorBrush CRedBack = MainViewModel.MakeBrush("#FCEBEB");
    private static readonly SolidColorBrush CRedText = MainViewModel.MakeBrush("#A32D2D");
    private static readonly SolidColorBrush CAmberBack = MainViewModel.MakeBrush("#FAEEDA");
    private static readonly SolidColorBrush CAmberText = MainViewModel.MakeBrush("#854F0B");
    private static readonly SolidColorBrush CBlueBack = MainViewModel.MakeBrush("#E6F1FB");
    private static readonly SolidColorBrush CBlueText = MainViewModel.MakeBrush("#185FA5");
    private static readonly SolidColorBrush CGrayBack = MainViewModel.MakeBrush("#F1EFE8");
    private static readonly SolidColorBrush CGrayText = MainViewModel.MakeBrush("#5F5E5A");

    private readonly Action<TaskRowViewModel> _toggle;
    private readonly Action<TaskRowViewModel> _delete;
    private readonly Action<TaskRowViewModel> _recall;
    private readonly Action<TaskRowViewModel> _move;
    private readonly Action<TaskRowViewModel> _bringToToday;

    /// <summary>Set by the view once it has measured the title against the row width.</summary>
    private bool _isTruncated;

    public TaskRowViewModel(TaskItem model, string sel, string today, bool canEdit,
        Action<TaskRowViewModel> toggle, Action<TaskRowViewModel> delete,
        Action<TaskRowViewModel> recall,
        Action<TaskRowViewModel> move, Action<TaskRowViewModel> bringToToday)
    {
        Model = model;
        CanEdit = canEdit;
        _toggle = toggle;
        _delete = delete;
        _recall = recall;
        _move = move;
        _bringToToday = bringToToday;

        CanBringToToday = CanMove && TaskScheduleService.IsFuture(model, today);

        ToggleCommand = new RelayCommand(_ => _toggle(this), _ => canEdit);
        DeleteCommand = new RelayCommand(_ => _delete(this), _ => canEdit);
        RecallCommand = new RelayCommand(_ => _recall(this), _ => canEdit);
        MoveCommand = new RelayCommand(_ => _move(this), _ => CanMove);
        BringToTodayCommand = new RelayCommand(_ => _bringToToday(this), _ => CanBringToToday);

        ComputeBadge(sel, today);
    }

    public TaskItem Model { get; }

    public string Title => Model.Title;

    public bool IsDone => Model.DateCompleted != null;

    public bool CanEdit { get; }

    public IReadOnlyList<string> Tags => Model.Tags;

    public string PriorityText => Model.Priority switch
    {
        2 => "高优先级",
        0 => "低优先级",
        _ => "中优先级"
    };

    public Brush PriorityBrush => IsDone ? CDone : Model.Priority switch
    {
        2 => CHigh,
        0 => CLow,
        _ => CNormal
    };

    /// <summary>Pill colours for the full-text card, which spells the level out in words.</summary>
    public Brush PriorityBack => IsDone ? CGrayBack : Model.Priority switch
    {
        2 => CRedBack,
        0 => CGrayBack,
        _ => CAmberBack
    };

    public Brush PriorityFore => IsDone ? CGrayText : Model.Priority switch
    {
        2 => CRedText,
        0 => CGrayText,
        _ => CAmberText
    };

    /// <summary>Whether the rendered title had to be cut off. Drives the click affordance.</summary>
    public bool IsTruncated => _isTruncated;

    /// <summary>Called from the view's measurement pass; never set from the data layer.</summary>
    internal void SetTruncated(bool value)
    {
        if (_isTruncated == value) return;
        _isTruncated = value;
        OnPropertyChanged(nameof(IsTruncated));
    }

    /// <summary>
    /// Where this task came from and where it sits now: the one part of a task the row
    /// itself cannot show for lack of space.
    /// </summary>
    public string DetailMetaText
    {
        get
        {
            var parts = new List<string> { "创建于 " + MonthDay(Model.DateOrigin) };

            if (Model.PostponeDays > 0) parts.Add("顺延 " + Model.PostponeDays + " 天");

            if (Model.DateCompleted != null)
                parts.Add("于 " + MonthDay(Model.DateCompleted) + " 完成");
            else if (Model.DateCurrent != Model.DateOrigin)
                parts.Add("现排在 " + MonthDay(Model.DateCurrent));

            return string.Join(" · ", parts);
        }
    }

    private static string MonthDay(string date)
    {
        var d = DateOnly.ParseExact(date, DateUtil.Format, CultureInfo.InvariantCulture);
        return d.Month + "/" + d.Day;
    }

    public Brush CheckFill => IsDone ? CAccent : Brushes.Transparent;

    public Brush CheckBorder => !IsDone && Model.PostponeDays > 0 ? CCheckOverdue : CCheckBorder;

    public string BadgeText { get; private set; } = string.Empty;
    public bool HasBadge => BadgeText.Length > 0;
    public Brush BadgeBack { get; private set; } = Brushes.Transparent;
    public Brush BadgeFore { get; private set; } = Brushes.Transparent;

    public ICommand ToggleCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RecallCommand { get; }
    public ICommand MoveCommand { get; }
    public ICommand BringToTodayCommand { get; }

    /// <summary>Only unfinished tasks on an editable day can be rescheduled.</summary>
    public bool CanMove => CanEdit && !IsDone;

    /// <summary>Show the shortcut only for tasks sitting on a future day.</summary>
    public bool CanBringToToday { get; }

    private void ComputeBadge(string sel, string today)
    {
        void Set(string text, Brush back, Brush fore)
        {
            BadgeText = text;
            BadgeBack = back;
            BadgeFore = fore;
        }

        if (!CanEdit)
        {
            if (Model.DateCompleted == null)
                Set("未完成 · 已顺延至今天", CRedBack, CRedText);
            else if (DateUtil.Compare(Model.DateCompleted, sel) > 0)
                Set("当日未完成 · 顺延至 " + DateUtil.Short(Model.DateCompleted!) + " 完成", CAmberBack, CAmberText);
            else if (Model.PostponeDays > 0)
                Set("顺延 " + Model.PostponeDays + " 天后完成", CGrayBack, CGrayText);
            return;
        }

        if (!IsDone && Model.PostponeDays > 0)
        {
            if (Model.PostponeDays >= 3) Set("顺延 " + Model.PostponeDays + " 天", CRedBack, CRedText);
            else Set("顺延 " + Model.PostponeDays + " 天", CAmberBack, CAmberText);
            return;
        }

        if (DateUtil.Compare(sel, today) > 0)
            Set("计划任务", CBlueBack, CBlueText);
    }
}

/// <summary>One entry in a note's quote list. Clicking it opens the quoted day.</summary>
public sealed class NoteQuoteDisplay : ObservableObject
{
    public NoteQuoteDisplay(NoteReference model, string text, ICommand openCommand)
    {
        Model = model;
        Text = text;
        OpenCommand = openCommand;
        var d = DateOnly.ParseExact(model.Date, DateUtil.Format, CultureInfo.InvariantCulture);
        DateText = d.Month + "/" + d.Day;
    }

    public NoteReference Model { get; }
    public string Text { get; }
    public string DateText { get; }
    public ICommand OpenCommand { get; }
}

public sealed class NoteRowViewModel : ObservableObject
{
    private readonly Action<NoteItem> _edit;
    private readonly Action<NoteItem> _delete;
    private readonly Action<NoteItem> _quote;
    private bool _isExpanded;
    private bool _canFold;

    public NoteRowViewModel(NoteItem model, bool canEdit,
        Func<NoteReference, string> resolve,
        Func<NoteTaskReference, (string Text, string Day)> resolveTask,
        Action<NoteTaskReference> openTask,
        Action<NoteItem> edit, Action<NoteItem> delete,
        Action<NoteItem> quote, Action<string> openQuote)
    {
        Model = model;
        CanEdit = canEdit;
        _edit = edit;
        _delete = delete;
        _quote = quote;

        EditCommand = new RelayCommand(_ => _edit(Model), _ => CanEdit);
        DeleteCommand = new RelayCommand(_ => _delete(Model), _ => CanEdit);
        QuoteCommand = new RelayCommand(_ => _quote(Model));

        // Seed only: the row is capped at three lines now, so the real answer comes from
        // NoteFoldWatcher measuring the text at its rendered width.
        _canFold = EstimateLines(model.Text) > 3;
        ToggleFoldCommand = new RelayCommand(_ =>
        {
            if (CanFold) IsExpanded = !IsExpanded;
        });

        Quotes = (model.References ?? new List<NoteReference>())
            .Where(r => r != null)
            .Select(r => new NoteQuoteDisplay(r, resolve(r),
                new RelayCommand(_ => openQuote(r.Date))))
            .ToList();

        TaskLinks = (model.TaskRefs ?? new List<NoteTaskReference>())
            .Where(r => r != null)
            .Select(r =>
            {
                var (text, day) = resolveTask(r);
                return new NoteTaskDisplay(r, text, day, new RelayCommand(_ => openTask(r)));
            })
            .ToList();
    }

    public NoteItem Model { get; }

    public string Time => Model.CreatedAt.Length >= 5
        ? Model.CreatedAt.Substring(0, 5)
        : Model.CreatedAt;

    public string Text => Model.Text;

    public bool CanEdit { get; }

    public IReadOnlyList<NoteQuoteDisplay> Quotes { get; }

    public bool HasQuotes => Quotes.Count > 0;

    /// <summary>Todos this note mentions with @. Clicking one opens the todo's own day.</summary>
    public IReadOnlyList<NoteTaskDisplay> TaskLinks { get; }

    public bool HasTaskLinks => TaskLinks.Count > 0;

    /// <summary>Only notes taller than three lines get a fold toggle.</summary>
    public bool CanFold => _canFold;

    /// <summary>Called by the view's measuring pass; never set from the data layer.</summary>
    internal void SetOverflows(bool value)
    {
        if (_canFold == value) return;
        _canFold = value;
        OnPropertyChanged(nameof(CanFold));
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (!Set(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(IsCollapsed));
            OnPropertyChanged(nameof(FoldText));
        }
    }

    public bool IsCollapsed => !IsExpanded;

    public string FoldText => IsExpanded ? "收起" : "展开全文";

    public ICommand ToggleFoldCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand QuoteCommand { get; }

    /// <summary>Rough line estimate used as a seed before the view has measured anything.</summary>
    private static int EstimateLines(string text)
    {
        const int charsPerLine = 24;
        return text.Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)charsPerLine)));
    }
}

/// <summary>One @mention chip under a note. Clicking it opens the mentioned todo.</summary>
public sealed class NoteTaskDisplay : ObservableObject
{
    public NoteTaskDisplay(NoteTaskReference model, string text, string day, ICommand openCommand)
    {
        Model = model;
        Text = text;
        DayText = day;
        OpenCommand = openCommand;
    }

    public NoteTaskReference Model { get; }
    public string Text { get; }
    public string DayText { get; }
    public ICommand OpenCommand { get; }
}

/// <summary>One row of the @ popup: a todo from today or yesterday.</summary>
public sealed class NoteMentionItem
{
    public NoteMentionItem(Guid taskId, string date, string title, bool isDone)
    {
        TaskId = taskId;
        Date = date;
        Title = title;
        IsDone = isDone;
    }

    public Guid TaskId { get; }

    /// <summary>Day the todo was found on: today or yesterday.</summary>
    public string Date { get; }

    public string Title { get; }

    public bool IsDone { get; }

    public string DayText => Date == DateUtil.Today()
        ? "今天"
        : Date == DateUtil.From(DateTime.Today.AddDays(-1))
            ? "昨天"
            : Date;

    public string StateText => IsDone ? "已完成" : "未完成";

    /// <summary>Right-hand label in the popup row.</summary>
    public string DetailText => DayText + " · " + StateText;
}

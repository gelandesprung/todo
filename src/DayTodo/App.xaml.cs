using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DayTodo.Mcp;
using DayTodo.Services;
using DayTodo.ViewModels;
using DayTodo.Views;
using WinForms = System.Windows.Forms;

namespace DayTodo;

public partial class App : Application, ISettingsHost
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;

    private WinForms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private bool _forceExit;
    private bool _shuttingDown;

    private McpSettings _mcpSettings = new();
    private AppSettings _appSettings = new();
    private McpServer? _mcpServer;
    private McpHttpHost? _mcpHost;
    private string _mcpError = string.Empty;
    private Views.SettingsWindow? _settingsWindow;

    public bool ForceExit => _forceExit;

    /// <summary>True only when the listener is actually running (switch state alone is not enough).</summary>
    public bool McpEnabled => _mcpHost?.IsRunning == true;

    // ---- ISettingsHost ----

    public AppSettings Settings => _appSettings;

    public McpSettings McpSettings => _mcpSettings;

    public bool McpRunning => McpEnabled;

    /// <summary>What the service is really doing right now, shown under the switches.</summary>
    public string McpStatusText
    {
        get
        {
            if (McpEnabled) return $"运行中 · 127.0.0.1:{_mcpSettings.Port}/mcp";
            if (!string.IsNullOrEmpty(_mcpError)) return "启动失败：" + _mcpError;
            return "已停止 · AI 客户端连不上";
        }
    }

    /// <summary>
    /// DAYTODO_DATA_DIR marks an isolated instance (smoke tests, a second data set).
    /// Give it its own mutex and activation event so it neither gets blocked by nor
    /// steals focus from the user's everyday tray instance. Default installs keep
    /// the original names and the original single-instance behaviour.
    /// </summary>
    private static string InstanceScope()
    {
        var dir = Environment.GetEnvironmentVariable("DAYTODO_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(dir.Trim().ToUpperInvariant());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return "." + Convert.ToHexString(hash).Substring(0, 8);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var createdNew = false;
        try
        {
            var scope = InstanceScope();
            _mutex = new Mutex(true, @"Local\DayTodo.SingleInstance" + scope, out createdNew);
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                @"Local\DayTodo.Activate" + scope, out _);
            if (!createdNew)
            {
                _activateEvent.Set();
                Shutdown();
                return;
            }
        }
        catch (Exception)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherException;

        try
        {
            var data = DataStore.Load();
            var moved = RolloverService.Run(data, DateUtil.Today());
            DataStore.Save(data);

            _appSettings = AppSettings.Load();
            _mcpSettings = McpSettings.Load();

            _window = new MainWindow();
            _vm = new MainViewModel(data, ShowBalloon, _window.PickMoveDate,
                _window.FocusTitleBox, _window.FocusNoteBox);
            _window.DataContext = _vm;
            MainWindow = _window;

            var commitExternal = () => _vm!.CommitExternalChange();
            _mcpServer = new McpServer(
                new McpTaskService(data, Dispatcher, commitExternal),
                new McpNoteService(data, Dispatcher, commitExternal));

            InitTray();
            StartActivationListener();
            if (_mcpSettings.Enabled) StartMcp();
            SyncMcpUi();

            var silentStart = Array.IndexOf(e.Args, "--minimized") >= 0;
            if (!silentStart) _window.Show();

            if (moved > 0) ShowBalloon($"{moved} 项未完成任务已顺延到今天");

            // Smoke-test hook: --verify shows the window (so binding errors surface),
            // simulates typing + Enter, checks the task was added, then restores the
            // whole data directory from a snapshot so the user's list is never changed.
            if (Array.IndexOf(e.Args, "--verify") >= 0)
            {
                var marker = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DayTodo-verify.ok");
                var log = new System.Text.StringBuilder();
                log.AppendLine("OK " + typeof(App).Assembly.GetName().Version + " " + DateTime.Now.ToString("O"));

                var snapshot = SnapshotDataDir();
                log.AppendLine("SnapshotFiles=" + snapshot.Count);
                log.AppendLine("DayFiles=" + DataStore.DayFileCount());
                log.AppendLine("ManifestExists=" + System.IO.File.Exists(DataStore.ManifestPath));

                try
                {
                    var box = _window.FindName("TitleBox") as System.Windows.Controls.TextBox;
                    var button = _window.FindName("AddButton") as System.Windows.Controls.Button;
                    if (box == null || _vm == null) throw new Exception("TitleBox / view model missing");

                    box.Text = "自检任务";
                    log.AppendLine("NewTitle=" + _vm.NewTitle);
                    log.AppendLine("CanAdd=" + _vm.CanAdd);
                    log.AppendLine("AddButtonEnabled=" + button?.IsEnabled);
                    log.AppendLine("PlaceholderHidden=" + !_vm.TitleHintVisible);

                    var source = System.Windows.PresentationSource.FromVisual(box);
                    if (source == null) throw new Exception("no PresentationSource");
                    var keyEvent = new System.Windows.Input.KeyEventArgs(
                        System.Windows.Input.Keyboard.PrimaryDevice, source,
                        Environment.TickCount, System.Windows.Input.Key.Enter)
                    { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
                    box.RaiseEvent(keyEvent);
                    log.AppendLine("EnterHandled=" + keyEvent.Handled);

                    using var doc = System.Text.Json.JsonDocument.Parse(
                        System.IO.File.ReadAllText(DataStore.TodayFilePath));
                    log.AppendLine("TasksAfterEnter=" + doc.RootElement.GetProperty("Tasks").GetArrayLength());

                    // Recall: the plan leaves the list and lands in the editor, where it
                    // can be changed and sent again. Recalling twice keeps only the last.
                    System.Func<System.Windows.Input.KeyEventArgs> enterKey = () =>
                    {
                        var src = System.Windows.PresentationSource.FromVisual(box);
                        var args = new System.Windows.Input.KeyEventArgs(
                            System.Windows.Input.Keyboard.PrimaryDevice, src,
                            Environment.TickCount, System.Windows.Input.Key.Enter)
                        { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent };
                        box.RaiseEvent(args);
                        return args;
                    };

                    var first = _vm.Rows.FirstOrDefault(r => r.Title == "自检任务");
                    log.AppendLine("RecallRowFound=" + (first != null));
                    if (first != null)
                    {
                        // The row template really renders a 撤回 button, and it is bound.
                        // Checked before the recall: afterwards the row is gone.
                        _window.UpdateLayout();
                        var recallButton = FindRecallButton();
                        log.AppendLine("RecallButtonRendered=" + (recallButton != null));
                        log.AppendLine("RecallButtonVisible=" +
                            (recallButton?.Visibility == System.Windows.Visibility.Visible));
                        log.AppendLine("RecallButtonCommand=" + (recallButton?.Command != null));

                        _vm.RecallRow(first);
                        log.AppendLine("RecallComposerTitle=" + _vm.NewTitle);
                        log.AppendLine("RecallDraftPending=" + _vm.HasRecalledDraft);
                        log.AppendLine("RecallHintText=" + _vm.RecalledHintText);
                        log.AppendLine("RecallRemovedFromList=" +
                            !_vm.Rows.Any(r => r.Title == "自检任务"));

                        // The recall has to reach the disk as well: the day file must
                        // no longer contain the recalled plan.
                        using var afterRecall = System.Text.Json.JsonDocument.Parse(
                            System.IO.File.ReadAllText(DataStore.TodayFilePath));
                        log.AppendLine("TasksAfterRecall=" +
                            afterRecall.RootElement.GetProperty("Tasks").GetArrayLength());

                        _window.UpdateLayout();
                        var hint = _window.FindName("RecallHint") as System.Windows.FrameworkElement;
                        log.AppendLine("RecallHintVisible=" +
                            (hint?.Visibility == System.Windows.Visibility.Visible));
                    }

                    box.Text = "自检任务二";
                    enterKey();
                    var second = _vm.Rows.FirstOrDefault(r => r.Title == "自检任务二");
                    if (second != null)
                    {
                        _vm.RecallRow(second);
                        log.AppendLine("RecallKeepsLastOnly=" + (_vm.NewTitle == "自检任务二"));
                        log.AppendLine("RecallFirstGone=" +
                            !_vm.Rows.Any(r => r.Title == "自检任务"));
                    }

                    // Send the recalled plan again, with a changed title.
                    box.Text = "自检任务二改";
                    enterKey();
                    log.AppendLine("RecallResent=" + _vm.Rows.Any(r => r.Title == "自检任务二改"));
                    log.AppendLine("RecallDraftCleared=" + !_vm.HasRecalledDraft);
                    log.AppendLine("RecallComposerCleared=" + (_vm.NewTitle.Length == 0));

                    // 随记: switch tabs, type into the real TextBox and save with Enter.
                    _vm.ShowNotesCommand.Execute(null);
                    _window.UpdateLayout();
                    var noteBox = _window.FindName("NoteBox") as System.Windows.Controls.TextBox;
                    var saveNoteButton = _window.FindName("SaveNoteButton") as System.Windows.Controls.Button;
                    if (noteBox == null) throw new Exception("NoteBox missing");

                    System.Func<System.Windows.Input.KeyEventArgs> noteEnter = () =>
                    {
                        var src = System.Windows.PresentationSource.FromVisual(noteBox);
                        var args = new System.Windows.Input.KeyEventArgs(
                            System.Windows.Input.Keyboard.PrimaryDevice, src,
                            Environment.TickCount, System.Windows.Input.Key.Enter)
                        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                        noteBox.RaiseEvent(args);
                        return args;
                    };

                    noteBox.Text = "自检随记";
                    log.AppendLine("NoteTabVisible=" + _vm.IsNotesTab);
                    log.AppendLine("NoteCanWrite=" + _vm.CanWriteNote);
                    log.AppendLine("NoteVmText=" + _vm.NewNoteText);
                    log.AppendLine("NoteCanSave=" + _vm.CanSaveNote);
                    log.AppendLine("NoteSaveButtonEnabled=" + saveNoteButton?.IsEnabled);
                    log.AppendLine("NotePlaceholderHidden=" + !_vm.NoteHintVisible);
                    log.AppendLine("NoteEnterHandled=" + noteEnter().Handled);
                    log.AppendLine("NoteVmTextAfter=" + _vm.NewNoteText);
                    log.AppendLine("NoteCountAfterEnter=" + _vm.Notes.Count);
                    log.AppendLine("NoteAdded=" + _vm.Notes.Any(n => n.Text == "自检随记"));

                    noteBox.Text = "自检随记二";
                    noteEnter();
                    var firstNote = _vm.Notes.FirstOrDefault(n => n.Text == "自检随记");
                    if (firstNote != null)
                    {
                        _vm.QuoteNoteForVerify(firstNote.Model);
                        log.AppendLine("QuotePending=" + _vm.HasPendingQuotes);
                        log.AppendLine("QuoteSummary=" + _vm.PendingQuoteText);
                        noteBox.Text = "自检引用随记";
                        noteEnter();
                        var quoted = _vm.Notes.FirstOrDefault(n => n.Text == "自检引用随记");
                        log.AppendLine("QuoteSaved=" + (quoted?.Model.References.Count == 1));
                        log.AppendLine("QuoteClearedAfterSave=" + !_vm.HasPendingQuotes);
                    }

                    var editRow = _vm.Notes.FirstOrDefault(n => n.Text == "自检随记");
                    if (editRow != null)
                    {
                        editRow.EditCommand.Execute(null);
                        log.AppendLine("NoteEditing=" + _vm.IsEditingNote);
                        log.AppendLine("NoteEditHint=" + _vm.NoteEditorHintText);
                        noteBox.Text = "自检随记改";
                        noteEnter();
                        log.AppendLine("NoteEdited=" + _vm.Notes.Any(n => n.Text == "自检随记改"));
                        log.AppendLine("NoteEditCountUnchanged=" + (_vm.Notes.Count == 3));
                        log.AppendLine("NoteEditCleared=" + !_vm.IsEditingNote);
                    }

                    using (var noteDoc = System.Text.Json.JsonDocument.Parse(
                               System.IO.File.ReadAllText(DataStore.TodayFilePath)))
                    {
                        log.AppendLine("NotesInDayFile=" +
                            noteDoc.RootElement.GetProperty("Notes").GetArrayLength());
                    }

                    // @ 引用：弹层只列今天/昨天的待办，回车把 @标题 写进正文并留下链接。
                    var noteListBox = _window.FindName("NoteList") as System.Windows.Controls.ListBox;
                    var mentionPopup = _window.FindName("MentionPopup")
                        as System.Windows.Controls.Primitives.Popup;
                    var mentionList = _window.FindName("MentionList") as System.Windows.Controls.ListBox;

                    // Reads a note's TaskRefs count straight from today's file, matched by text.
                    System.Func<string, int> taskRefsOfNote = prefix =>
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(
                            System.IO.File.ReadAllText(DataStore.TodayFilePath));
                        foreach (var note in doc.RootElement.GetProperty("Notes").EnumerateArray())
                        {
                            if (!note.TryGetProperty("Text", out var textNode)) continue;
                            if (!(textNode.GetString() ?? string.Empty)
                                .StartsWith(prefix, StringComparison.Ordinal)) continue;
                            return note.TryGetProperty("TaskRefs", out var refs)
                                ? refs.GetArrayLength()
                                : 0;
                        }
                        return -1;
                    };

                    noteBox.Text = "复盘 ";
                    noteBox.CaretIndex = noteBox.Text.Length;
                    noteBox.AppendText("@");
                    _window.UpdateLayout();
                    log.AppendLine("MentionPopupOpen=" + (mentionPopup?.IsOpen == true));
                    log.AppendLine("MentionTargetIsNoteBox=" +
                        ReferenceEquals(mentionPopup?.PlacementTarget, noteBox));
                    log.AppendLine("MentionCandidates=" + (mentionList?.Items.Count ?? 0));
                    log.AppendLine("MentionPanelWidth=" +
                        (mentionPopup?.Child as System.Windows.FrameworkElement)?.ActualWidth);
                    if (mentionPopup?.Child is System.Windows.FrameworkElement panel)
                        SaveShotElement(panel, "mention-popup", 2.0);

                    var picked = mentionList?.SelectedItem as NoteMentionItem;
                    log.AppendLine("MentionPicked=" + (picked?.Title ?? "(none)"));
                    log.AppendLine("MentionDayLabel=" + (picked?.DetailText ?? "(none)"));

                    // Enter belongs to the popup while it is open: commit, do not save.
                    var mentionEnter = noteEnter();
                    log.AppendLine("MentionCommitHandled=" + mentionEnter.Handled);
                    log.AppendLine("MentionTextInserted=" +
                        (picked != null && noteBox.Text == "复盘 @" + picked.Title + " "));
                    log.AppendLine("MentionCaretAfterInsert=" + (noteBox.CaretIndex == noteBox.Text.Length));
                    log.AppendLine("MentionPopupClosed=" + (mentionPopup?.IsOpen == false));

                    // Deleting the @text is how a mention is undone: the link goes with it.
                    noteBox.Text = "复盘";
                    noteEnter();
                    log.AppendLine("MentionPrunedWhenTextRemoved=" + (taskRefsOfNote("复盘") == 0));

                    noteBox.Text = "再说一次 ";
                    noteBox.CaretIndex = noteBox.Text.Length;
                    noteBox.AppendText("@");
                    noteEnter();   // picks the first candidate
                    noteEnter();   // no popup now: saves the note
                    log.AppendLine("MentionSavedToFile=" + (taskRefsOfNote("再说一次") == 1));

                    _window.UpdateLayout();
                    var linkedRow = _vm.Notes.FirstOrDefault(n => n.HasTaskLinks);
                    log.AppendLine("MentionRowHasChip=" + (linkedRow != null));
                    log.AppendLine("MentionChipText=" +
                        (linkedRow == null
                            ? "(none)"
                            : linkedRow.TaskLinks[0].DayText + "/" + linkedRow.TaskLinks[0].Text));
                    if (linkedRow != null && noteListBox != null) noteListBox.ScrollIntoView(linkedRow);
                    _window.UpdateLayout();
                    SaveShot(_window, "notes-list");

                    if (linkedRow != null)
                    {
                        var linkedItem = noteListBox?.ItemContainerGenerator.ContainerFromItem(linkedRow)
                            as System.Windows.Controls.ListBoxItem;
                        var chipButton = linkedItem == null
                            ? null
                            : FindVisual<System.Windows.Controls.Button>(linkedItem,
                                b => b.Command == linkedRow.TaskLinks[0].OpenCommand);
                        log.AppendLine("MentionChipButtonRendered=" + (chipButton != null));

                        linkedRow.TaskLinks[0].OpenCommand.Execute(null);
                        log.AppendLine("MentionJumpToTasksTab=" + !_vm.IsNotesTab);
                        log.AppendLine("MentionJumpToTaskDay=" +
                            (_vm.SelectedDate.Date == DateTime.Today));
                        _vm.ShowNotesCommand.Execute(null);
                        _window.UpdateLayout();
                    }

                    // 高度：短随记不再占满三行，长随记封顶三行，折叠按钮只在真放不下时出现。
                    var mediumText = new string('中', 80);
                    noteBox.Text = mediumText;
                    noteEnter();
                    _window.UpdateLayout();

                    var shortRow = _vm.Notes.FirstOrDefault(n => n.Text == "复盘");
                    var mediumRow = _vm.Notes.FirstOrDefault(n => n.Text == mediumText);
                    log.AppendLine("MediumRowSeededFold=" + (mediumRow != null));
                    log.AppendLine("MediumNoteFitsWithoutFold=" + (mediumRow?.CanFold == false));

                    System.Func<NoteRowViewModel?, double> collapsedHeight = row =>
                    {
                        if (row == null || noteListBox == null) return -1;
                        // The list virtualises: a row that never scrolled into view has no
                        // container to measure, so bring it in first.
                        noteListBox.ScrollIntoView(row);
                        noteListBox.UpdateLayout();
                        var item = noteListBox.ItemContainerGenerator.ContainerFromItem(row)
                            as System.Windows.Controls.ListBoxItem;
                        if (item == null) return -1;
                        var block = FindVisual<System.Windows.Controls.TextBlock>(item,
                            t => t.MaxHeight > 0 && t.MaxHeight < 200);
                        return block?.ActualHeight ?? -1;
                    };

                    var shortHeight = collapsedHeight(shortRow);
                    var mediumHeight = collapsedHeight(mediumRow);
                    log.AppendLine("ShortNoteHeight=" + shortHeight);
                    log.AppendLine("ShortNoteNotThreeLines=" + (shortHeight > 0 && shortHeight < 40));
                    log.AppendLine("MediumNoteNotCapped=" + (mediumHeight > 0 && mediumHeight < 57));

                    noteBox.Text = "三行封顶 " + new string('长', 200);
                    noteEnter();
                    var tallRow = _vm.Notes.FirstOrDefault(n => n.Text.StartsWith("三行封顶"));
                    if (tallRow != null && noteListBox != null) noteListBox.ScrollIntoView(tallRow);
                    _window.UpdateLayout();
                    var tallHeight = collapsedHeight(tallRow);
                    log.AppendLine("TallNoteHeight=" + tallHeight);
                    log.AppendLine("TallNoteCappedAtThreeLines=" + (tallHeight > 56 && tallHeight < 58));
                    log.AppendLine("TallNoteFoldable=" + (tallRow?.CanFold == true));
                    SaveShot(_window, "notes-fold");

                    // A past day must expose no editor and no edit/delete commands.
                    var yesterday = DateUtil.From(DateTime.Today.AddDays(-1));
                    _vm.SeedNoteForVerify(yesterday, "历史随记");
                    _vm.SeedNoteForVerify(yesterday, new string('长', 300));
                    _vm.SelectedDate = DateTime.Today.AddDays(-1);
                    _window.UpdateLayout();
                    log.AppendLine("HistoryNoteReadOnly=" + !_vm.CanWriteNote);
                    var historyRow = _vm.Notes.FirstOrDefault(n => n.Text == "历史随记");
                    log.AppendLine("HistoryRowNotEditable=" + (historyRow?.CanEdit == false));
                    var noteList = _window.FindName("NoteList") as System.Windows.Controls.ListBox;
                    var historyItem = noteList?.ItemContainerGenerator.ContainerFromIndex(0)
                        as System.Windows.Controls.ListBoxItem;
                    if (historyItem != null)
                    {
                        log.AppendLine("HistoryEditButtonHidden=" +
                            (FindVisual<System.Windows.Controls.Button>(historyItem,
                                b => (b.Content as string) == "编辑")?.Visibility ==
                             System.Windows.Visibility.Collapsed));
                        log.AppendLine("HistoryDeleteButtonHidden=" +
                            (FindVisual<System.Windows.Controls.Button>(historyItem,
                                b => (b.Content as string) == "删除")?.Visibility ==
                             System.Windows.Visibility.Collapsed));
                        log.AppendLine("HistoryQuoteButtonVisible=" +
                            (FindVisual<System.Windows.Controls.Button>(historyItem,
                                b => (b.Content as string) == "引用")?.Visibility ==
                             System.Windows.Visibility.Visible));
                    }

                    var longRow = _vm.Notes.FirstOrDefault(n => n.Text.Length > 100);
                    if (longRow != null && noteList != null) noteList.ScrollIntoView(longRow);
                    _window.UpdateLayout();
                    log.AppendLine("LongNoteCanFold=" + (longRow?.CanFold == true));
                    log.AppendLine("LongNoteInitiallyCollapsed=" + (longRow?.IsCollapsed == true));
                    var longIndex = longRow == null ? -1 : _vm.Notes.IndexOf(longRow);
                    var longItem = longIndex >= 0
                        ? noteList?.ItemContainerGenerator.ContainerFromIndex(longIndex)
                            as System.Windows.Controls.ListBoxItem
                        : null;
                    if (longItem != null)
                    {
                        var foldButton = FindVisual<System.Windows.Controls.Button>(longItem,
                            b => (b.Content as string) == "展开全文");
                        log.AppendLine("FoldButtonVisible=" +
                            (foldButton?.Visibility == System.Windows.Visibility.Visible));
                    }
                    longRow?.ToggleFoldCommand.Execute(null);
                    log.AppendLine("LongNoteExpanded=" + (longRow?.IsExpanded == true));
                    log.AppendLine("LongNoteFoldTextChanged=" + (longRow?.FoldText == "收起"));
                    longRow?.ToggleFoldCommand.Execute(null);
                    log.AppendLine("LongNoteCollapsedAgain=" + (longRow?.IsCollapsed == true));
                    _vm.SelectedDate = DateTime.Today;
                    _window.UpdateLayout();

                    // 任务全文：只有标题真被截断的行才可点开，卡片里是完整原文。
                    box.Text = "把上周三家供应商的报价单全部核对一遍，重点看交期和付款条件有没有变化，"
                             + "另外把差异整理成一页结论发给采购";
                    enterKey();
                    _window.UpdateLayout();
                    _window.UpdateLayout();

                    var longTask = _vm.Rows.FirstOrDefault(r => r.Title.StartsWith("把上周三家"));
                    var shortTask = _vm.Rows.FirstOrDefault(r => r.Title == "自检任务二改");
                    log.AppendLine("LongTaskRowFound=" + (longTask != null));
                    log.AppendLine("LongTaskTruncated=" + (longTask?.IsTruncated == true));
                    log.AppendLine("ShortTaskNotTruncated=" + (shortTask?.IsTruncated == false));

                    if (longTask != null)
                    {
                        // Back to the task tab: the card only ever opens from there.
                        _vm.ShowTasksCommand.Execute(null);
                        _window.UpdateLayout();
                        SaveShot(_window, "task-list");

                        // Drive the real path: the row's title block is a Button, so raise
                        // its click instead of calling the view model directly.
                        var taskList = _window.FindName("TaskList") as System.Windows.Controls.ListBox;
                        var taskIndex = _vm.Rows.IndexOf(longTask);
                        var taskItem = taskIndex < 0 ? null : taskList?.ItemContainerGenerator
                            .ContainerFromIndex(taskIndex) as System.Windows.Controls.ListBoxItem;
                        var bodyButton = taskItem == null ? null
                            : FindVisual<System.Windows.Controls.Button>(taskItem,
                                b => ReferenceEquals(b.Style, _window.FindResource("TaskBodyButton")));
                        log.AppendLine("TaskBodyButtonFound=" + (bodyButton != null));

                        bodyButton?.RaiseEvent(new RoutedEventArgs(
                            System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        _window.UpdateLayout();
                        SaveShot(_window, "task-detail", 2.0);

                        var detailLayer = _window.FindName("DetailLayer") as System.Windows.FrameworkElement;
                        var detailTitle = _window.FindName("DetailTitle") as System.Windows.Controls.TextBox;
                        log.AppendLine("DetailOpenByClick=" + _vm.IsDetailOpen);
                        log.AppendLine("DetailRowIsLongTask=" + ReferenceEquals(_vm.DetailRow, longTask));
                        log.AppendLine("DetailLayerVisible=" +
                            (detailLayer?.Visibility == Visibility.Visible));
                        log.AppendLine("DetailTitleFull=" + (detailTitle?.Text == longTask.Title));
                        log.AppendLine("DetailMetaText=" + longTask.DetailMetaText);

                        // Esc closes the card; the handler lives on the window so it works
                        // wherever focus happens to be inside the card.
                        var esc = new System.Windows.Input.KeyEventArgs(
                            System.Windows.Input.Keyboard.PrimaryDevice,
                            System.Windows.PresentationSource.FromVisual(_window),
                            Environment.TickCount, System.Windows.Input.Key.Escape)
                        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                        _window.RaiseEvent(esc);
                        log.AppendLine("DetailEscClosed=" + !_vm.IsDetailOpen);
                        log.AppendLine("DetailEscHandled=" + esc.Handled);

                        // A title that fits must not open the card, even if requested directly.
                        if (shortTask != null)
                        {
                            _vm.OpenTaskDetail(shortTask);
                            log.AppendLine("ShortTaskCardRefused=" + !_vm.IsDetailOpen);
                        }
                    }

                    // 搜索：聚焦入口、跨天结果、高亮、筛选、键盘跳转与 Esc 退出。
                    var searchBox = _window.FindName("SearchBox") as System.Windows.Controls.TextBox;
                    var searchLayer = _window.FindName("SearchLayer") as System.Windows.FrameworkElement;
                    var searchList = _window.FindName("SearchList") as System.Windows.Controls.ListBox;
                    if (searchBox == null || searchLayer == null || searchList == null)
                        throw new Exception("search UI missing");

                    _window.FocusSearchBox();
                    // FocusSearchBox defers to the input queue; flush it before asserting.
                    Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }),
                        DispatcherPriority.Input);
                    _window.UpdateLayout();
                    log.AppendLine("SearchOpened=" + _vm.IsSearching);
                    log.AppendLine("SearchLayerVisible=" + (searchLayer.Visibility == Visibility.Visible));
                    log.AppendLine("SearchGuideVisible=" + _vm.ShowSearchGuide);

                    searchBox.Text = "自检";
                    _window.UpdateLayout();
                    log.AppendLine("SearchTextVm=" + _vm.SearchText);
                    log.AppendLine("SearchHits=" + _vm.SearchHitCount);
                    log.AppendLine("SearchHasTaskHit=" +
                        _vm.SearchResults.OfType<SearchHit>().Any(h => h.IsTask));
                    log.AppendLine("SearchHasNoteHit=" +
                        _vm.SearchResults.OfType<SearchHit>().Any(h => !h.IsTask));
                    log.AppendLine("SearchGroupHeaders=" + _vm.SearchResults.OfType<SearchGroupHeader>().Count());

                    var firstHit = _vm.SearchResults.OfType<SearchHit>().FirstOrDefault();
                    if (firstHit != null)
                    {
                        var hitIndex = _vm.SearchResults.IndexOf(firstHit);
                        var hitItem = searchList.ItemContainerGenerator.ContainerFromIndex(hitIndex)
                            as System.Windows.FrameworkElement;
                        var marked = hitItem == null ? null
                            : FindVisual<System.Windows.Controls.TextBlock>(hitItem,
                                t => t.Inlines.Count > 1);
                        log.AppendLine("SearchHighlightRuns=" + (marked?.Inlines.Count ?? 0));
                    }
                    SaveShot(_window, "search-results", 2.0);

                    _vm.SetSearchFilterCommand.Execute(1);
                    log.AppendLine("SearchFilterTasksOnly=" +
                        _vm.SearchResults.OfType<SearchHit>().All(h => h.IsTask));
                    _vm.SetSearchFilterCommand.Execute(0);

                    System.Func<System.Windows.Input.Key, System.Windows.Input.KeyEventArgs> windowKey = key =>
                        new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                            System.Windows.PresentationSource.FromVisual(_window), Environment.TickCount, key)
                        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };

                    var searchDown = windowKey(System.Windows.Input.Key.Down);
                    _window.RaiseEvent(searchDown);
                    var current = _vm.CurrentSearchHit;
                    log.AppendLine("SearchDownHandled=" + searchDown.Handled);
                    log.AppendLine("SearchCurrentExists=" + (current != null));
                    log.AppendLine("SearchCurrentHighlighted=" + (current?.IsCurrent == true));

                    var searchEnter = windowKey(System.Windows.Input.Key.Enter);
                    _window.RaiseEvent(searchEnter);
                    log.AppendLine("SearchEnterHandled=" + searchEnter.Handled);
                    log.AppendLine("SearchClosedAfterJump=" + !_vm.IsSearching);
                    if (current != null)
                    {
                        var jumpedDay = DateOnly.FromDateTime(_vm.SelectedDate)
                            .ToString(DateUtil.Format, System.Globalization.CultureInfo.InvariantCulture);
                        log.AppendLine("SearchJumpedToHitDay=" + (jumpedDay == current.Date));
                        log.AppendLine("SearchJumpedTabMatches=" +
                            (current.IsTask ? _vm.IsTasksTab : _vm.IsNotesTab));
                    }

                    var dayBeforeEsc = _vm.SelectedDate;
                    _vm.OpenSearchCommand.Execute(null);
                    var searchEsc = windowKey(System.Windows.Input.Key.Escape);
                    _window.RaiseEvent(searchEsc);
                    log.AppendLine("SearchEscHandled=" + searchEsc.Handled);
                    log.AppendLine("SearchEscClosed=" + !_vm.IsSearching);
                    log.AppendLine("SearchEscKeptDate=" + (_vm.SelectedDate == dayBeforeEsc));
                    log.AppendLine("SearchTextClearedOnExit=" + string.IsNullOrEmpty(_vm.SearchText));

                    // Clicking the hit text takes the same jump path as Enter.
                    _vm.OpenSearchCommand.Execute(null);
                    searchBox.Text = "自检";
                    _window.UpdateLayout();
                    var clickTarget = _vm.SearchResults.OfType<SearchHit>().FirstOrDefault();
                    var clickIndex = clickTarget == null ? -1 : _vm.SearchResults.IndexOf(clickTarget);
                    var clickItem = clickIndex < 0 ? null
                        : searchList.ItemContainerGenerator.ContainerFromIndex(clickIndex)
                            as System.Windows.FrameworkElement;
                    var clickPanel = clickItem == null ? null
                        : FindVisual<System.Windows.Controls.StackPanel>(clickItem,
                            p => p.Cursor == System.Windows.Input.Cursors.Hand);
                    var clickArgs = new System.Windows.Input.MouseButtonEventArgs(
                        System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount,
                        System.Windows.Input.MouseButton.Left)
                    { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonDownEvent };
                    clickPanel?.RaiseEvent(clickArgs);
                    log.AppendLine("SearchClickFound=" + (clickPanel != null));
                    log.AppendLine("SearchClickHandled=" + clickArgs.Handled);
                    log.AppendLine("SearchClickJumped=" + !_vm.IsSearching);
                }
                catch (Exception ex)
                {
                    log.AppendLine("ERROR " + ex.Message);
                }
                finally
                {
                    try { RestoreDataDir(snapshot); }
                    catch (Exception ex) { log.AppendLine("ROLLBACK-ERROR " + ex.Message); }
                }

                // The settings dialog is a second binding surface: show it once so a
                // broken binding inside it surfaces here instead of at the user's desk.
                SettingsViewModel? settingsVm = null;
                try
                {
                    var settingsWin = new Views.SettingsWindow();
                    settingsVm = new SettingsViewModel(this, null);
                    settingsWin.DataContext = settingsVm;
                    settingsWin.Show();
                    settingsWin.UpdateLayout();
                    log.AppendLine("SettingsAutoStart=" + settingsVm.AutoStartEnabled);
                    log.AppendLine("SettingsMcpEnabled=" + settingsVm.McpEnabled);
                    log.AppendLine("SettingsPort=" + settingsVm.McpPortText);
                    log.AppendLine("SettingsEndpoint=" + settingsVm.McpEndpoint);
                    log.AppendLine("SettingsStatus=" + settingsVm.McpStatusText);
                    log.AppendLine("SettingsVersion=" + settingsVm.VersionText);
                    log.AppendLine("SettingsDaysDir=" + settingsVm.DaysDirPath);
                    settingsWin.Close();
                }
                catch (Exception ex)
                {
                    log.AppendLine("SETTINGS-ERROR " + ex.Message);
                }

                // Exercise the settings actions for real: change the port and the token
                // and check the listener follows. Everything is restored afterwards.
                if (settingsVm != null)
                {
                    try
                    {
                        var savedPort = _mcpSettings.Port;
                        var savedToken = _mcpSettings.Token;

                        settingsVm.McpPortText = (savedPort + 1).ToString();
                        settingsVm.CommitPort();
                        log.AppendLine("PortApplied=" + (_mcpSettings.Port == savedPort + 1));
                        log.AppendLine("PortListenerMoved=" + PortListening(savedPort + 1));
                        log.AppendLine("OldPortClosed=" + !PortListening(savedPort));

                        settingsVm.McpToken = "verify-token";
                        settingsVm.CommitToken();
                        log.AppendLine("TokenApplied=" + (_mcpSettings.Token == "verify-token"));
                        log.AppendLine("TokenRejectsAnonymous=" + (McpHttpStatus(savedPort + 1, null) == 401));
                        log.AppendLine("TokenAcceptsBearer=" + (McpHttpStatus(savedPort + 1, "verify-token") == 200));

                        settingsVm.McpPortText = savedPort.ToString();
                        settingsVm.CommitPort();
                        settingsVm.McpToken = savedToken;
                        settingsVm.CommitToken();
                        log.AppendLine("PortRestored=" + (_mcpSettings.Port == savedPort));
                        log.AppendLine("TokenRestored=" + (_mcpSettings.Token == savedToken));
                    }
                    catch (Exception ex)
                    {
                        log.AppendLine("SETTINGS-ACTION-ERROR " + ex.Message);
                    }
                }

                // CloudStore is a contract only at this point: check that the default
                // is inert, and that the local-folder reference implementation can
                // round-trip a manifest-shaped blob without any network access.
                log.AppendLine("CloudStoreProvider=" + CloudStoreProvider.Current.Provider);
                log.AppendLine("CloudStoreEnabled=" + CloudStoreProvider.Current.IsEnabled);
                try
                {
                    var probeDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "DayTodo-cloudstore-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    var probe = new LocalFolderCloudStore(probeDir);
                    var payload = new System.Text.UTF8Encoding(false)
                        .GetBytes("{\"Layout\":\"per-day\"}");

                    log.AppendLine("CloudStorePut=" +
                        probe.PutAsync("manifest.json", payload).GetAwaiter().GetResult());
                    log.AppendLine("CloudStoreDirtyWriteSkipped=" +
                        !probe.PutAsync("manifest.json", payload).GetAwaiter().GetResult());

                    var listed = probe.ListAsync().GetAwaiter().GetResult();
                    var got = probe.GetAsync("manifest.json").GetAwaiter().GetResult();
                    log.AppendLine("CloudStoreList=" + listed.Count);
                    log.AppendLine("CloudStoreRoundTrip=" +
                        (listed.Count == 1 && listed[0].Name == "manifest.json" &&
                         !string.IsNullOrEmpty(listed[0].Sha256) &&
                         got != null && got.AsSpan().SequenceEqual(payload)));
                    log.AppendLine("CloudStoreMissingIsNull=" +
                        (probe.GetAsync("missing.json").GetAwaiter().GetResult() == null));

                    var rejected = false;
                    try { probe.GetAsync("..\\evil.json").GetAwaiter().GetResult(); }
                    catch (ArgumentException) { rejected = true; }
                    log.AppendLine("CloudStoreRejectsTraversal=" + rejected);

                    var nullRejected = false;
                    try { NullCloudStore.Instance.PutAsync("x.json", payload).GetAwaiter().GetResult(); }
                    catch (NotSupportedException) { nullRejected = true; }
                    log.AppendLine("CloudStoreNullRejectsPut=" + nullRejected);

                    try { System.IO.Directory.Delete(probeDir, true); }
                    catch (Exception) { }
                }
                catch (Exception ex)
                {
                    log.AppendLine("CLOUDSTORE-ERROR " + ex.Message);
                }

                System.IO.File.WriteAllText(marker, log.ToString());
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：" + ex.Message, "DayTodo",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("程序出现异常：" + e.Exception.Message, "DayTodo",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>Finds the 撤回 button inside the first rendered task row.</summary>
    private System.Windows.Controls.Button? FindRecallButton()
    {
        var list = _window?.FindName("TaskList") as System.Windows.Controls.ListBox;
        if (list == null) return null;

        var container = list.ItemContainerGenerator.ContainerFromIndex(0)
            as System.Windows.Controls.ListBoxItem;
        if (container == null) return null;

        return FindVisual<System.Windows.Controls.Button>(container,
            b => (b.Content as string) == "撤回");
    }

    /// <summary>
    /// Writes the current window pixels into DAYTODO_SHOT_DIR, when that variable is set.
    /// Used to eyeball layout during development; without the variable it does nothing.
    /// </summary>
    private static void SaveShot(Window window, string name, double scale = 1.0) =>
        SaveShotElement(window, name, scale);

    /// <summary>Same as <see cref="SaveShot"/>, for a piece of UI that lives in its own window (a Popup).</summary>
    private static void SaveShotElement(System.Windows.Media.Visual visual, string name, double scale = 1.0)
    {
        var dir = Environment.GetEnvironmentVariable("DAYTODO_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;

        if (visual is not System.Windows.FrameworkElement element) return;

        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(element);
            var factor = dpi.DpiScaleX * scale;
            var width = (int)Math.Ceiling(element.ActualWidth * factor);
            var height = (int)Math.Ceiling(element.ActualHeight * factor);
            if (width <= 0 || height <= 0) return;

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96 * factor, 96 * factor,
                System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(element);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(System.IO.Path.Combine(dir, name + ".png"));
            encoder.Save(stream);
        }
        catch (Exception)
        {
            // A screenshot is a development convenience; never let it fail the run.
        }
    }

    /// <summary>Depth-first search of the visual tree for the first matching element.</summary>
    private static T? FindVisual<T>(System.Windows.DependencyObject parent,
        Func<T, bool>? match = null) where T : System.Windows.DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && (match == null || match(typed))) return typed;
            var found = FindVisual<T>(child, match);
            if (found != null) return found;
        }
        return null;
    }

    private void InitTray()
    {
        try
        {
            _tray = new WinForms.NotifyIcon
            {
                Text = "DayTodo 日清单",
                Visible = true
            };

            var info = GetResourceStream(new Uri("pack://application:,,,/Assets/tray.png", UriKind.Absolute));
            if (info.Stream != null)
            {
                using var stream = info.Stream;
                using var bmp = new System.Drawing.Bitmap(stream);
                _trayIcon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
                _tray.Icon = _trayIcon;
            }

            // The tray only carries navigation: everything else lives in the settings dialog.
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
            menu.Items.Add("设置…", null, (_, _) => OpenSettings());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => ExitApp());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowMainWindow();
        }
        catch (Exception)
        {
            // Tray is optional; the main window still works without it.
        }
    }

    private void ShowBalloon(string message)
    {
        try
        {
            Dispatcher.Invoke(() =>
                _tray?.ShowBalloonTip(4000, "DayTodo 日清单", message, WinForms.ToolTipIcon.Info));
        }
        catch (Exception) { }
    }

    public void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            var w = _window;
            if (w == null) return;
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w.Topmost = true;
            w.Topmost = false;
            w.Focus();
        });
    }

    /// <summary>Opens (or focuses) the settings dialog.</summary>
    public void OpenSettings()
    {
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new Views.SettingsWindow
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                _settingsWindow.DataContext = new SettingsViewModel(this, ShowBalloon);
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            _settingsWindow.Show();
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            _settingsWindow.Topmost = true;
            _settingsWindow.Topmost = false;
            _settingsWindow.Focus();
        });
    }

    public void ExitApp()
    {
        _forceExit = true;
        _shuttingDown = true;
        try { _tray?.Dispose(); }
        catch (Exception) { }
        try { _window?.Close(); }
        catch (Exception) { }
        Shutdown();
    }

    private void StartActivationListener()
    {
        var evt = _activateEvent;
        var thread = new Thread(() =>
        {
            while (true)
            {
                try { evt!.WaitOne(); }
                catch (Exception) { return; }
                try { Dispatcher.Invoke(ShowMainWindow); }
                catch (Exception) { return; }
            }
        })
        {
            IsBackground = true,
            Name = "DayTodo.ActivationListener"
        };
        thread.Start();
    }

    private void StartMcp()
    {
        if (_mcpServer == null || _mcpHost != null) return;
        try
        {
            var token = string.IsNullOrWhiteSpace(_mcpSettings.Token)
                ? Environment.GetEnvironmentVariable("DAYTODO_MCP_TOKEN")
                : _mcpSettings.Token;
            var host = new McpHttpHost(_mcpServer, _mcpSettings.Port, token);
            host.Start();
            _mcpHost = host;
            _mcpError = string.Empty;
        }
        catch (Exception ex)
        {
            // Port taken or no permission: the app keeps working, the status line explains.
            _mcpError = ex.Message;
            System.Diagnostics.Debug.WriteLine("DayTodo MCP 启动失败：" + ex.Message);
        }
    }

    private void StopMcp()
    {
        try { _mcpHost?.Stop(); }
        catch (Exception) { }
        _mcpHost = null;
    }

    /// <summary>Toggled from the sidebar switch or the settings dialog; the choice is persisted.</summary>
    public void SetMcpEnabled(bool enabled)
    {
        _mcpSettings.Enabled = enabled;
        McpSettings.Save(_mcpSettings);
        if (enabled) StartMcp();
        else { StopMcp(); _mcpError = string.Empty; }
        SyncMcpUi();
    }

    /// <summary>Applies a new port and restarts the listener so the change takes effect now.</summary>
    public void ApplyMcpPort(int port)
    {
        if (port < 1 || port > 65535) return;
        if (_mcpSettings.Port == port) return;
        _mcpSettings.Port = port;
        McpSettings.Save(_mcpSettings);
        RestartMcp();
        SyncMcpUi();
    }

    /// <summary>Applies a new access token and restarts the listener.</summary>
    public void ApplyMcpToken(string token)
    {
        token ??= string.Empty;
        if (_mcpSettings.Token == token) return;
        _mcpSettings.Token = token;
        McpSettings.Save(_mcpSettings);
        RestartMcp();
        SyncMcpUi();
    }

    /// <summary>Whether the close button hides to the tray (true) or quits for real (false).</summary>
    public void SetMinimizeToTrayOnClose(bool value)
    {
        if (_appSettings.MinimizeToTrayOnClose == value) return;
        _appSettings.MinimizeToTrayOnClose = value;
        AppSettings.Save(_appSettings);
    }

    private void RestartMcp()
    {
        if (!_mcpSettings.Enabled || _shuttingDown) return;
        StopMcp();
        StartMcp();
    }

    private void SyncMcpUi()
    {
        var on = _mcpSettings.Enabled;
        var status = McpStatusText;
        Dispatcher.Invoke(() =>
        {
            _window?.SyncMcpState(on, status);
            (_settingsWindow?.DataContext as SettingsViewModel)?.Refresh();
        });
    }

    /// <summary>True when something accepts connections on the given loopback port.</summary>
    private static bool PortListening(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connected = client.ConnectAsync(System.Net.IPAddress.Loopback, port).Wait(1500);
            return connected && client.Connected;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Posts a ping to the local MCP endpoint and returns the HTTP status code.</summary>
    private static int McpHttpStatus(int port, string? token)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Post, $"http://127.0.0.1:{port}/mcp");
            request.Content = new System.Net.Http.StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}",
                System.Text.Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            return (int)response.StatusCode;
        }
        catch (Exception) { return 0; }
    }

    /// <summary>
    /// Captures every file under the data directory so --verify can put the whole
    /// store back exactly: day files, manifest, per-day backups and settings alike.
    /// </summary>
    private static System.Collections.Generic.Dictionary<string, byte[]> SnapshotDataDir()
    {
        var result = new System.Collections.Generic.Dictionary<string, byte[]>(
            StringComparer.OrdinalIgnoreCase);
        var root = DataStore.DataDir;
        if (!System.IO.Directory.Exists(root)) return result;

        foreach (var f in System.IO.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories))
        {
            try { result[f] = System.IO.File.ReadAllBytes(f); }
            catch (Exception) { }
        }
        return result;
    }

    private static void RestoreDataDir(
        System.Collections.Generic.Dictionary<string, byte[]> snapshot)
    {
        var root = DataStore.DataDir;
        if (!System.IO.Directory.Exists(root)) return;

        foreach (var f in System.IO.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories))
        {
            if (snapshot.ContainsKey(f)) continue;
            try { System.IO.File.Delete(f); }
            catch (Exception) { }
        }

        foreach (var pair in snapshot)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(pair.Key);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(pair.Key, pair.Value);
            }
            catch (Exception) { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        StopMcp();
        try { _tray?.Dispose(); } catch (Exception) { }
        try { _trayIcon?.Dispose(); } catch (Exception) { }
        try { if (_mutex != null) _mutex.ReleaseMutex(); } catch (Exception) { }
        base.OnExit(e);
    }
}

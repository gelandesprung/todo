using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DayTodo.ViewModels;

namespace DayTodo;

public partial class MainWindow : Window
{
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
    private readonly DispatcherTimer _dateTimer;
    private bool _mcpSwitchSyncing;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            var app = System.Windows.Application.Current as App;
            SyncMcpState(app?.McpSettings.Enabled ?? false, app?.McpStatusText ?? "已停止");
            var vm = DataContext as MainViewModel;
            if (vm != null) vm.SearchHitOpened += OnSearchHitOpened;
        };

        _dateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _dateTimer.Tick += (_, _) => CheckDateChange();
        _dateTimer.Start();
    }

    /// <summary>
    /// Keeps the single sidebar switch in sync with the shell (tray menu, settings dialog).
    /// The switch shows the saved intent, the status line shows what is really running.
    /// </summary>
    public void SyncMcpState(bool enabled, string status)
    {
        _mcpSwitchSyncing = true;
        McpSwitch.IsChecked = enabled;
        _mcpSwitchSyncing = false;
        (DataContext as MainViewModel)?.SetMcpStatus(status);
    }

    private void McpSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_mcpSwitchSyncing) return;
        (System.Windows.Application.Current as App)?.SetMcpEnabled(McpSwitch.IsChecked == true);
    }

    /// <summary>Opens the settings dialog, where every other operation lives.</summary>
    private void Settings_Click(object sender, RoutedEventArgs e) =>
        (System.Windows.Application.Current as App)?.OpenSettings();

    // ---- 任务全文 ----

    /// <summary>
    /// The title block is a button for every row, but only a row whose title was measured
    /// as trimmed opens the card. A short task therefore stays completely inert on click,
    /// which is why the check lives here rather than in a command's CanExecute.
    /// </summary>
    private void TaskBody_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext is not TaskRowViewModel row) return;
        if (DataContext is not MainViewModel vm || !row.IsTruncated) return;

        vm.OpenTaskDetail(row);
        if (!vm.IsDetailOpen) return;

        DetailLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));

        // Focus lands after the layer has been laid out, so the caret is inside the card
        // from the first key press (Esc, Tab, Ctrl+C).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (vm.IsDetailOpen) DetailTitle.Focus();
        }), DispatcherPriority.Input);
    }

    private void DetailBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        (DataContext as MainViewModel)?.CloseTaskDetail();
    }

    private void DetailClose_Click(object sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.CloseTaskDetail();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Ctrl+F works from anywhere, including the multi-line note editor.
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            vm.OpenSearch();
            FocusSearchBox();
            e.Handled = true;
            return;
        }

        if (vm.IsSearching)
        {
            if (e.Key == Key.Escape)
            {
                vm.CloseSearch();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                var hit = vm.MoveSearchSelection(e.Key == Key.Down ? 1 : -1);
                if (hit != null) ScrollToSearchHit(hit);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && vm.CurrentSearchHit != null)
            {
                vm.OpenSearchHitCommand.Execute(vm.CurrentSearchHit);
                e.Handled = true;
                return;
            }
        }

        if (e.Key != Key.Escape || !vm.IsDetailOpen) return;

        vm.CloseTaskDetail();
        e.Handled = true;
    }

    // ---- 搜索 ----

    /// <summary>Focusing the box is the entry point; Ctrl+F uses the same path.</summary>
    private void SearchBox_GotFocus(object sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.OpenSearch();

    public void FocusSearchBox()
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    /// <summary>Clicking the text jumps just like the 跳转 button; headers have no handler.</summary>
    private void SearchHit_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not SearchHit hit) return;
        (DataContext as MainViewModel)?.OpenSearchHitCommand.Execute(hit);
        e.Handled = true;
    }

    private void ScrollToSearchHit(SearchHit hit)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            var list = SearchList;
            var index = 0;
            for (var i = 0; i < SearchList.Items.Count; i++)
                if (ReferenceEquals(SearchList.Items[i], hit)) { index = i; break; }
            list.ScrollIntoView(list.Items[index]);
        }), DispatcherPriority.Input);
    }

    private void OnSearchHitOpened(SearchHit hit)
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            UpdateLayout();

            if (DataContext is not MainViewModel vm) return;
            var index = -1;
            if (hit.IsTask)
            {
                for (var i = 0; i < vm.Rows.Count; i++)
                    if (vm.Rows[i].Model.Id == hit.Id) { index = i; break; }
            }
            else
            {
                for (var i = 0; i < vm.Notes.Count; i++)
                    if (vm.Notes[i].Model.Id == hit.Id) { index = i; break; }
            }
            if (index < 0) return;

            var list = hit.IsTask ? TaskList : NoteList;
            list.ScrollIntoView(list.Items[index]);
            var item = list.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (item == null) return;
            var card = FindVisual<Border>(item, b => b.Name == "RowCard");
            if (card == null) return;

            var original = card.Background;
            card.Background = new SolidColorBrush(Color.FromRgb(230, 241, 251));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            EventHandler? restore = null;
            restore = (_, _) =>
            {
                card.Background = original;
                timer.Stop();
                timer.Tick -= restore;
            };
            timer.Tick += restore;
            timer.Start();
        }), DispatcherPriority.Input);
    }

    private static T? FindVisual<T>(DependencyObject root, Predicate<T> match) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed && match(typed)) return typed;
            var nested = FindVisual(child, match);
            if (nested != null) return nested;
        }
        return null;
    }

    /// <summary>Lets the user pick today or any future day. Returns null when cancelled.</summary>
    public DateTime? PickMoveDate(DateTime earliest)
    {
        var start = earliest.Date;
        var suggested = start.AddDays(1);

        var calendar = new Calendar
        {
            DisplayDateStart = start,
            SelectedDate = suggested,
            DisplayDate = suggested,
            FirstDayOfWeek = DayOfWeek.Monday,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, 6, 8, 0)
        };

        var ok = new Button
        {
            Content = "移动", Width = 84, Height = 30, IsDefault = true,
            Style = (Style)FindResource("PrimaryButton")
        };
        var cancel = new Button
        {
            Content = "取消", Width = 72, Height = 30,
            Style = (Style)FindResource("SubtleButton"),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 10, 16, 16)
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "把任务移动到哪一天？", FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimary"),
            Margin = new Thickness(16, 16, 16, 0)
        });
        root.Children.Add(new TextBlock
        {
            Text = "只能选今天或之后；选今天就是拉回今天", FontSize = 11,
            Foreground = (Brush)FindResource("TextMuted"),
            Margin = new Thickness(16, 6, 16, 0)
        });
        root.Children.Add(calendar);
        root.Children.Add(buttons);

        var dialog = new Window
        {
            Title = "移动任务",
            Owner = this,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = Brushes.White,
            Content = root
        };

        DateTime? picked = null;
        ok.Click += (_, _) =>
        {
            if (calendar.SelectedDate == null) return;
            picked = calendar.SelectedDate.Value.Date;
            dialog.DialogResult = true;
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;

        dialog.ShowDialog();
        return picked;
    }

    private void CheckDateChange()
    {
        var now = DateOnly.FromDateTime(DateTime.Today);
        if (now == _today) return;
        _today = now;
        (DataContext as MainViewModel)?.OnNewDay();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) != null) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    // Hiding to the tray must not leave the card open for the next time the window appears.
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        (DataContext as MainViewModel)?.CloseTaskDetail();
        Close();
    }

    // The whole bordered area acts as the input box: clicking padding focuses the TextBox.
    private void InputArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!TitleBox.IsKeyboardFocusWithin) TitleBox.Focus();
    }

    /// <summary>
    /// Called right after a plan is recalled: the editor now holds that plan,
    /// so put the caret there and let the user edit it straight away.
    /// </summary>
    public void FocusTitleBox()
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            TitleBox.Focus();
            TitleBox.CaretIndex = TitleBox.Text.Length;
        }));
    }

    /// <summary>Quoting a past note lands the caret in today's note editor.</summary>
    public void FocusNoteBox()
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsVisible) return;
            NoteBox.Focus();
            NoteBox.CaretIndex = NoteBox.Text.Length;
        }));
    }

    private void NoteInputArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!NoteBox.IsKeyboardFocusWithin) NoteBox.Focus();
    }

    // ---- 随记里的 @ 引用 ----

    /// <summary>Rows currently offered by the popup, in display order.</summary>
    private List<NoteMentionItem> _mentionItems = new();

    /// <summary>Index of the '@' that opened the popup.</summary>
    private int _mentionStart = -1;

    /// <summary>Set while the popup itself rewrites the text box, so it does not reopen.</summary>
    private bool _mentionSuppress;

    /// <summary>
    /// The popup lives where the caret is, and the caret is only visible here. Every text
    /// change therefore re-reads the token before the caret: "@" plus anything typed after
    /// it, up to a space.
    /// </summary>
    private void NoteBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateMentionPopup();

    private void UpdateMentionPopup()
    {
        if (_mentionSuppress || DataContext is not MainViewModel vm)
        {
            CloseMention();
            return;
        }

        if (!TryReadMentionQuery(out var start, out var query))
        {
            CloseMention();
            return;
        }

        var items = vm.FindMentionCandidates(query);
        if (items.Count == 0)
        {
            CloseMention();
            return;
        }

        _mentionStart = start;
        _mentionItems = items.ToList();
        MentionList.ItemsSource = _mentionItems;
        MentionList.SelectedIndex = 0;
        // Narrower than the editor: eight short rows do not need 600px of width, and a
        // tighter panel reads as a picker rather than as a second text box.
        MentionPanel.Width = Math.Clamp(NoteBox.ActualWidth * 0.8, 300, 420);
        MentionPopup.IsOpen = true;
    }

    /// <summary>
    /// True when the caret sits in an unfinished @token: the character before '@' is a
    /// boundary, and nothing between '@' and the caret is whitespace or another '@'.
    /// </summary>
    private bool TryReadMentionQuery(out int start, out string query)
    {
        start = -1;
        query = string.Empty;

        var text = NoteBox.Text ?? string.Empty;
        var caret = Math.Min(NoteBox.CaretIndex, text.Length);
        if (caret <= 0 || caret > text.Length) return false;

        var at = text.LastIndexOf('@', caret - 1);
        if (at < 0) return false;

        if (at > 0)
        {
            var before = text[at - 1];
            // "a@b.com" is not a mention: an @token only starts after a boundary.
            if (!char.IsWhiteSpace(before) && ",，。；;：:、）)】]".IndexOf(before) < 0) return false;
        }

        var token = text.Substring(at + 1, caret - at - 1);
        if (token.Length > 24) return false;
        if (token.Any(ch => char.IsWhiteSpace(ch) || ch == '@')) return false;

        start = at;
        query = token;
        return true;
    }

    private void CloseMention()
    {
        if (MentionPopup.IsOpen) MentionPopup.IsOpen = false;
        _mentionItems.Clear();
        MentionList.ItemsSource = null;
        _mentionStart = -1;
    }

    /// <summary>Replaces the typed @token with "@标题 " and remembers the link.</summary>
    private void CommitMention()
    {
        if (!MentionPopup.IsOpen) return;

        var index = MentionList.SelectedIndex;
        if (index < 0 || index >= _mentionItems.Count || _mentionStart < 0)
        {
            CloseMention();
            return;
        }

        var item = _mentionItems[index];
        var text = NoteBox.Text ?? string.Empty;
        var caret = Math.Min(NoteBox.CaretIndex, text.Length);
        var start = Math.Min(_mentionStart, caret);
        var insert = "@" + item.Title + " ";

        // Rewriting the text raises TextChanged synchronously, which closes the popup and
        // resets the token index: the caret has to be placed from a captured value.
        _mentionSuppress = true;
        NoteBox.Text = text.Remove(start, caret - start).Insert(start, insert);
        NoteBox.CaretIndex = start + insert.Length;
        _mentionSuppress = false;

        CloseMention();
        (DataContext as MainViewModel)?.AddTaskRef(item);
        NoteBox.Focus();
    }

    private void MoveMention(int delta)
    {
        if (_mentionItems.Count == 0) return;
        var index = Math.Clamp(MentionList.SelectedIndex + delta, 0, _mentionItems.Count - 1);
        MentionList.SelectedIndex = index;
        MentionList.ScrollIntoView(MentionList.SelectedItem);
    }

    private void MentionList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(MentionList, source) is ListBoxItem container)
        {
            MentionList.SelectedItem = container.DataContext;
        }

        e.Handled = true;
        CommitMention();
    }

    // Clicking another control moves the focus out of the text box; the popup must not
    // linger over the window after that.
    private void NoteBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (NoteBox.IsKeyboardFocusWithin || MentionPopup.IsKeyboardFocusWithin) return;
            CloseMention();
        }), DispatcherPriority.Input);

    private void NoteBox_PreviewMouseDown(object sender, MouseButtonEventArgs e) => CloseMention();

    private void NoteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (MentionPopup.IsOpen)
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveMention(1);
                    e.Handled = true;
                    return;
                case Key.Up:
                    MoveMention(-1);
                    e.Handled = true;
                    return;
                case Key.Enter:
                case Key.Tab:
                    CommitMention();
                    e.Handled = true;
                    return;
                case Key.Escape:
                    CloseMention();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift) return;
        if (DataContext is MainViewModel vm) vm.SaveNoteCommand.Execute(null);
        e.Handled = true;
    }

    // Belt-and-braces: keep CanAdd in sync even if the binding is ever lost.
    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox box && DataContext is MainViewModel vm && vm.NewTitle != box.Text)
            vm.NewTitle = box.Text;
    }

    // Handled in code-behind instead of InputBindings: reliable with IME and never
    // depends on InputBinding inheriting the window's DataContext.
    private void TitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is MainViewModel vm) vm.AddCommand.Execute(null);
        e.Handled = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        if (!app.ForceExit)
        {
            if (app.Settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            // The user turned "minimize to tray" off in the settings dialog: really quit.
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(() => app.ExitApp()));
            return;
        }
        base.OnClosing(e);
    }
}

/// <summary>Shows the placeholder only while the search box is empty.</summary>
public sealed class EmptyTextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture) => Binding.DoNothing;
}

/// <summary>
/// Rebuilds a TextBlock's inlines so every query token is highlighted in place.
/// A plain Text binding cannot do this, and one run per match keeps long notes readable.
/// </summary>
public static class SearchHighlight
{
    private static readonly Brush MarkBack = MakeBrush("#E6F1FB");
    private static readonly Brush MarkFore = MakeBrush("#185FA5");

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SearchHighlight),
        new PropertyMetadata(string.Empty, OnChanged));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(SearchHighlight),
        new PropertyMetadata(string.Empty, OnChanged));

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);
    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);
    public static void SetQuery(DependencyObject element, string value) => element.SetValue(QueryProperty, value);
    public static string GetQuery(DependencyObject element) => (string)element.GetValue(QueryProperty);

    private static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        var text = GetText(block) ?? string.Empty;
        var tokens = (GetQuery(block) ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        block.Inlines.Clear();
        var ranges = new List<(int Start, int End)>();
        foreach (var token in tokens)
        {
            var from = 0;
            while (from < text.Length)
            {
                var at = text.IndexOf(token, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) break;
                ranges.Add((at, at + token.Length));
                from = at + Math.Max(1, token.Length);
            }
        }

        if (ranges.Count == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(int Start, int End)>();
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
            else
                merged.Add(range);
        }

        var pos = 0;
        foreach (var range in merged)
        {
            if (range.Start > pos) block.Inlines.Add(new Run(text.Substring(pos, range.Start - pos)));
            block.Inlines.Add(new Run(text.Substring(range.Start, range.End - range.Start))
            {
                Background = MarkBack,
                Foreground = MarkFore
            });
            pos = range.End;
        }
        if (pos < text.Length) block.Inlines.Add(new Run(text.Substring(pos)));
    }
}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // The whole bordered area acts as the input box: clicking padding focuses the TextBox.
    private void InputArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!TitleBox.IsKeyboardFocusWithin) TitleBox.Focus();
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

using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DayTodo.Services;
using DayTodo.ViewModels;
using WinForms = System.Windows.Forms;

namespace DayTodo;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;

    private WinForms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private bool _forceExit;

    public bool ForceExit => _forceExit;

    protected override void OnStartup(StartupEventArgs e)
    {
        var createdNew = false;
        try
        {
            _mutex = new Mutex(true, @"Local\DayTodo.SingleInstance", out createdNew);
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                @"Local\DayTodo.Activate", out _);
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

            _vm = new MainViewModel(data, ShowBalloon);
            _window = new MainWindow { DataContext = _vm };
            MainWindow = _window;

            InitTray();
            StartActivationListener();

            var silentStart = Array.IndexOf(e.Args, "--minimized") >= 0;
            if (!silentStart) _window.Show();

            if (moved > 0) ShowBalloon($"{moved} 项未完成任务已顺延到今天");

            // Smoke-test hook: --verify shows the window (so binding errors surface),
            // simulates typing + Enter, checks the task was added, then rolls the data
            // file back so the user's list is never changed.
            if (Array.IndexOf(e.Args, "--verify") >= 0)
            {
                var marker = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DayTodo-verify.ok");
                var log = new System.Text.StringBuilder();
                log.AppendLine("OK " + typeof(App).Assembly.GetName().Version + " " + DateTime.Now.ToString("O"));

                var dataPath = DataStore.DataFilePath;
                var before = System.IO.File.Exists(dataPath) ? System.IO.File.ReadAllText(dataPath) : null;
                var backupDir = System.IO.Path.Combine(DataStore.DataDir, "backups");
                var backupsBefore = new System.Collections.Generic.HashSet<string>(
                    System.IO.Directory.Exists(backupDir)
                        ? System.IO.Directory.GetFiles(backupDir)
                        : Array.Empty<string>());

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

                    using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(dataPath));
                    log.AppendLine("TasksAfterEnter=" + doc.RootElement.GetProperty("Tasks").GetArrayLength());
                }
                catch (Exception ex)
                {
                    log.AppendLine("ERROR " + ex.Message);
                }
                finally
                {
                    try
                    {
                        if (before != null)
                            System.IO.File.WriteAllText(dataPath, before, new System.Text.UTF8Encoding(false));
                        else if (System.IO.File.Exists(dataPath))
                            System.IO.File.Delete(dataPath);

                        if (System.IO.Directory.Exists(backupDir))
                            foreach (var f in System.IO.Directory.GetFiles(backupDir))
                                if (!backupsBefore.Contains(f))
                                    try { System.IO.File.Delete(f); } catch (Exception) { }
                    }
                    catch (Exception ex) { log.AppendLine("ROLLBACK-ERROR " + ex.Message); }
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

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());

            var auto = new WinForms.ToolStripMenuItem("开机自启")
            {
                Checked = StartupService.IsEnabled(),
                CheckOnClick = true
            };
            auto.CheckedChanged += (_, _) => StartupService.SetEnabled(auto.Checked);
            menu.Items.Add(auto);
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

    public void ExitApp()
    {
        _forceExit = true;
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

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch (Exception) { }
        try { _trayIcon?.Dispose(); } catch (Exception) { }
        try { if (_mutex != null) _mutex.ReleaseMutex(); } catch (Exception) { }
        base.OnExit(e);
    }
}

using System;
using System.Windows;
using System.Windows.Input;
using DayTodo.Mcp;
using DayTodo.Services;

namespace DayTodo.ViewModels;

/// <summary>
/// Everything the settings dialog needs from the application shell. Keeping it an
/// interface lets the view model stay free of WPF window plumbing.
/// </summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }
    McpSettings McpSettings { get; }

    /// <summary>True when the HTTP listener is actually up (not only the saved flag).</summary>
    bool McpRunning { get; }

    /// <summary>Human readable runtime state: 运行中 / 已停止 / 启动失败：…</summary>
    string McpStatusText { get; }

    void SetMcpEnabled(bool enabled);
    void ApplyMcpPort(int port);
    void ApplyMcpToken(string token);
    void SetMinimizeToTrayOnClose(bool value);
    void ExitApp();
}

/// <summary>
/// Backs the settings dialog: general behaviour, the MCP service, the data folder
/// and the about box. Everything the main window used to expose directly lives here now.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private readonly Action<string>? _notify;
    private bool _minimizeToTrayOnClose;
    private bool _mcpEnabled;
    private string _mcpPortText = "8765";
    private string _mcpToken = string.Empty;
    private string _mcpStatusText = "已停止";

    public SettingsViewModel(ISettingsHost host, Action<string>? notify = null)
    {
        _host = host;
        _notify = notify;

        OpenDataFolderCommand = new RelayCommand(_ => OpenDataFolder());
        OpenBackupFolderCommand = new RelayCommand(_ => OpenBackupFolder());
        CopyEndpointCommand = new RelayCommand(_ => CopyEndpoint());
        ExitAppCommand = new RelayCommand(_ => _host.ExitApp());

        _minimizeToTrayOnClose = _host.Settings.MinimizeToTrayOnClose;
        _mcpEnabled = _host.McpSettings.Enabled;
        _mcpPortText = _host.McpSettings.Port.ToString();
        _mcpToken = _host.McpSettings.Token;
        _mcpStatusText = _host.McpStatusText;
    }

    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenBackupFolderCommand { get; }
    public ICommand CopyEndpointCommand { get; }
    public ICommand ExitAppCommand { get; }

    // ---------------- 通用 ----------------

    /// <summary>Writes / removes the HKCU Run key. The registry is the single source of truth.</summary>
    public bool AutoStartEnabled
    {
        get => StartupService.IsEnabled();
        set
        {
            StartupService.SetEnabled(value);
            OnPropertyChanged();
            _notify?.Invoke(value ? "已设置开机自启" : "已取消开机自启");
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (!Set(ref _minimizeToTrayOnClose, value)) return;
            _host.SetMinimizeToTrayOnClose(value);
        }
    }

    // ---------------- MCP 服务 ----------------

    /// <summary>Desired state; the runtime state is reported by <see cref="McpStatusText"/>.</summary>
    public bool McpEnabled
    {
        get => _mcpEnabled;
        set
        {
            if (!Set(ref _mcpEnabled, value)) return;
            _host.SetMcpEnabled(value);
            OnPropertyChanged(nameof(McpStatusText));
        }
    }

    public string McpStatusText
    {
        get => _mcpStatusText;
        private set => Set(ref _mcpStatusText, value);
    }

    /// <summary>Edited as text so a half-typed port never restarts the listener.</summary>
    public string McpPortText
    {
        get => _mcpPortText;
        set => Set(ref _mcpPortText, value);
    }

    public string McpToken
    {
        get => _mcpToken;
        set => Set(ref _mcpToken, value);
    }

    public string McpEndpoint => $"http://127.0.0.1:{Port}/mcp";

    private int Port
    {
        get
        {
            if (!int.TryParse(_mcpPortText, out var p)) return _host.McpSettings.Port;
            return p;
        }
    }

    /// <summary>Applies the port box. Called on focus loss / Enter, not on every keystroke.</summary>
    public void CommitPort()
    {
        if (int.TryParse(_mcpPortText, out var port) && port >= 1 && port <= 65535)
        {
            if (port != _host.McpSettings.Port)
            {
                _host.ApplyMcpPort(port);
                _notify?.Invoke($"MCP 端口已改为 {port}");
            }
        }
        else
        {
            _mcpPortText = _host.McpSettings.Port.ToString();
            OnPropertyChanged(nameof(McpPortText));
        }
        OnPropertyChanged(nameof(McpEndpoint));
        OnPropertyChanged(nameof(McpStatusText));
    }

    /// <summary>Applies the token box. Called on focus loss / Enter.</summary>
    public void CommitToken()
    {
        if (_mcpToken == _host.McpSettings.Token) return;
        _host.ApplyMcpToken(_mcpToken);
        _notify?.Invoke(string.IsNullOrEmpty(_mcpToken) ? "已清除访问令牌" : "已更新访问令牌");
    }

    // ---------------- 数据 ----------------

    public string DataDirPath => DataStore.DataDir;

    public string DataFilePath => DataStore.DataFilePath;

    public string BackupDirPath => DataLocationService.BackupDir;

    // ---------------- 关于 ----------------

    public string VersionText
    {
        get
        {
            var v = typeof(SettingsViewModel).Assembly.GetName().Version;
            var text = v == null ? "1.0.0" : v.ToString();
            if (text.EndsWith(".0", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 2);
            return "DayTodo 日清单 v" + text;
        }
    }

    /// <summary>
    /// Pulls the volatile state back from the host (tray toggles, service restarts…).
    /// The port / token boxes keep their local text so a half-typed value is not lost.
    /// </summary>
    public void Refresh()
    {
        _minimizeToTrayOnClose = _host.Settings.MinimizeToTrayOnClose;
        _mcpEnabled = _host.McpSettings.Enabled;
        _mcpStatusText = _host.McpStatusText;

        OnPropertyChanged(nameof(MinimizeToTrayOnClose));
        OnPropertyChanged(nameof(McpEnabled));
        OnPropertyChanged(nameof(McpEndpoint));
        OnPropertyChanged(nameof(McpStatusText));
        OnPropertyChanged(nameof(AutoStartEnabled));
    }

    private void OpenDataFolder()
    {
        try { DataLocationService.OpenDataLocation(); }
        catch (Exception ex) { ShowError("打开记录位置失败：" + ex.Message); }
    }

    private void OpenBackupFolder()
    {
        try { DataLocationService.OpenBackupLocation(); }
        catch (Exception ex) { ShowError("打开备份目录失败：" + ex.Message); }
    }

    private void CopyEndpoint()
    {
        try
        {
            Clipboard.SetText(McpEndpoint);
            _notify?.Invoke("连接地址已复制");
        }
        catch (Exception ex) { ShowError("复制失败：" + ex.Message); }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "DayTodo 日清单",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

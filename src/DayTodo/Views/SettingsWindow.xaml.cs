using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DayTodo.ViewModels;

namespace DayTodo.Views;

/// <summary>
/// The single place where every non-core operation lives: auto start, tray behaviour,
/// the MCP service, the data folder and the about box. The main window keeps a single
/// MCP switch only.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        Loaded += (_, _) => (DataContext as SettingsViewModel)?.Refresh();
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

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        Close();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingEdits();
        Close();
    }

    /// <summary>Applies the port / token boxes that the user may still be editing.</summary>
    private void CommitPendingEdits()
    {
        var vm = DataContext as SettingsViewModel;
        if (vm == null) return;
        vm.CommitPort();
        vm.CommitToken();
    }

    private void PortBox_LostFocus(object sender, RoutedEventArgs e) =>
        (DataContext as SettingsViewModel)?.CommitPort();

    private void TokenBox_LostFocus(object sender, RoutedEventArgs e) =>
        (DataContext as SettingsViewModel)?.CommitToken();

    private void PortBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (DataContext as SettingsViewModel)?.CommitPort();
        e.Handled = true;
    }

    private void TokenBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        (DataContext as SettingsViewModel)?.CommitToken();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CommitPendingEdits();
            Close();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T match) return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}

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

    public MainWindow()
    {
        InitializeComponent();

        _dateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _dateTimer.Tick += (_, _) => CheckDateChange();
        _dateTimer.Start();
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
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}

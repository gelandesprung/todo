using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DayTodo.ViewModels;

namespace DayTodo.Views;

/// <summary>
/// Reports whether a single-line <see cref="TextBlock"/> really had to cut its text off.
///
/// The task row only offers the "read in full" card when something was actually trimmed,
/// so the flag has to come from the rendered width rather than a guess about character
/// count (Chinese and Latin text occupy wildly different widths). The measured result is
/// pushed back onto the row view model, which is what the row's cursor and hover hint
/// bind to.
/// </summary>
public static class TextTrimWatcher
{
    private static readonly DependencyPropertyKey IsTrimmedKey =
        DependencyProperty.RegisterAttachedReadOnly(
            "IsTrimmed", typeof(bool), typeof(TextTrimWatcher), new PropertyMetadata(false));

    public static readonly DependencyProperty IsTrimmedProperty = IsTrimmedKey.DependencyProperty;

    public static readonly DependencyProperty MonitorProperty =
        DependencyProperty.RegisterAttached("Monitor", typeof(bool), typeof(TextTrimWatcher),
            new PropertyMetadata(false, OnMonitorChanged));

    private static readonly DependencyPropertyDescriptor TextDescriptor =
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));

    public static void SetMonitor(DependencyObject element, bool value) =>
        element.SetValue(MonitorProperty, value);

    public static bool GetMonitor(DependencyObject element) =>
        (bool)element.GetValue(MonitorProperty);

    public static bool GetIsTrimmed(DependencyObject element) =>
        (bool)element.GetValue(IsTrimmedProperty);

    private static void OnMonitorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text) return;

        Detach(text);
        if (e.NewValue is true) Attach(text);
    }

    private static void Attach(TextBlock text)
    {
        text.Loaded += OnLoaded;
        text.Unloaded += OnUnloaded;
        text.SizeChanged += OnSizeChanged;
        text.DataContextChanged += OnDataContextChanged;
        TextDescriptor.AddValueChanged(text, OnTextChanged);
    }

    private static void Detach(TextBlock text)
    {
        text.Loaded -= OnLoaded;
        text.Unloaded -= OnUnloaded;
        text.SizeChanged -= OnSizeChanged;
        text.DataContextChanged -= OnDataContextChanged;
        TextDescriptor.RemoveValueChanged(text, OnTextChanged);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Update((TextBlock)sender);

    /// <summary>A block taken out of the tree (virtualisation) must not keep a stale flag.</summary>
    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var text = (TextBlock)sender;
        text.SetValue(IsTrimmedKey, false);
        if (text.DataContext is TaskRowViewModel row) row.SetTruncated(false);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((TextBlock)sender);

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Update((TextBlock)sender);

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock text) Update(text);
    }

    private static void Update(TextBlock text)
    {
        var trimmed = false;
        var value = text.Text;

        // A block without a laid-out width has nothing to measure against yet. Requiring
        // Loaded here would be wrong: under test the window lays out before the dispatcher
        // ever gets to raise Loaded.
        if (text.ActualWidth > 0 && !string.IsNullOrEmpty(value))
        {
            var typeface = new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch);
            var formatted = new FormattedText(value, CultureInfo.CurrentUICulture,
                text.FlowDirection, typeface, text.FontSize, Brushes.Black,
                VisualTreeHelper.GetDpi(text).PixelsPerDip);

            trimmed = formatted.WidthIncludingTrailingWhitespace > text.ActualWidth + 0.5;
        }

        text.SetValue(IsTrimmedKey, trimmed);

        if (text.DataContext is TaskRowViewModel row) row.SetTruncated(trimmed);
    }
}

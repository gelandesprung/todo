using System;
using System.Windows;
using System.Windows.Controls;
using DayTodo.ViewModels;

namespace DayTodo.Views;

/// <summary>
/// Decides whether a note is tall enough to need a fold toggle.
///
/// The collapsed block is capped with MaxHeight, so the rendered height alone cannot
/// tell "three lines" from "ten lines cut to three". A throwaway TextBlock with the
/// same font and width is measured without the cap instead: its desired height is the
/// real one. Guessing from character count does not work here - the note column is
/// wide, and Chinese and Latin take very different room.
/// </summary>
public static class NoteFoldWatcher
{
    public static readonly DependencyProperty MonitorProperty =
        DependencyProperty.RegisterAttached("Monitor", typeof(bool), typeof(NoteFoldWatcher),
            new PropertyMetadata(false, OnMeasureInputChanged));

    public static readonly DependencyProperty MaxVisibleHeightProperty =
        DependencyProperty.RegisterAttached("MaxVisibleHeight", typeof(double), typeof(NoteFoldWatcher),
            new PropertyMetadata(57.0, OnMeasureInputChanged));

    public static void SetMonitor(DependencyObject element, bool value) =>
        element.SetValue(MonitorProperty, value);

    public static bool GetMonitor(DependencyObject element) =>
        (bool)element.GetValue(MonitorProperty);

    public static void SetMaxVisibleHeight(DependencyObject element, double value) =>
        element.SetValue(MaxVisibleHeightProperty, value);

    public static double GetMaxVisibleHeight(DependencyObject element) =>
        (double)element.GetValue(MaxVisibleHeightProperty);

    private static void OnMeasureInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text) return;

        if (e.Property == MonitorProperty)
        {
            Detach(text);
            if (e.NewValue is true) Attach(text);
            return;
        }

        Update(text);
    }

    private static void Attach(TextBlock text)
    {
        text.Loaded += OnLoaded;
        text.SizeChanged += OnSizeChanged;
        text.DataContextChanged += OnDataContextChanged;
    }

    private static void Detach(TextBlock text)
    {
        text.Loaded -= OnLoaded;
        text.SizeChanged -= OnSizeChanged;
        text.DataContextChanged -= OnDataContextChanged;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Update((TextBlock)sender);

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((TextBlock)sender);

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Update((TextBlock)sender);

    private static void Update(TextBlock text)
    {
        // The expanded template swaps this block out, leaving it with no width. Keeping the
        // last answer is what stops the "收起" button from vanishing the moment it is used.
        if (text.ActualWidth <= 0) return;
        if (text.DataContext is not NoteRowViewModel row) return;

        var probe = new TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontStretch = text.FontStretch,
            FontStyle = text.FontStyle,
            FontWeight = text.FontWeight,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = text.LineHeight,
            LineStackingStrategy = text.LineStackingStrategy,
            MaxWidth = text.ActualWidth
        };

        probe.Measure(new Size(text.ActualWidth, double.PositiveInfinity));
        row.SetOverflows(probe.DesiredSize.Height > GetMaxVisibleHeight(text) + 0.5);
    }
}

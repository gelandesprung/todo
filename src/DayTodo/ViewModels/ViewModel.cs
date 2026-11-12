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

    public ObservableCollection<TaskRowViewModel> Rows { get; } = new();

    public ICommand PrevDayCommand { get; }
    public ICommand NextDayCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand SetPriorityCommand { get; }

    public MainViewModel(AppData data, Action<string>? notify = null)
    {
        _data = data;
        _notify = notify;

        PrevDayCommand = new RelayCommand(_ => MoveDay(-1));
        NextDayCommand = new RelayCommand(_ => MoveDay(1));
        TodayCommand = new RelayCommand(_ => SetSelectedDate(DateTime.Today));
        AddCommand = new RelayCommand(_ => AddTask());
        SetPriorityCommand = new RelayCommand(p => SetPriority(p));

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
        Rebuild();
    }

    public string SelectedDateText => $"{_selectedDate.Month} 月 {_selectedDate.Day} 日";

    public string WeekdayText => WeekNames[(int)_selectedDate.DayOfWeek];

    public bool IsToday => DateUtil.Compare(Sel(), DateUtil.Today()) == 0;
    public bool IsFuture => DateUtil.Compare(Sel(), DateUtil.Today()) > 0;
    public bool IsHistory => DateUtil.Compare(Sel(), DateUtil.Today()) < 0;

    public bool CanEdit => !IsHistory;

    public bool AutoStartEnabled
    {
        get => StartupService.IsEnabled();
        set
        {
            StartupService.SetEnabled(value);
            OnPropertyChanged();
        }
    }

    public string NewTitle
    {
        get => _newTitle;
        set
        {
            if (!Set(ref _newTitle, value)) return;
            OnPropertyChanged(nameof(CanAdd));
            OnPropertyChanged(nameof(TitleHintVisible));
        }
    }

    public string NewTags
    {
        get => _newTags;
        set => Set(ref _newTags, value);
    }

    public bool CanAdd => CanEdit && !string.IsNullOrWhiteSpace(_newTitle);

    public bool TitleHintVisible => string.IsNullOrEmpty(_newTitle);

    public bool HasNoTasks => _totalCount == 0;

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

    private void MoveDay(int delta) => SetSelectedDate(_selectedDate.AddDays(delta));

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
        Commit();
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

    private void Commit()
    {
        DataStore.Save(_data);
        Rebuild();
    }

    /// <summary>Called when the system date rolls over while the app is running.</summary>
    public void OnNewDay()
    {
        var jump = _viewingToday;
        var moved = RolloverService.Run(_data, DateUtil.Today());
        DataStore.Save(_data);

        if (jump) SetSelectedDate(DateTime.Today);
        else Rebuild();

        if (moved > 0) _notify?.Invoke($"{moved} 项未完成任务已顺延到今天");
    }

    private void Rebuild()
    {
        var sel = Sel();
        var today = DateUtil.Today();
        var editable = CanEdit;

        var rows = _data.Tasks
            .Where(t => AppearsOn(t, sel, today))
            .Select(t => new TaskRowViewModel(t, sel, today, editable, Toggle, Delete))
            .OrderBy(r => r.IsDone ? 1 : 0)
            .ThenByDescending(r => r.Model.Priority)
            .ThenBy(r => r.Model.DateOrigin, StringComparer.Ordinal)
            .ToList();

        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);

        DoneCount = rows.Count(r => r.IsDone);
        TotalCount = rows.Count;
        CarryCount = rows.Count(r => !r.IsDone && r.Model.PostponeDays > 0);
        Progress = TotalCount == 0 ? 0 : DoneCount * 100.0 / TotalCount;
        StreakDays = ComputeStreak(today);

        OnPropertyChanged(nameof(SelectedDateText));
        OnPropertyChanged(nameof(WeekdayText));
        OnPropertyChanged(nameof(IsToday));
        OnPropertyChanged(nameof(IsFuture));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(HasNoTasks));
        OnPropertyChanged(nameof(SummaryText));
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

    internal static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
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

    public TaskRowViewModel(TaskItem model, string sel, string today, bool canEdit,
        Action<TaskRowViewModel> toggle, Action<TaskRowViewModel> delete)
    {
        Model = model;
        CanEdit = canEdit;
        _toggle = toggle;
        _delete = delete;

        ToggleCommand = new RelayCommand(_ => _toggle(this), _ => canEdit);
        DeleteCommand = new RelayCommand(_ => _delete(this), _ => canEdit);

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

    public Brush CheckFill => IsDone ? CAccent : Brushes.Transparent;

    public Brush CheckBorder => !IsDone && Model.PostponeDays > 0 ? CCheckOverdue : CCheckBorder;

    public string BadgeText { get; private set; } = string.Empty;
    public bool HasBadge => BadgeText.Length > 0;
    public Brush BadgeBack { get; private set; } = Brushes.Transparent;
    public Brush BadgeFore { get; private set; } = Brushes.Transparent;

    public ICommand ToggleCommand { get; }
    public ICommand DeleteCommand { get; }

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

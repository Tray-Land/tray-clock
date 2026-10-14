using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayClock.Services;

namespace TrayClock.Views;

/// <summary>One city on the Clocks tab.</summary>
public sealed class CityRow(string name, TimeZoneInfo zone, bool removable) : INotifyPropertyChanged
{
    private string _time = "";
    private string _detail = "";
    private TimeSpan? _planned;

    public string Name { get; } = name;
    public TimeZoneInfo Zone { get; } = zone;
    /// <summary>False for the pinned Local row, which can't be moved or removed.</summary>
    public bool Removable { get; } = removable;

    public string Time { get => _time; private set => Set(ref _time, value, nameof(Time)); }
    public string Detail { get => _detail; private set => Set(ref _detail, value, nameof(Detail)); }

    /// <summary>While set, the Local row shows a time picker instead of its clock.</summary>
    public TimeSpan? PlannedTime
    {
        get => _planned;
        set
        {
            if (_planned != value)
            {
                _planned = value;
                Notify(string.Empty);
                PlannedChanged?.Invoke();
            }
        }
    }

    public Action? PlannedChanged { get; set; }

    public Visibility PickerVisibility => _planned is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TimeVisibility => _planned is null ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(DateTimeOffset instant)
    {
        Time = TimeZoneInfo.ConvertTime(instant, Zone).ToString("t", CultureInfo.CurrentCulture);
        Detail = ClockFormat.Describe(instant, Zone);
    }

    private void Set(ref string field, string value, string name)
    {
        if (field != value)
        {
            field = value;
            Notify(name);
        }
    }

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One timer on the Timers tab; reads its live values from the <see cref="TimerItem"/>.</summary>
public sealed class TimerRow : INotifyPropertyChanged
{
    private readonly TimerItem _item;

    internal TimerRow(TimerItem item)
    {
        _item = item;
        Refresh();
    }

    public int Id => _item.Id;
    public string Label => _item.Label;
    public string Remaining { get; private set; } = "";
    public double Progress { get; private set; }
    public string Glyph { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        Remaining = _item.Done ? "Done" : ClockFormat.Countdown(_item.Remaining);
        // Starts full and drains to empty, like the donut tray icon.
        Progress = _item.Done || _item.Duration <= TimeSpan.Zero
            ? 0
            : 100 * Math.Clamp(_item.Remaining / _item.Duration, 0, 1);
        Glyph = _item.Done ? "" : _item.Running ? "" : "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

/// <summary>
/// The flyout's content: world clocks, a stopwatch, and timers. The stopwatch and timers live in
/// <see cref="ClockState"/>; this page only draws them, and its 1-second tick runs only while shown.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private readonly ObservableCollection<CityRow> _cities = [];
    private readonly ObservableCollection<TimerRow> _timers = [];
    private readonly CityRow _localRow = new("Local", TimeZoneInfo.Local, removable: false);
    private readonly DispatcherQueueTimer _tick;
    private bool _syncingPicker;

    public FlyoutPage()
    {
        InitializeComponent();

        CityList.ItemsSource = _cities;
        TimerListView.ItemsSource = _timers;
        LapList.ItemsSource = new ObservableCollection<string>();

        foreach (string name in SettingsService.Cities)
        {
            if (CityCatalog.Find(name) is { } city)
            {
                _cities.Add(new CityRow(city.Name, city.Zone, removable: true));
            }
        }

        _localRow.PlannedChanged = Tick;
        _cities.Insert(0, _localRow);
        RefreshCityPicker();
        RebuildTimers();
        RebuildLaps();

        _tick = DispatcherQueue.CreateTimer();
        _tick.IsRepeating = true;
        _tick.Tick += (_, _) => Tick();
    }

    /// <summary>The flyout opened: start drawing live time.</summary>
    public void OnShown()
    {
        PinButton.IsChecked = App.Current.IsFlyoutPinned;

        if (ClockState.PendingTab is { } tab)
        {
            ClockState.PendingTab = null;
            Tabs.SelectedIndex = tab;
        }

        RebuildTimers();
        Tick();
        _tick.Start();
    }

    /// <summary>The flyout hid: stop ticking. Nothing runs while hidden.</summary>
    public void OnHidden() => _tick.Stop();

    public void Dispose() => _tick.Stop();

    private void Tick()
    {
        // The stopwatch wants tenths of a second; everything else only needs whole seconds.
        _tick.Interval = ClockState.StopwatchRunning && Tabs.SelectedIndex == 1
            ? TimeSpan.FromMilliseconds(50)
            : TimeSpan.FromMilliseconds(250);

        DateTimeOffset instant = _localRow.PlannedTime is { } t ? ClockFormat.LocalInstant(t) : DateTimeOffset.Now;
        foreach (CityRow row in _cities)
        {
            row.Update(instant);
        }

        StopwatchText.Text = ClockFormat.Stopwatch(ClockState.StopwatchElapsed);
        StopwatchStartButton.Content = ClockState.StopwatchRunning ? "Pause"
            : ClockState.StopwatchElapsed > TimeSpan.Zero ? "Resume" : "Start";
        StopwatchLapButton.IsEnabled = ClockState.StopwatchRunning;
        StopwatchResetButton.IsEnabled = ClockState.StopwatchElapsed > TimeSpan.Zero;

        foreach (TimerRow row in _timers)
        {
            row.Refresh();
        }
    }

    // Clocks
    private void AddCityButton_Toggled(object sender, RoutedEventArgs e)
    {
        bool show = AddCityButton.IsChecked == true;
        CityPicker.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            CityPicker.IsDropDownOpen = true;
        }
    }

    private void TimeShiftButton_Toggled(object sender, RoutedEventArgs e)
    {
        // The Local row swaps its clock for a picker; unchecking returns everything to live time.
        TimeSpan now = DateTime.Now.TimeOfDay;
        _localRow.PlannedTime = TimeShiftButton.IsChecked == true
            ? new TimeSpan(now.Hours, now.Minutes, 0)
            : null;
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Visibility v = Tabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddCityButton.Visibility = v;
        TimeShiftButton.Visibility = v;
    }

    private void RefreshCityPicker()
    {
        _syncingPicker = true;
        HashSet<string> used = _cities.Select(c => c.Name).ToHashSet();
        CityPicker.ItemsSource = CityCatalog.All.Select(c => c.Name).Where(n => !used.Contains(n)).ToList();
        CityPicker.SelectedIndex = -1;
        _syncingPicker = false;
    }

    private void CityPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingPicker || CityPicker.SelectedItem is not string name || CityCatalog.Find(name) is not { } city)
        {
            return;
        }

        _cities.Add(new CityRow(city.Name, city.Zone, removable: true));
        SaveCities();
        RefreshCityPicker();
        CityPicker.Visibility = Visibility.Collapsed;
        AddCityButton.IsChecked = false;
        Tick();
    }

    private void CityList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        SwipeControl? swipe = args.ItemContainer.ContentTemplateRoot as SwipeControl;
        if (args.Item is not CityRow { Removable: true } row)
        {
            args.ItemContainer.ContextFlyout = null;
            if (swipe is not null)
            {
                swipe.RightItems = null;
            }

            return;
        }

        if (swipe is not null)
        {
            // Swipe the row sideways (touch or pen) to reveal Delete.
            SwipeItem delete = new()
            {
                Text = "Delete",
                IconSource = new FontIconSource { Glyph = "" },
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Firebrick),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            };
            delete.Invoked += (_, _) => RemoveCity(row);
            SwipeItems items = new() { Mode = SwipeMode.Execute };
            items.Add(delete);
            swipe.RightItems = items;
        }

        MenuFlyout menu = new();
        MenuFlyoutItem up = new() { Text = "Move up", Icon = new FontIcon { Glyph = "" } };
        up.Click += (_, _) => MoveCity(row, -1);
        MenuFlyoutItem down = new() { Text = "Move down", Icon = new FontIcon { Glyph = "" } };
        down.Click += (_, _) => MoveCity(row, 1);
        MenuFlyoutItem remove = new() { Text = "Remove", Icon = new FontIcon { Glyph = "" } };
        remove.Click += (_, _) => RemoveCity(row);
        menu.Opening += (_, _) =>
        {
            int index = _cities.IndexOf(row);
            up.IsEnabled = index > 1; // index 0 is Local
            down.IsEnabled = index < _cities.Count - 1;
        };
        menu.Items.Add(up);
        menu.Items.Add(down);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);
        args.ItemContainer.ContextFlyout = menu;
    }

    private void CityList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Local stays pinned at the top however the drag ended.
        int local = _cities.IndexOf(_localRow);
        if (local > 0)
        {
            _cities.Move(local, 0);
        }

        SaveCities();
    }

    private void MoveCity(CityRow row, int delta)
    {
        int from = _cities.IndexOf(row);
        int to = from + delta;
        if (from < 1 || to < 1 || to >= _cities.Count)
        {
            return;
        }

        _cities.Move(from, to);
        SaveCities();
    }

    private void RemoveCity(CityRow row)
    {
        _cities.Remove(row);
        SaveCities();
        RefreshCityPicker();
    }

    private void SaveCities() => SettingsService.Cities = _cities.Skip(1).Select(c => c.Name).ToArray();

    // Stopwatch
    private void StopwatchStart_Click(object sender, RoutedEventArgs e)
    {
        ClockState.StopwatchToggle();
        Tick();
    }

    private void StopwatchLap_Click(object sender, RoutedEventArgs e)
    {
        ClockState.StopwatchLap();
        RebuildLaps();
    }

    private void StopwatchReset_Click(object sender, RoutedEventArgs e)
    {
        ClockState.StopwatchReset();
        RebuildLaps();
        Tick();
    }

    private void RebuildLaps()
    {
        IReadOnlyList<TimeSpan> laps = ClockState.Laps;
        var items = (ObservableCollection<string>)LapList.ItemsSource;
        items.Clear();
        for (int i = laps.Count - 1; i >= 0; i--)
        {
            TimeSpan split = i == 0 ? laps[0] : laps[i] - laps[i - 1];
            items.Add($"Lap {i + 1,-3} {ClockFormat.Stopwatch(split)}   total {ClockFormat.Stopwatch(laps[i])}");
        }
    }

    // Timers
    private void PresetTimer_Click(object sender, RoutedEventArgs e)
    {
        int seconds = int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture);
        ClockState.AddTimer(TimeSpan.FromSeconds(seconds));
        AfterTimersChanged();
    }

    private void CustomTimer_Click(object sender, RoutedEventArgs e)
    {
        double h = double.IsNaN(HoursBox.Value) ? 0 : HoursBox.Value;
        double m = double.IsNaN(MinutesBox.Value) ? 0 : MinutesBox.Value;
        double s = double.IsNaN(SecondsBox.Value) ? 0 : SecondsBox.Value;
        TimeSpan duration = TimeSpan.FromSeconds((h * 3600) + (m * 60) + s);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        ClockState.AddTimer(duration);
        HoursBox.Value = MinutesBox.Value = SecondsBox.Value = 0;
        AfterTimersChanged();
    }

    private void ToggleTimer_Click(object sender, RoutedEventArgs e)
    {
        ClockState.ToggleTimer((int)((FrameworkElement)sender).Tag);
        AfterTimersChanged();
    }

    private void RemoveTimer_Click(object sender, RoutedEventArgs e)
    {
        ClockState.RemoveTimer((int)((FrameworkElement)sender).Tag);
        AfterTimersChanged();
    }

    private void AfterTimersChanged()
    {
        RebuildTimers();
        Tick();
    }

    private void RebuildTimers()
    {
        _timers.Clear();
        foreach (TimerItem item in ClockState.Timers)
        {
            _timers.Add(new TimerRow(item));
        }
    }

    private void PinButton_Toggled(object sender, RoutedEventArgs e)
    {
        bool pinned = PinButton.IsChecked == true;
        App.Current.IsFlyoutPinned = pinned;
        PinIcon.Glyph = pinned ? "" : ""; // Pinned / Pin
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
}

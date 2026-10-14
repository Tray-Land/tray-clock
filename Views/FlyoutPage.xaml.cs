using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TrayClock.Services;
using Windows.ApplicationModel.DataTransfer;
using VirtualKey = Windows.System.VirtualKey;

namespace TrayClock.Views;

/// <summary>One city on the Clocks tab.</summary>
public sealed class CityRow(string name, TimeZoneInfo zone) : INotifyPropertyChanged
{
    private string _time = "";
    private string _detail = "";
    private string _dayGlyph = "";

    public string Name { get; } = name;
    public TimeZoneInfo Zone { get; } = zone;
    public string MoreName => $"More options for {Name}";

    public string Time { get => _time; private set => Set(ref _time, value, nameof(Time)); }
    public string Detail { get => _detail; private set => Set(ref _detail, value, nameof(Detail)); }

    /// <summary>A sun or a moon, so it's obvious at a glance who's awake.</summary>
    public string DayGlyph { get => _dayGlyph; private set => Set(ref _dayGlyph, value, nameof(DayGlyph)); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(DateTimeOffset instant)
    {
        DateTimeOffset there = TimeZoneInfo.ConvertTime(instant, Zone);
        Time = there.ToString("t", CultureInfo.CurrentCulture);
        Detail = ClockFormat.Describe(instant, Zone);
        DayGlyph = FlyoutPage.DayGlyphFor(there);
    }

    private void Set(ref string field, string value, string name)
    {
        if (field != value)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

/// <summary>One recorded lap on the Stopwatch tab.</summary>
public sealed class LapRow
{
    internal LapRow(Lap lap)
    {
        Number = lap.Number.ToString(CultureInfo.CurrentCulture);
        Split = ClockFormat.Stopwatch(lap.Split);
        Total = ClockFormat.Stopwatch(lap.Total);
        FastestVisibility = lap.Rank == LapRank.Fastest ? Visibility.Visible : Visibility.Collapsed;
        SlowestVisibility = lap.Rank == LapRank.Slowest ? Visibility.Visible : Visibility.Collapsed;
        string rank = lap.Rank switch { LapRank.Fastest => ", fastest", LapRank.Slowest => ", slowest", _ => "" };
        Description = $"Lap {Number}{rank}, split {Split}, total {Total}";
    }

    public string Number { get; }
    public string Split { get; }
    public string Total { get; }
    public Visibility FastestVisibility { get; }
    public Visibility SlowestVisibility { get; }
    public string Description { get; }
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
    public string Status { get; private set; } = "";
    public double Progress { get; private set; }
    public double RingOpacity { get; private set; } = 1;
    public string CenterGlyph { get; private set; } = "";
    public string ToggleGlyph { get; private set; } = "";
    public string ToggleName { get; private set; } = "";
    public string Description { get; private set; } = "";

    /// <summary>Finished and going off until dismissed or restarted.</summary>
    public bool IsRinging { get; private set; }
    public Visibility RingingVisibility => IsRinging ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ControlsVisibility => IsRinging ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        IsRinging = _item.Done;
        bool resting = !_item.Running && !_item.Done; // paused or ready
        Remaining = IsRinging ? "Time's up" : ClockFormat.Countdown(_item.Remaining);
        Status = IsRinging ? "Finished"
            : _item.Running ? $"Ends at {DateTime.Now + _item.Remaining:t}"
            : _item.Ready ? "Ready"
            : "Paused";

        // Starts full and drains to empty, like the donut tray icon; a ringing timer shows a full ring.
        Progress = IsRinging ? 100
            : _item.Duration <= TimeSpan.Zero ? 0
            : 100 * Math.Clamp(_item.Remaining / _item.Duration, 0, 1);
        RingOpacity = resting ? 0.55 : 1;
        CenterGlyph = resting && !_item.Ready ? "" : "";
        ToggleGlyph = _item.Running ? "" : ""; // Pause / Play
        ToggleName = _item.Running ? "Pause timer" : _item.Ready ? "Start timer" : "Resume timer";
        Description = IsRinging ? $"{Label}, time's up" : $"{Label}, {Remaining} left, {Status}";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

/// <summary>
/// The flyout's content: world clocks, a stopwatch, and timers. The stopwatch and timers live in
/// <see cref="ClockState"/>; this page only draws them, and its tick runs only while shown.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private const string SunGlyph = "";
    private const string MoonGlyph = "";
    private const string MoreGlyph = "";
    private const string CheckGlyph = "";
    private const double TabSlideDistance = 24;

    private readonly ObservableCollection<CityRow> _cities = [];
    private readonly ObservableCollection<TimerRow> _timers = [];
    private readonly ObservableCollection<LapRow> _laps = [];
    private readonly HashSet<int> _announcedRinging = [];
    private readonly DispatcherQueueTimer _tick;
    private readonly DispatcherQueueTimer _copiedTimer;
    private int _tab;
    private TimeSpan _shift;

    public FlyoutPage()
    {
        // Created first: controls raise change events while InitializeComponent builds them.
        _tick = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _tick.IsRepeating = true;
        _tick.Tick += (_, _) => Tick();

        InitializeComponent();

        _copiedTimer = DispatcherQueue.CreateTimer();
        _copiedTimer.Interval = TimeSpan.FromSeconds(1.5);
        _copiedTimer.IsRepeating = false;
        _copiedTimer.Tick += (_, _) => ResetCopiedConfirmation();

        CityList.ItemsSource = _cities;
        TimerList.ItemsSource = _timers;
        LapList.ItemsSource = _laps;

        foreach (string name in SettingsService.Cities)
        {
            if (CityCatalog.Find(name) is { } city)
            {
                _cities.Add(new CityRow(city.Name, city.Zone));
            }
        }

        RebuildLaps();
    }

    internal static string DayGlyphFor(DateTimeOffset local) => ClockFormat.IsDaytime(local) ? SunGlyph : MoonGlyph;

    /// <summary>The flyout opened: start drawing live time.</summary>
    public void OnShown()
    {
        PinButton.IsChecked = App.Current.IsFlyoutPinned;

        if (ClockState.PendingTab is { } tab)
        {
            ClockState.PendingTab = null;
            Tabs.SelectedItem = Tabs.Items[tab];
        }

        SyncTimers();
        RebuildLaps();
        Tick();
        _tick.Start();
    }

    /// <summary>The flyout hid: stop ticking, and drop the timer cards so a ringing one stops animating.</summary>
    public void OnHidden()
    {
        _tick.Stop();
        ResetCopiedConfirmation();
        _timers.Clear();
        _announcedRinging.Clear();
    }

    public void Dispose() => OnHidden();

    private void Tick()
    {
        // The stopwatch wants hundredths; everything else only needs whole seconds.
        _tick.Interval = ClockState.StopwatchRunning && _tab == 1
            ? TimeSpan.FromMilliseconds(50)
            : TimeSpan.FromMilliseconds(250);

        TickClocks();
        TickStopwatch();
        TickTimers();
    }

    private void TickClocks()
    {
        DateTimeOffset instant = DateTimeOffset.Now + _shift;
        DateTimeOffset local = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.Local);
        CultureInfo culture = CultureInfo.CurrentCulture;
        LocalTimeText.Text = local.ToString("t", culture);
        LocalCaption.Text = $"Local · {local.ToString("dddd", culture)}, {local.ToString("m", culture)}";
        LocalDayIcon.Glyph = DayGlyphFor(local);

        bool shifted = _shift != TimeSpan.Zero;
        ShiftText.Text = ClockFormat.Shift(_shift);
        ShiftText.Visibility = shifted ? Visibility.Visible : Visibility.Collapsed;
        BackToNowButton.Visibility = ShiftText.Visibility;

        foreach (CityRow row in _cities)
        {
            row.Update(instant);
        }

        CitiesEmptyText.Visibility = _cities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TickStopwatch()
    {
        bool running = ClockState.StopwatchRunning;
        TimeSpan elapsed = ClockState.StopwatchElapsed;
        (StopwatchWhole.Text, StopwatchFraction.Text) = ClockFormat.StopwatchParts(elapsed);
        AutomationProperties.SetName(StopwatchDisplay, $"Elapsed {ClockFormat.Stopwatch(elapsed)}");

        StopwatchStatus.Text = running ? "Running" : elapsed > TimeSpan.Zero ? "Paused" : "Ready";
        StopwatchDot.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        string label = running ? "Pause" : elapsed > TimeSpan.Zero ? "Resume" : "Start";
        StopwatchStartLabel.Text = label;
        StopwatchStartIcon.Glyph = running ? "" : "";
        AutomationProperties.SetName(StopwatchStartButton, label);
        StopwatchLapButton.IsEnabled = running;
        StopwatchResetButton.IsEnabled = elapsed > TimeSpan.Zero;
    }

    private void TickTimers()
    {
        foreach (TimerRow row in _timers)
        {
            row.Refresh();
            if (!row.IsRinging)
            {
                _announcedRinging.Remove(row.Id);
            }
            else if (_announcedRinging.Add(row.Id))
            {
                // The animation is visual only; tell screen readers too.
                Announce(TimerList, $"{row.Label} is done", "TimerDone");
            }
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int index = sender.Items.IndexOf(sender.SelectedItem);
        if (index < 0 || index == _tab || ClocksPanel is null)
        {
            return;
        }

        UIElement[] panels = [ClocksPanel, StopwatchPanel, TimersPanel];
        for (int i = 0; i < panels.Length; i++)
        {
            panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        // Slide in from the side the tab sits on, so the content follows the selection.
        Motion.SlideIn(panels[index], index > _tab ? TabSlideDistance : -TabSlideDistance);
        _tab = index;
        Tick();
    }

    // Clocks
    private void ShiftSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _shift = TimeSpan.FromMinutes(e.NewValue);
        Tick();
    }

    private void BackToNow_Click(object sender, RoutedEventArgs e) => ShiftSlider.Value = 0;

    private List<string> AvailableCities(string query)
    {
        HashSet<string> used = _cities.Select(c => c.Name).ToHashSet();
        CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace; // "sao" finds "São Paulo"
        return CityCatalog.All
            .Select(c => c.Name)
            .Where(n => !used.Contains(n) && (query.Length == 0 || compare.IndexOf(n, query.Trim(), Loose) >= 0))
            .ToList();
    }

    private void CitySearch_GotFocus(object sender, RoutedEventArgs e)
    {
        // Show the whole list on focus, so there's something to pick before typing.
        CitySearch.ItemsSource = AvailableCities(CitySearch.Text);
        CitySearch.IsSuggestionListOpen = true;
    }

    private void CitySearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = AvailableCities(sender.Text);
        }
    }

    private void CitySearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string? name = args.ChosenSuggestion as string ?? AvailableCities(args.QueryText).FirstOrDefault();
        if (name is null || CityCatalog.Find(name) is not { } city)
        {
            return;
        }

        _cities.Add(new CityRow(city.Name, city.Zone));
        SaveCities();
        sender.Text = "";
        sender.IsSuggestionListOpen = false;
        Tick();
    }

    private void CityList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not CityRow row)
        {
            return;
        }

        if (args.ItemContainer.ContentTemplateRoot is SwipeControl swipe)
        {
            // Swipe the row sideways (touch or pen) to reveal Delete.
            SwipeItem delete = new()
            {
                Text = "Remove",
                IconSource = new FontIconSource { Glyph = "" },
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Firebrick),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            };
            delete.Invoked += (_, _) => RemoveCity(row);
            SwipeItems items = new() { Mode = SwipeMode.Execute };
            items.Add(delete);
            swipe.RightItems = items;
        }

        args.ItemContainer.ContextFlyout = CityMenu(row);
    }

    private void CityMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CityRow row } button)
        {
            CityMenu(row).ShowAt(button);
        }
    }

    /// <summary>The same actions for right-click and the row's "more" button; drag and swipe are shortcuts for them.</summary>
    private MenuFlyout CityMenu(CityRow row)
    {
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
            up.IsEnabled = index > 0;
            down.IsEnabled = index < _cities.Count - 1;
        };
        menu.Items.Add(up);
        menu.Items.Add(down);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);
        return menu;
    }

    private void CityList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => SaveCities();

    private void MoveCity(CityRow row, int delta)
    {
        int from = _cities.IndexOf(row);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= _cities.Count)
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
        Tick();
    }

    private void SaveCities() => SettingsService.Cities = _cities.Select(c => c.Name).ToArray();

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

    private void StopwatchMenu_Opening(object sender, object e) => CopyLapsItem.IsEnabled = ClockState.Laps.Count > 0;

    private void CopyLaps_Click(object sender, RoutedEventArgs e)
    {
        DataPackage package = new();
        package.SetText(LapStats.ToTable(ClockState.Laps));
        Clipboard.SetContent(package);
        Clipboard.Flush(); // Keep the laps on the clipboard after the app exits.

        // Confirm in place: the "more" button briefly becomes a check mark.
        StopwatchMoreIcon.Glyph = CheckGlyph;
        ToolTipService.SetToolTip(StopwatchMoreButton, "Laps copied");
        _copiedTimer.Stop();
        _copiedTimer.Start();
        Announce(StopwatchMoreButton, "Laps copied", "LapsCopied");
    }

    private void ResetCopiedConfirmation()
    {
        _copiedTimer.Stop();
        StopwatchMoreIcon.Glyph = MoreGlyph;
        ToolTipService.SetToolTip(StopwatchMoreButton, "More options");
    }

    private static void Announce(UIElement source, string text, string id)
    {
        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(source)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(source);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent, text, id);
    }

    private void RebuildLaps()
    {
        _laps.Clear();
        foreach (Lap lap in LapStats.Build(ClockState.Laps))
        {
            _laps.Add(new LapRow(lap));
        }

        bool any = _laps.Count > 0;
        LapHeader.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        LapsEmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

    // Timers
    private void PresetTimer_Click(object sender, RoutedEventArgs e)
    {
        int seconds = int.Parse((string)((FrameworkElement)sender).Tag, CultureInfo.InvariantCulture);
        ClockState.AddTimer(TimeSpan.FromSeconds(seconds));
        AfterTimersChanged();
    }

    private TimeSpan CustomDuration()
    {
        static double Read(NumberBox box) => double.IsNaN(box.Value) ? 0 : box.Value;
        return TimeSpan.FromSeconds((Read(HoursBox) * 3600) + (Read(MinutesBox) * 60) + Read(SecondsBox));
    }

    // Start lights up only once there's a time to count down.
    private void DurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        CustomStartButton.IsEnabled = CustomDuration() > TimeSpan.Zero;

    private void DurationBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && CustomStartButton.IsEnabled)
        {
            StartCustomTimer();
        }
    }

    private void CustomTimer_Click(object sender, RoutedEventArgs e) => StartCustomTimer();

    private void StartCustomTimer()
    {
        TimeSpan duration = CustomDuration();
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        ClockState.AddTimer(duration);
        HoursBox.Value = MinutesBox.Value = SecondsBox.Value = double.NaN;
        AfterTimersChanged();
    }

    private void ToggleTimer_Click(object sender, RoutedEventArgs e)
    {
        ClockState.ToggleTimer((int)((FrameworkElement)sender).Tag);
        AfterTimersChanged();
    }

    private void DismissTimer_Click(object sender, RoutedEventArgs e)
    {
        ClockState.DismissTimer((int)((FrameworkElement)sender).Tag);
        AfterTimersChanged();
    }

    private void RemoveTimer_Click(object sender, RoutedEventArgs e)
    {
        ClockState.RemoveTimer((int)((FrameworkElement)sender).Tag);
        AfterTimersChanged();
    }

    private void AfterTimersChanged()
    {
        SyncTimers();
        Tick();
    }

    /// <summary>Adds and removes cards to match the timers, leaving existing cards in place so nothing re-animates.</summary>
    private void SyncTimers()
    {
        IReadOnlyList<TimerItem> items = ClockState.Timers;
        for (int i = _timers.Count - 1; i >= 0; i--)
        {
            if (!items.Any(t => t.Id == _timers[i].Id))
            {
                _timers.RemoveAt(i);
            }
        }

        foreach (TimerItem item in items)
        {
            if (!_timers.Any(r => r.Id == item.Id))
            {
                _timers.Add(new TimerRow(item));
            }
        }

        TimersEmptyText.Visibility = _timers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PinButton_Toggled(object sender, RoutedEventArgs e)
    {
        bool pinned = PinButton.IsChecked == true;
        App.Current.IsFlyoutPinned = pinned;
        PinIcon.Glyph = pinned ? "" : ""; // Pinned / Pin
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
}

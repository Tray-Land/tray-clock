using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;

namespace TrayClock.Services;

/// <summary>
/// Turns the tray into live readouts while something is running: one icon per running timer (a
/// draining donut) and one for a running stopwatch (a ticking hand). The primary icon takes the
/// first activity and extra icons are created and removed as more start and stop. This is background
/// work the tray icons themselves display: one one-shot dispatcher timer aimed at the soonest visible
/// change (every <see cref="StopwatchStepSeconds"/> seconds for the stopwatch, one of
/// <see cref="TimerIcon.Segments"/> steps for a timer), stopped as soon as nothing is running. Owns the
/// generated icon handles; the shell copies the image on update, so the previous handle is freed
/// after each switch.
/// </summary>
internal sealed class ActivityTrayIcon(
    TrayIcon primary,
    DispatcherQueue dispatcher,
    Action restoreAppIcon,
    Func<uint, TrayIcon> createExtra,
    Action beforeIconCountChange)
{
    /// <summary>How long the stopwatch hand rests between ticks.</summary>
    public const int StopwatchStepSeconds = 3;

    private const uint IconFormatVersion = 0x00030000; // Required by CreateIconFromResourceEx.

    // Shortest rest between redraws, so a very short timer doesn't redraw the icon in a tight loop.
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

    private sealed class Slot(TrayIcon icon, bool isPrimary)
    {
        public TrayIcon Icon { get; } = icon;
        public bool IsPrimary { get; } = isPrimary;

        /// <summary>The tab for what this icon is showing, so a click opens the matching page.</summary>
        public ClockTab Tab { get; set; }
        public HICON Handle;
    }

    private readonly DispatcherQueueTimer _timer = dispatcher.CreateTimer();
    private readonly List<Slot> _slots = [new Slot(primary, true)];
    private bool _active;

    /// <summary>True while generated icons are showing instead of the app icon.</summary>
    public bool IsActive => _active;

    /// <summary>Re-reads what is running and shows a matching icon for each, or puts the app icon back.</summary>
    public void Update()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;

        List<TimerItem> timers = [.. ClockState.RunningTimers];
        int count = timers.Count + (ClockState.StopwatchRunning ? 1 : 0);
        if (count == 0)
        {
            if (_active)
            {
                _active = false;
                restoreAppIcon();
                Release(_slots[0]);
            }

            if (_slots.Count > 1)
            {
                beforeIconCountChange();
            }

            TrimSlots(1);
            return;
        }

        if (_slots.Count != count)
        {
            beforeIconCountChange();
        }

        while (_slots.Count < count)
        {
            _slots.Add(new Slot(createExtra((uint)_slots.Count + 1), false));
        }

        TrimSlots(count);

        TimeSpan soonest = TimeSpan.MaxValue;
        for (int i = 0; i < timers.Count; i++)
        {
            TimerItem timer = timers[i];
            _slots[i].Tab = ClockTab.Timers;
            Show(_slots[i], TimerIcon.Create(IconSize, TimerIcon.Fraction(timer.Remaining, timer.Duration), Color), timer.Label);
            soonest = Min(soonest, TimerIcon.UntilNextSegment(timer.Remaining, timer.Duration));
        }

        if (ClockState.StopwatchRunning)
        {
            double degrees = StopwatchIcon.HandDegrees(ClockState.StopwatchElapsed, StopwatchStepSeconds);
            _slots[count - 1].Tab = ClockTab.Stopwatch;
            Show(_slots[count - 1], StopwatchIcon.Create(IconSize, degrees, Color), "Stopwatch");
            double intoStep = ClockState.StopwatchElapsed.TotalSeconds % StopwatchStepSeconds;
            soonest = Min(soonest, TimeSpan.FromSeconds(StopwatchStepSeconds - intoStep));
        }

        _active = true;
        Schedule(soonest);
    }

    /// <summary>
    /// The tab a click on <paramref name="icon"/> should open: the timers for a timer icon, the stopwatch
    /// for the stopwatch icon, and the best page for whatever is left when the app icon is showing.
    /// </summary>
    public ClockTab TabFor(TrayIcon icon)
    {
        Slot? slot = _active ? _slots.Find(s => s.Icon == icon) : null;
        return slot?.Tab ?? ClockState.IdleTab;
    }

    public void Dispose()
    {
        _timer.Stop();
        _active = false;
        Release(_slots[0]);
        TrimSlots(1);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    // Removes extra icons beyond the first <paramref name="keep"/> slots.
    private void TrimSlots(int keep)
    {
        while (_slots.Count > keep)
        {
            Slot slot = _slots[^1];
            _slots.RemoveAt(_slots.Count - 1);
            slot.Icon.IsVisible = false;
            slot.Icon.Dispose();
            Release(slot);
        }
    }

    private static int IconSize =>
        PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());

    // A white glyph disappears on a light taskbar, which follows the Windows theme, not the app theme.
    private static uint Color => SystemThemeService.IsLight ? 0xFF000000 : 0xFFFFFFFF;

    private void OnTick(DispatcherQueueTimer sender, object args) => Update();

    // Aim just past the boundary so the redraw lands on the new step, not the old one.
    private void Schedule(TimeSpan untilChange)
    {
        TimeSpan interval = untilChange + TimeSpan.FromMilliseconds(20);
        _timer.Interval = interval < MinInterval ? MinInterval : interval;
        _timer.IsRepeating = false;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void Show(Slot slot, byte[] ico, string label)
    {
        int size = ico[6];
        HICON icon;
        nint handle;
        unsafe
        {
            fixed (byte* bits = ico)
            {
                icon = PInvoke.CreateIconFromResourceEx(
                    bits + IcoEncoder.ImageOffset,
                    (uint)(ico.Length - IcoEncoder.ImageOffset),
                    true,
                    IconFormatVersion,
                    size,
                    size,
                    IMAGE_FLAGS.LR_DEFAULTCOLOR);
            }

            handle = (nint)icon.Value;
        }

        if (handle == 0)
        {
            return;
        }

        slot.Icon.SetIcon(Win32Interop.GetIconIdFromIcon(handle));
        if (!slot.IsPrimary)
        {
            slot.Icon.Tooltip = $"{App.DisplayName}\n{label}";
        }

        Release(slot);
        slot.Handle = icon;
    }

    private static void Release(Slot slot)
    {
        if (!slot.Handle.IsNull)
        {
            PInvoke.DestroyIcon(slot.Handle);
            slot.Handle = default;
        }
    }
}

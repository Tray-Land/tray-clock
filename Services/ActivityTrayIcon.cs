using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using WinUIEx;

namespace TrayClock.Services;

/// <summary>
/// Turns the tray icon into a live readout while something is running: a draining donut for the
/// timer that finishes first, otherwise a ticking stopwatch. This is background work the tray icon
/// itself displays: one one-shot dispatcher timer aimed at the next visible change (every
/// <see cref="StopwatchStepSeconds"/> seconds for the stopwatch, one of <see cref="TimerIcon.Segments"/>
/// steps for a timer), stopped as soon as nothing is running. Owns the generated icon handle; the
/// shell copies the image on update, so the previous handle is freed after each switch.
/// </summary>
internal sealed class ActivityTrayIcon(TrayIcon trayIcon, DispatcherQueue dispatcher, Action restoreAppIcon)
{
    /// <summary>How long the stopwatch hand rests between ticks.</summary>
    public const int StopwatchStepSeconds = 3;

    private const uint IconFormatVersion = 0x00030000; // Required by CreateIconFromResourceEx.

    // Shortest rest between redraws, so a very short timer doesn't redraw the icon in a tight loop.
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherQueueTimer _timer = dispatcher.CreateTimer();
    private HICON _icon;
    private bool _active;

    /// <summary>True while a generated icon is showing instead of the app icon.</summary>
    public bool IsActive => _active;

    /// <summary>Re-reads what is running and shows the matching icon, or puts the app icon back.</summary>
    public void Update()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;

        if (ClockState.NextTimer is { } timer)
        {
            Show(TimerIcon.Create(IconSize, TimerIcon.Fraction(timer.Remaining, timer.Duration), Color));
            Schedule(TimerIcon.UntilNextSegment(timer.Remaining, timer.Duration));
        }
        else if (ClockState.StopwatchRunning)
        {
            double degrees = StopwatchIcon.HandDegrees(ClockState.StopwatchElapsed, StopwatchStepSeconds);
            Show(StopwatchIcon.Create(IconSize, degrees, Color));
            double intoStep = ClockState.StopwatchElapsed.TotalSeconds % StopwatchStepSeconds;
            Schedule(TimeSpan.FromSeconds(StopwatchStepSeconds - intoStep));
        }
        else if (_active)
        {
            _active = false;
            restoreAppIcon();
            Release();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _active = false;
        Release();
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

    private void Show(byte[] ico)
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

        _active = true;
        trayIcon.SetIcon(Win32Interop.GetIconIdFromIcon(handle));
        Release();
        _icon = icon;
    }

    private void Release()
    {
        if (!_icon.IsNull)
        {
            PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
    }
}

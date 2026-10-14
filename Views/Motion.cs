using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace TrayClock.Views;

/// <summary>
/// Small looping animations for a timer that's going off (a glow that breathes, a bell that
/// shakes, a ring that throbs) and the slide between tabs. They run only while the element is
/// loaded, stop as soon as their property turns false, and are skipped when the user turned off
/// animation effects in Windows. Storyboards on Opacity and RenderTransform run on the compositor,
/// so a ringing card costs the UI thread nothing.
/// </summary>
/// <remarks>
/// A DependencyObject rather than a static class: XAML only finds attached properties on types it
/// has metadata for.
/// </remarks>
public sealed partial class Motion : DependencyObject
{
    private static readonly UISettings UiSettings = new();

    /// <summary>Settings > Accessibility > Visual effects > Animation effects.</summary>
    public static bool IsEnabled => UiSettings.AnimationsEnabled;

    /// <summary>Opacity breathes between faint and strong.</summary>
    public static readonly DependencyProperty PulseProperty = RegisterLoop("Pulse");

    /// <summary>A quick side-to-side shake, then a rest, like a ringing bell.</summary>
    public static readonly DependencyProperty WiggleProperty = RegisterLoop("Wiggle");

    /// <summary>Grows a little and settles back, over and over.</summary>
    public static readonly DependencyProperty ThrobProperty = RegisterLoop("Throb");

    private static readonly DependencyProperty ActiveStoryboardProperty = DependencyProperty.RegisterAttached(
        "ActiveStoryboard", typeof(Storyboard), typeof(Motion), new PropertyMetadata(null));

    private static readonly DependencyProperty IsHookedProperty = DependencyProperty.RegisterAttached(
        "IsHooked", typeof(bool), typeof(Motion), new PropertyMetadata(false));

    public static bool GetPulse(FrameworkElement element) => (bool)element.GetValue(PulseProperty);

    public static void SetPulse(FrameworkElement element, bool value) => element.SetValue(PulseProperty, value);

    public static bool GetWiggle(FrameworkElement element) => (bool)element.GetValue(WiggleProperty);

    public static void SetWiggle(FrameworkElement element, bool value) => element.SetValue(WiggleProperty, value);

    public static bool GetThrob(FrameworkElement element) => (bool)element.GetValue(ThrobProperty);

    public static void SetThrob(FrameworkElement element, bool value) => element.SetValue(ThrobProperty, value);

    /// <summary>Slides and fades an element in from <paramref name="fromX"/> pixels to the side.</summary>
    public static void SlideIn(UIElement element, double fromX)
    {
        if (!IsEnabled)
        {
            return;
        }

        CompositeTransform transform = EnsureTransform(element);
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        Storyboard storyboard = new();
        storyboard.Children.Add(Animate(transform, nameof(CompositeTransform.TranslateX), fromX, 0, 300, ease));
        storyboard.Children.Add(Animate(element, nameof(UIElement.Opacity), 0, 1, 200, null));
        storyboard.Begin();
    }

    private static DependencyProperty RegisterLoop(string name) => DependencyProperty.RegisterAttached(
        name, typeof(bool), typeof(Motion), new PropertyMetadata(false, OnLoopChanged));

    private static void OnLoopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (!(bool)element.GetValue(IsHookedProperty))
        {
            // Containers come and go with their items; stop when unloaded, resume if reloaded while still on.
            element.SetValue(IsHookedProperty, true);
            element.Loaded += (_, _) => Update(element);
            element.Unloaded += (_, _) => Stop(element);
        }

        Update(element);
    }

    private static void Update(FrameworkElement element)
    {
        Stop(element);

        // IsLoaded reads false for template elements that loaded before this hook existed; XamlRoot
        // is set whenever the element is in a live tree.
        if (element.XamlRoot is null)
        {
            return;
        }

        if (!IsEnabled)
        {
            return;
        }

        Storyboard? storyboard = GetPulse(element) ? PulseStoryboard(element)
            : GetWiggle(element) ? WiggleStoryboard(element)
            : GetThrob(element) ? ThrobStoryboard(element)
            : null;
        if (storyboard is null)
        {
            return;
        }

        storyboard.RepeatBehavior = RepeatBehavior.Forever;
        element.SetValue(ActiveStoryboardProperty, storyboard);
        storyboard.Begin();
    }

    private static void Stop(FrameworkElement element)
    {
        if (element.GetValue(ActiveStoryboardProperty) is Storyboard storyboard)
        {
            // Stop() returns every animated value to its resting state.
            storyboard.Stop();
            element.ClearValue(ActiveStoryboardProperty);
        }
    }

    private static Storyboard PulseStoryboard(UIElement element)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        DoubleAnimationUsingKeyFrames opacity = new();
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.12 });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(700), Value = 0.45, EasingFunction = ease });
        opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1400), Value = 0.12, EasingFunction = ease });
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, nameof(UIElement.Opacity));
        return new Storyboard { Children = { opacity } };
    }

    private static Storyboard WiggleStoryboard(UIElement element)
    {
        // Swing from the top, where a bell hangs, so the shake reads even at 18 px.
        CompositeTransform transform = EnsureTransform(element);
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.1);
        DoubleAnimationUsingKeyFrames rotate = new();
        double[] angles = [0, -24, 20, -16, 12, -8, 4, 0];
        for (int i = 0; i < angles.Length; i++)
        {
            rotate.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(i * 70), Value = angles[i] });
        }

        // Hold still for a beat so the shake reads as rings, not a blur.
        rotate.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1400), Value = 0 });
        Storyboard.SetTarget(rotate, transform);
        Storyboard.SetTargetProperty(rotate, nameof(CompositeTransform.Rotation));
        return new Storyboard { Children = { rotate } };
    }

    private static Storyboard ThrobStoryboard(UIElement element)
    {
        CompositeTransform transform = EnsureTransform(element);
        Storyboard storyboard = new();
        foreach (string property in new[] { nameof(CompositeTransform.ScaleX), nameof(CompositeTransform.ScaleY) })
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            DoubleAnimationUsingKeyFrames scale = new();
            scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1 });
            scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(700), Value = 1.1, EasingFunction = ease });
            scale.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(1400), Value = 1, EasingFunction = ease });
            Storyboard.SetTarget(scale, transform);
            Storyboard.SetTargetProperty(scale, property);
            storyboard.Children.Add(scale);
        }

        return storyboard;
    }

    private static DoubleAnimation Animate(DependencyObject target, string property, double from, double to, double ms, EasingFunctionBase? ease)
    {
        DoubleAnimation animation = new()
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(ms),
            EasingFunction = ease,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private static CompositeTransform EnsureTransform(UIElement element)
    {
        if (element.RenderTransform is not CompositeTransform transform)
        {
            transform = new CompositeTransform();
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }

        return transform;
    }
}

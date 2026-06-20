using System.Windows;
using System.Windows.Media.Animation;

namespace QuickNotes.Models;

public static class AnimationHelper
{
    public static bool Enabled { get; set; } = true;

    public static Duration Dur(double ms) => Enabled ? TimeSpan.FromMilliseconds(ms) : new Duration(TimeSpan.Zero);

    public static DoubleAnimation MakeAnimation(double to, double ms)
    {
        var a = new DoubleAnimation(to, Dur(ms));
        if (Enabled) a.EasingFunction = new QuadraticEase();
        return a;
    }

    public static DoubleAnimation MakeAnimation(double from, double to, double ms)
    {
        var a = new DoubleAnimation(from, to, Dur(ms));
        if (Enabled) a.EasingFunction = new QuadraticEase();
        return a;
    }
}

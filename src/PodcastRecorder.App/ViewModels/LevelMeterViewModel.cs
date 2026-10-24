using CommunityToolkit.Mvvm.ComponentModel;
using PodcastRecorder.Audio.Mixing;

namespace PodcastRecorder.App.ViewModels;

/// <summary>
/// Turns raw peaks into meter positions on a -60..0 dB scale, with a falling needle and a
/// clip light that stays on for a moment so it can be noticed.
/// </summary>
public partial class LevelMeterViewModel(PeakMeter meter) : ObservableObject
{
    private const double FloorDb = -60;
    private const double FallPerTick = 0.04;
    private const int ClipHoldTicks = 30;

    private int _clipTicks;

    [ObservableProperty]
    public partial double Left { get; set; }

    [ObservableProperty]
    public partial double Right { get; set; }

    [ObservableProperty]
    public partial bool IsClipping { get; set; }

    public void Refresh()
    {
        var level = meter.ReadAndReset();
        Left = Fall(Left, ToMeter(level.Left));
        Right = Fall(Right, ToMeter(level.Right));

        if (level.IsClipping) _clipTicks = ClipHoldTicks;
        else if (_clipTicks > 0) _clipTicks--;
        IsClipping = _clipTicks > 0;
    }

    private static double Fall(double previous, double next) => Math.Max(next, previous - FallPerTick);

    private static double ToMeter(float peak)
    {
        if (peak <= 0) return 0;
        double db = 20 * Math.Log10(peak);
        return Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
    }
}

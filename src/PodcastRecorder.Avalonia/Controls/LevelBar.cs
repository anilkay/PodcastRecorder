using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PodcastRecorder.App.Controls;

/// <summary>A plain level bar that fills upwards, the same in every theme and on every platform.</summary>
public sealed class LevelBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<LevelBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<LevelBar, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty = AvaloniaProperty.Register<LevelBar, IBrush?>(nameof(Track));

    static LevelBar() => AffectsRender<LevelBar>(ValueProperty, FillProperty, TrackProperty);

    /// <summary>0 to 1.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.DrawRectangle(Track, null, bounds, 2, 2);
        double height = bounds.Height * Math.Clamp(Value, 0, 1);
        if (height > 0) context.DrawRectangle(Fill, null, new Rect(0, bounds.Height - height, bounds.Width, height), 2, 2);
    }
}

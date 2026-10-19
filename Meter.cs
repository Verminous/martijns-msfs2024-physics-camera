using System;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace MsfsPhysicsCamera
{
    // A thin bar that fills from zero towards the value (from the minimum when zero is out of range), drawn
    // directly in OnRender: no template, cheap enough for a dozen meters updated every frame.
    public class Meter : FrameworkElement
    {
        private const double BAR_HEIGHT = 4.0;
        private const double ZERO_MARK_HEIGHT = 10.0;

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Meter),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(Meter),
            new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(Meter),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

        private Brush? track, fill, mark;

        protected override Size MeasureOverride(Size availableSize) => new Size(0, ZERO_MARK_HEIGHT);

        protected override void OnRender(DrawingContext dc)
        {
            track ??= TryFindResource("TrackBrush") as Brush ?? Brushes.DimGray;
            fill ??= TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
            mark ??= TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray;

            double w = ActualWidth, h = ActualHeight;
            double range = Maximum - Minimum;
            if (w <= 0 || range <= 0) return;

            double y = (h - BAR_HEIGHT) / 2.0;
            double r = BAR_HEIGHT / 2.0;
            dc.DrawRoundedRectangle(track, null, new Rect(0, y, w, BAR_HEIGHT), r, r);

            double origin = Math.Clamp(0.0, Minimum, Maximum);
            double value = double.IsFinite(Value) ? Math.Clamp(Value, Minimum, Maximum) : origin;
            double x0 = (origin - Minimum) / range * w;
            double x1 = (value - Minimum) / range * w;
            double left = Math.Min(x0, x1), width = Math.Abs(x1 - x0);
            if (width > 0.5)
            {
                dc.DrawRoundedRectangle(fill, null, new Rect(left, y, width, BAR_HEIGHT), r, r);
            }

            if (Minimum < 0 && Maximum > 0)
            {
                dc.DrawRectangle(mark, null, new Rect(Math.Round(x0) - 0.5, (h - ZERO_MARK_HEIGHT) / 2.0, 1, ZERO_MARK_HEIGHT));
            }
        }
    }
}

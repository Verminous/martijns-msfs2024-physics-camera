using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;

namespace MsfsPhysicsCamera
{
    public partial class RangeSlider : UserControl
    {
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register("Minimum", typeof(double), typeof(RangeSlider), new PropertyMetadata(0.0, OnValuesChanged));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register("Maximum", typeof(double), typeof(RangeSlider), new PropertyMetadata(1.0, OnValuesChanged));
        public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register("LowerValue", typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuesChanged));
        public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register("UpperValue", typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuesChanged));

        public event EventHandler<double>? TestClampRequested;
        public event EventHandler? TestClampEnded;

        public double Minimum { get { return (double)GetValue(MinimumProperty); } set { SetValue(MinimumProperty, value); } }
        public double Maximum { get { return (double)GetValue(MaximumProperty); } set { SetValue(MaximumProperty, value); } }
        public double LowerValue { get { return (double)GetValue(LowerValueProperty); } set { SetValue(LowerValueProperty, value); } }
        public double UpperValue { get { return (double)GetValue(UpperValueProperty); } set { SetValue(UpperValueProperty, value); } }

        public RangeSlider()
        {
            InitializeComponent();
        }

        private bool _isUpdating = false;

        private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RangeSlider slider)
            {
                if (slider._isUpdating) return;
                slider._isUpdating = true;

                double min = slider.Minimum;
                double max = slider.Maximum;
                double lower = slider.LowerValue;
                double upper = slider.UpperValue;

                bool changed = false;

                if (lower < min) { lower = min; changed = true; }
                if (upper > max) { upper = max; changed = true; }
                if (lower > upper) 
                { 
                    if (e.Property == LowerValueProperty) { upper = lower; }
                    else { lower = upper; }
                    changed = true; 
                }

                if (changed)
                {
                    slider.LowerValue = lower;
                    slider.UpperValue = upper;
                }

                slider.UpdateUI();
                slider._isUpdating = false;
            }
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (SliderCanvas == null || SliderCanvas.ActualWidth == 0) return;

            double range = Maximum - Minimum;
            if (range <= 0) return;

            double lowerPercent = (LowerValue - Minimum) / range;
            double upperPercent = (UpperValue - Minimum) / range;

            double lowerX = lowerPercent * SliderCanvas.ActualWidth;
            double upperX = upperPercent * SliderCanvas.ActualWidth;

            Canvas.SetLeft(MinThumb, lowerX - MinThumb.Width / 2);
            Canvas.SetLeft(MaxThumb, upperX - MaxThumb.Width / 2);

            ActiveTrack.Margin = new Thickness(lowerX + 5, 0, 0, 0);
            ActiveTrack.Width = Math.Max(0, upperX - lowerX);
        }

        private void MinThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double range = Maximum - Minimum;
            double change = (e.HorizontalChange / SliderCanvas.ActualWidth) * range;
            double newValue = LowerValue + change;
            newValue = Math.Max(Minimum, Math.Min(newValue, UpperValue));
            LowerValue = newValue;
            TestClampRequested?.Invoke(this, LowerValue);
        }

        private void MaxThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double range = Maximum - Minimum;
            double change = (e.HorizontalChange / SliderCanvas.ActualWidth) * range;
            double newValue = UpperValue + change;
            newValue = Math.Max(LowerValue, Math.Min(newValue, Maximum));
            UpperValue = newValue;
            TestClampRequested?.Invoke(this, UpperValue);
        }

        private void MinThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            TestClampRequested?.Invoke(this, LowerValue);
        }

        private void MaxThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            TestClampRequested?.Invoke(this, UpperValue);
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            TestClampEnded?.Invoke(this, EventArgs.Empty);
        }
    }
}
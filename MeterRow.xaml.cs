using System.Globalization;
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace MsfsPhysicsCamera
{
    // One live reading: label, a centre-zero bar and the value, e.g. Format "{0:F2} ft/s²".
    public partial class MeterRow : UserControl
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(MeterRow), new PropertyMetadata(""));
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(MeterRow), new PropertyMetadata(0.0, OnTextChanged));
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(MeterRow), new PropertyMetadata(-1.0));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(MeterRow), new PropertyMetadata(1.0));
        public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(nameof(Format), typeof(string), typeof(MeterRow), new PropertyMetadata("{0:F2}", OnTextChanged));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

        public MeterRow()
        {
            InitializeComponent();
            UpdateText();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MeterRow)d).UpdateText();

        private void UpdateText()
        {
            if (ValueText != null) ValueText.Text = string.Format(CultureInfo.CurrentCulture, Format, Value);
        }
    }
}

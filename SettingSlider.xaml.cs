using System.Globalization;
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace MsfsPhysicsCamera
{
    // A setting: label and value on one line, the slider below. Value binds two-way to a window property.
    public partial class SettingSlider : UserControl
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingSlider), new PropertyMetadata(""));
        public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(SettingSlider), new PropertyMetadata(null));
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SettingSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SettingSlider), new PropertyMetadata(0.0));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SettingSlider), new PropertyMetadata(1.0));
        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(nameof(Step), typeof(double), typeof(SettingSlider), new PropertyMetadata(0.1));
        public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(nameof(Format), typeof(string), typeof(SettingSlider), new PropertyMetadata("{0:F2}", OnTextChanged));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string? Hint { get => (string?)GetValue(HintProperty); set => SetValue(HintProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
        public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

        public SettingSlider()
        {
            InitializeComponent();
            UpdateText();
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SettingSlider)d).UpdateText();

        private void UpdateText()
        {
            if (ValueText != null) ValueText.Text = string.Format(CultureInfo.CurrentCulture, Format, Value);
        }
    }
}

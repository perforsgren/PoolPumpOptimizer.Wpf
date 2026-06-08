using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PoolPumpOptimizer.Wpf.Views;

public partial class NumberBox : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(NumberBox),
            new PropertyMetadata("Value"));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(
            nameof(Unit),
            typeof(string),
            typeof(NumberBox),
            new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(decimal),
            typeof(NumberBox),
            new FrameworkPropertyMetadata(
                0m,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(
            nameof(Minimum),
            typeof(decimal),
            typeof(NumberBox),
            new PropertyMetadata(0m, OnLimitChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(decimal),
            typeof(NumberBox),
            new PropertyMetadata(999999m, OnLimitChanged));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(
            nameof(Step),
            typeof(decimal),
            typeof(NumberBox),
            new PropertyMetadata(1m));

    public static readonly DependencyProperty DecimalsProperty =
        DependencyProperty.Register(
            nameof(Decimals),
            typeof(int),
            typeof(NumberBox),
            new PropertyMetadata(0, OnValueChanged));

    private bool _isUpdatingText;

    public NumberBox()
    {
        InitializeComponent();

        Loaded += (_, _) => UpdateTextFromValue();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public decimal Value
    {
        get => (decimal)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Clamp(value));
    }

    public decimal Minimum
    {
        get => (decimal)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public decimal Maximum
    {
        get => (decimal)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public decimal Step
    {
        get => (decimal)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    private static void OnValueChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var control = (NumberBox)dependencyObject;

        var clamped = control.Clamp(control.Value);

        if (clamped != control.Value)
        {
            control.Value = clamped;
            return;
        }

        control.UpdateTextFromValue();
    }

    private static void OnLimitChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var control = (NumberBox)dependencyObject;
        control.Value = control.Clamp(control.Value);
        control.UpdateTextFromValue();
    }

    private void MinusButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Value = Clamp(Value - Step);
        CommitText();
    }

    private void PlusButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Value = Clamp(Value + Step);
        CommitText();
    }

    private void ValueTextBox_LostFocus(
        object sender,
        RoutedEventArgs e)
    {
        CommitText();
    }

    private void ValueTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitText();
        e.Handled = true;
    }

    private void ValueTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_isUpdatingText)
            return;

        if (TryParse(ValueTextBox.Text, out var parsed))
        {
            Value = Clamp(parsed);
        }
    }

    private void CommitText()
    {
        if (TryParse(ValueTextBox.Text, out var parsed))
        {
            Value = Clamp(parsed);
        }

        UpdateTextFromValue();
    }

    private void UpdateTextFromValue()
    {
        if (!IsLoaded)
            return;

        _isUpdatingText = true;

        var format = Decimals <= 0
            ? "0"
            : "0." + new string('0', Decimals);

        ValueTextBox.Text = Value.ToString(format, CultureInfo.CurrentCulture);

        _isUpdatingText = false;
    }

    private bool TryParse(string text, out decimal value)
    {
        text = text.Trim();

        return decimal.TryParse(
            text,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out value);
    }

    private decimal Clamp(decimal value)
    {
        if (value < Minimum)
            return Minimum;

        if (value > Maximum)
            return Maximum;

        return value;
    }
}
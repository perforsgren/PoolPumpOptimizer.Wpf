using System.Windows;
using System.Windows.Controls;

namespace PoolPumpOptimizer.Wpf.Views;

public partial class TokenBox : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(TokenBox),
            new PropertyMetadata("Token"));

    public static readonly DependencyProperty TokenProperty =
        DependencyProperty.Register(
            nameof(Token),
            typeof(string),
            typeof(TokenBox),
            new FrameworkPropertyMetadata(
                "",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTokenChanged));

    private bool _isVisible;
    private bool _isUpdating;

    public TokenBox()
    {
        InitializeComponent();

        Loaded += (_, _) => Refresh();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Token
    {
        get => (string)GetValue(TokenProperty);
        set => SetValue(TokenProperty, value);
    }

    private static void OnTokenChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var control = (TokenBox)dependencyObject;
        control.Refresh();
    }

    private void ToggleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        _isVisible = !_isVisible;
        Refresh();

        if (_isVisible)
        {
            VisibleTextBox.Focus();
            VisibleTextBox.CaretIndex = VisibleTextBox.Text.Length;
        }
    }

    private void VisibleTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_isUpdating)
            return;

        Token = VisibleTextBox.Text;
        RefreshMaskedText();
    }

    private void Refresh()
    {
        if (!IsLoaded)
            return;

        _isUpdating = true;

        VisibleTextBox.Text = Token ?? "";

        VisibleTextBox.Visibility = _isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

        MaskedTextBox.Visibility = _isVisible
            ? Visibility.Collapsed
            : Visibility.Visible;

        ToggleButton.Content = _isVisible
            ? "Dölj"
            : "Visa";

        RefreshMaskedText();

        _isUpdating = false;
    }

    private void RefreshMaskedText()
    {
        var token = Token ?? "";

        if (string.IsNullOrWhiteSpace(token))
        {
            MaskedTextBox.Text = "";
            return;
        }

        if (token.Length <= 10)
        {
            MaskedTextBox.Text = new string('•', token.Length);
            return;
        }

        MaskedTextBox.Text =
            token[..5] +
            new string('•', Math.Min(24, token.Length - 10)) +
            token[^5..];
    }
}
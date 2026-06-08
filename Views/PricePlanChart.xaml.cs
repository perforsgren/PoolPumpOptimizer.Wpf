using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PoolPumpOptimizer.Wpf.Models;

namespace PoolPumpOptimizer.Wpf.Views;

public partial class PricePlanChart : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(PricePlanChart),
            new PropertyMetadata(null, OnItemsSourceChanged));

    private readonly List<PlannedSlot> _slots = new();
    private INotifyCollectionChanged? _currentCollection;
    private Line? _hoverLine;
    private Ellipse? _hoverDot;

    private const double LeftMargin = 58;
    private const double RightMargin = 22;
    private const double TopMargin = 26;
    private const double BottomMargin = 42;

    public PricePlanChart()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var chart = (PricePlanChart)dependencyObject;

        if (chart._currentCollection != null)
            chart._currentCollection.CollectionChanged -= chart.ItemsSource_CollectionChanged;

        chart._currentCollection = args.NewValue as INotifyCollectionChanged;

        if (chart._currentCollection != null)
            chart._currentCollection.CollectionChanged += chart.ItemsSource_CollectionChanged;

        chart.ReloadItems();
        chart.Redraw();
    }

    private void ItemsSource_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        ReloadItems();
        Redraw();
    }

    private void Root_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        Redraw();
    }

    private void Root_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_slots.Count == 0)
        {
            HideTooltip();
            return;
        }

        var position = e.GetPosition(Root);

        var chartWidth = ActualWidth - LeftMargin - RightMargin;
        var chartHeight = ActualHeight - TopMargin - BottomMargin;

        if (chartWidth <= 0 || chartHeight <= 0)
        {
            HideTooltip();
            return;
        }

        var x = position.X;

        if (x < LeftMargin || x > ActualWidth - RightMargin)
        {
            HideTooltip();
            return;
        }

        var index = XToIndex(x);

        if (index < 0 || index >= _slots.Count)
        {
            HideTooltip();
            return;
        }

        var slot = _slots[index];

        var point = GetPoint(index, slot.PriceSekPerKwh);

        ShowHover(point, slot);
    }

    private void Root_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        HideTooltip();
    }

    private void ReloadItems()
    {
        _slots.Clear();

        if (ItemsSource == null)
            return;

        foreach (var item in ItemsSource)
        {
            if (item is PlannedSlot slot)
                _slots.Add(slot);
        }

        _slots.Sort((a, b) => a.StartsAt.CompareTo(b.StartsAt));
    }

    private void Redraw()
    {
        ChartCanvas.Children.Clear();
        HideTooltip();

        if (ActualWidth <= 100 || ActualHeight <= 100)
            return;

        if (_slots.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        DrawBackground();
        DrawGrid();
        DrawOnBands();
        DrawPriceLine();
        DrawAxes();
        DrawDaySeparators();
    }

    private void DrawEmptyState()
    {
        var text = new TextBlock
        {
            Text = "Ingen plan att visa. Hämta priser och optimera.",
            Foreground = new SolidColorBrush(Color.FromRgb(170, 179, 194)),
            FontSize = 14
        };

        Canvas.SetLeft(text, 24);
        Canvas.SetTop(text, 24);

        ChartCanvas.Children.Add(text);
    }

    private void DrawBackground()
    {
        var rect = new Rectangle
        {
            Width = Math.Max(0, ActualWidth),
            Height = Math.Max(0, ActualHeight),
            Fill = new SolidColorBrush(Color.FromRgb(30, 33, 40))
        };

        ChartCanvas.Children.Add(rect);
    }

    private void DrawGrid()
    {
        var maxPrice = GetMaxPrice();
        var minPrice = GetMinPrice();

        var ySteps = 5;

        for (var i = 0; i <= ySteps; i++)
        {
            var fraction = i / (double)ySteps;
            var y = TopMargin + fraction * GetChartHeight();

            var line = new Line
            {
                X1 = LeftMargin,
                X2 = ActualWidth - RightMargin,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(52, 58, 70)),
                StrokeThickness = 1
            };

            ChartCanvas.Children.Add(line);

            var value = maxPrice - (decimal)fraction * (maxPrice - minPrice);

            var label = new TextBlock
            {
                Text = value.ToString("0.00"),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 179, 194)),
                FontSize = 11
            };

            Canvas.SetLeft(label, 8);
            Canvas.SetTop(label, y - 8);

            ChartCanvas.Children.Add(label);
        }

        var xStep = Math.Max(4, _slots.Count / 12);

        for (var i = 0; i < _slots.Count; i += xStep)
        {
            var x = IndexToX(i);

            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = TopMargin,
                Y2 = ActualHeight - BottomMargin,
                Stroke = new SolidColorBrush(Color.FromRgb(42, 48, 60)),
                StrokeThickness = 1
            };

            ChartCanvas.Children.Add(line);
        }
    }

    private void DrawOnBands()
    {
        var startIndex = -1;

        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Run && startIndex < 0)
            {
                startIndex = i;
            }

            var isEnd =
                startIndex >= 0 &&
                (!_slots[i].Run || i == _slots.Count - 1);

            if (!isEnd)
                continue;

            var endIndex = _slots[i].Run && i == _slots.Count - 1
                ? i
                : i - 1;

            DrawOnBand(startIndex, endIndex);

            startIndex = -1;
        }
    }

    private void DrawOnBand(int startIndex, int endIndex)
    {
        if (startIndex < 0 || endIndex < startIndex)
            return;

        var x1 = IndexToX(startIndex);
        var x2 = IndexToX(endIndex);

        var slotWidth = GetSlotWidth();

        var rect = new Rectangle
        {
            Width = Math.Max(4, x2 - x1 + slotWidth),
            Height = GetChartHeight(),
            RadiusX = 6,
            RadiusY = 6,
            Fill = new SolidColorBrush(Color.FromArgb(48, 55, 214, 122)),
            Stroke = new SolidColorBrush(Color.FromArgb(105, 55, 214, 122)),
            StrokeThickness = 1
        };

        Canvas.SetLeft(rect, x1 - slotWidth / 2);
        Canvas.SetTop(rect, TopMargin);

        ChartCanvas.Children.Add(rect);
    }

    private void DrawPriceLine()
    {
        if (_slots.Count < 2)
            return;

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                var point = GetPoint(i, _slots[i].PriceSekPerKwh);

                if (i == 0)
                {
                    context.BeginFigure(point, false, false);
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();

        var path = new Path
        {
            Data = geometry,
            Stroke = new SolidColorBrush(Color.FromRgb(0, 163, 255)),
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        ChartCanvas.Children.Add(path);
    }

    private void DrawAxes()
    {
        var axisBrush = new SolidColorBrush(Color.FromRgb(90, 100, 118));

        var yAxis = new Line
        {
            X1 = LeftMargin,
            X2 = LeftMargin,
            Y1 = TopMargin,
            Y2 = ActualHeight - BottomMargin,
            Stroke = axisBrush,
            StrokeThickness = 1
        };

        var xAxis = new Line
        {
            X1 = LeftMargin,
            X2 = ActualWidth - RightMargin,
            Y1 = ActualHeight - BottomMargin,
            Y2 = ActualHeight - BottomMargin,
            Stroke = axisBrush,
            StrokeThickness = 1
        };

        ChartCanvas.Children.Add(yAxis);
        ChartCanvas.Children.Add(xAxis);

        DrawTimeLabels();
    }

    private void DrawTimeLabels()
    {
        var labelBrush = new SolidColorBrush(Color.FromRgb(170, 179, 194));

        var step = Math.Max(4, _slots.Count / 8);

        for (var i = 0; i < _slots.Count; i += step)
        {
            var slot = _slots[i];

            var text = _slots.Select(x => x.LocalDate).Distinct().Count() > 1
                ? slot.StartsAt.ToString("dd HH:mm")
                : slot.StartsAt.ToString("HH:mm");

            var label = new TextBlock
            {
                Text = text,
                Foreground = labelBrush,
                FontSize = 11
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(label, IndexToX(i) - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, ActualHeight - BottomMargin + 10);

            ChartCanvas.Children.Add(label);
        }
    }

    private void DrawDaySeparators()
    {
        if (_slots.Count < 2)
            return;

        for (var i = 1; i < _slots.Count; i++)
        {
            if (_slots[i].LocalDate == _slots[i - 1].LocalDate)
                continue;

            var x = IndexToX(i);

            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = TopMargin,
                Y2 = ActualHeight - BottomMargin,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 176, 32)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            };

            ChartCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = _slots[i].StartsAt.ToString("yyyy-MM-dd"),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 176, 32)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };

            Canvas.SetLeft(label, x + 6);
            Canvas.SetTop(label, TopMargin + 4);

            ChartCanvas.Children.Add(label);
        }
    }

    private void ShowHover(Point point, PlannedSlot slot)
    {
        if (_hoverLine == null)
        {
            _hoverLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromArgb(150, 242, 245, 248)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 }
            };

            ChartCanvas.Children.Add(_hoverLine);
        }

        if (_hoverDot == null)
        {
            _hoverDot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(Color.FromRgb(0, 163, 255)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5
            };

            ChartCanvas.Children.Add(_hoverDot);
        }

        _hoverLine.X1 = point.X;
        _hoverLine.X2 = point.X;
        _hoverLine.Y1 = TopMargin;
        _hoverLine.Y2 = ActualHeight - BottomMargin;

        Canvas.SetLeft(_hoverDot, point.X - _hoverDot.Width / 2);
        Canvas.SetTop(_hoverDot, point.Y - _hoverDot.Height / 2);

        TooltipTimeText.Text = slot.StartsAt.ToString("yyyy-MM-dd HH:mm");
        TooltipPriceText.Text = $"Pris: {slot.PriceSekPerKwh:0.000} SEK/kWh";
        TooltipStatusText.Text = $"Status: {(slot.Run ? "ON" : "OFF")}";
        TooltipStatusText.Foreground = slot.Run
            ? new SolidColorBrush(Color.FromRgb(55, 214, 122))
            : new SolidColorBrush(Color.FromRgb(170, 179, 194));

        TooltipReasonText.Text = string.IsNullOrWhiteSpace(slot.Reason)
            ? "Orsak: -"
            : $"Orsak: {slot.Reason}";

        TooltipBorder.Visibility = Visibility.Visible;

        TooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var tooltipWidth = TooltipBorder.DesiredSize.Width;
        var tooltipHeight = TooltipBorder.DesiredSize.Height;

        var left = point.X + 14;
        var top = point.Y - tooltipHeight / 2;

        if (left + tooltipWidth > ActualWidth - 8)
            left = point.X - tooltipWidth - 14;

        if (top < 8)
            top = 8;

        if (top + tooltipHeight > ActualHeight - 8)
            top = ActualHeight - tooltipHeight - 8;

        TooltipBorder.Margin = new Thickness(left, top, 0, 0);
    }

    private void HideTooltip()
    {
        TooltipBorder.Visibility = Visibility.Collapsed;

        if (_hoverLine != null)
        {
            ChartCanvas.Children.Remove(_hoverLine);
            _hoverLine = null;
        }

        if (_hoverDot != null)
        {
            ChartCanvas.Children.Remove(_hoverDot);
            _hoverDot = null;
        }
    }

    private Point GetPoint(int index, decimal price)
    {
        var x = IndexToX(index);
        var y = PriceToY(price);

        return new Point(x, y);
    }

    private double IndexToX(int index)
    {
        if (_slots.Count <= 1)
            return LeftMargin;

        var chartWidth = GetChartWidth();

        return LeftMargin + index * chartWidth / (_slots.Count - 1);
    }

    private int XToIndex(double x)
    {
        if (_slots.Count <= 1)
            return 0;

        var chartWidth = GetChartWidth();

        var fraction = (x - LeftMargin) / chartWidth;

        var index = (int)Math.Round(fraction * (_slots.Count - 1));

        return Math.Clamp(index, 0, _slots.Count - 1);
    }

    private double PriceToY(decimal price)
    {
        var maxPrice = GetMaxPrice();
        var minPrice = GetMinPrice();

        if (maxPrice <= minPrice)
            return TopMargin + GetChartHeight() / 2;

        var fraction = (double)((price - minPrice) / (maxPrice - minPrice));

        return TopMargin + (1.0 - fraction) * GetChartHeight();
    }

    private decimal GetMaxPrice()
    {
        if (_slots.Count == 0)
            return 1m;

        var max = _slots.Max(x => x.PriceSekPerKwh);

        return max <= 0
            ? 1m
            : max * 1.08m;
    }

    private decimal GetMinPrice()
    {
        if (_slots.Count == 0)
            return 0m;

        var min = _slots.Min(x => x.PriceSekPerKwh);

        if (min >= 0)
            return 0m;

        return min * 1.08m;
    }

    private double GetChartWidth()
    {
        return Math.Max(1, ActualWidth - LeftMargin - RightMargin);
    }

    private double GetChartHeight()
    {
        return Math.Max(1, ActualHeight - TopMargin - BottomMargin);
    }

    private double GetSlotWidth()
    {
        if (_slots.Count <= 1)
            return GetChartWidth();

        return GetChartWidth() / (_slots.Count - 1);
    }
}
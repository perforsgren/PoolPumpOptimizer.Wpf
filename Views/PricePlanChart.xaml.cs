using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PoolPumpOptimizer.Wpf.Views;

public partial class PricePlanChart : UserControl
{
    private const double LeftPadding = 56;
    private const double TopPadding = 16;
    private const double RightPadding = 18;
    private const double BottomPadding = 42;

    private readonly DispatcherTimer _nowTimer;

    private INotifyCollectionChanged? _currentObservableCollection;

    private List<ChartSlot> _visibleSlots = new();
    private List<ChartPoint> _pricePoints = new();

    private DateTimeOffset _visibleStart;
    private DateTimeOffset _visibleEnd;

    private double _plotWidth;
    private double _plotHeight;

    private Line? _hoverLine;
    private Ellipse? _hoverDot;
    private Border? _hoverInfo;

    public PricePlanChart()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        _nowTimer = new DispatcherTimer(
            DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(15)
        };

        _nowTimer.Tick += OnNowTimerTick;
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(PricePlanChart),
            new PropertyMetadata(
                null,
                OnItemsSourceChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    private static void OnItemsSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not PricePlanChart control)
            return;

        control.DetachCollectionChanged();
        control.AttachCollectionChanged(eventArgs.NewValue as IEnumerable);
        control.Redraw();
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        _nowTimer.Start();
        Redraw();
    }

    private void OnUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        _nowTimer.Stop();
        DetachCollectionChanged();
    }

    private void OnSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        Redraw();
    }

    private void OnNowTimerTick(
        object? sender,
        EventArgs e)
    {
        Redraw();
    }

    private void AttachCollectionChanged(
        IEnumerable? source)
    {
        if (source is not INotifyCollectionChanged observable)
            return;

        _currentObservableCollection = observable;
        _currentObservableCollection.CollectionChanged +=
            OnCollectionChanged;
    }

    private void DetachCollectionChanged()
    {
        if (_currentObservableCollection == null)
            return;

        _currentObservableCollection.CollectionChanged -=
            OnCollectionChanged;

        _currentObservableCollection = null;
    }

    private void OnCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        Redraw();
    }

    private void Redraw()
    {
        if (!IsLoaded)
            return;

        PlotCanvas.Children.Clear();

        _hoverLine = null;
        _hoverDot = null;
        _hoverInfo = null;

        _visibleSlots = ReadSlots(ItemsSource);
        _pricePoints = new List<ChartPoint>();

        if (_visibleSlots.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        var controlWidth = ActualWidth;
        var controlHeight = ActualHeight;

        if (controlWidth < 160 ||
            controlHeight < 140)
        {
            return;
        }

        _plotWidth = Math.Max(
            10,
            controlWidth - LeftPadding - RightPadding);

        _plotHeight = Math.Max(
            10,
            controlHeight - TopPadding - BottomPadding);

        _visibleStart =
            _visibleSlots[0].StartsAt.ToLocalTime();

        _visibleEnd =
            _visibleSlots[^1]
                .StartsAt
                .ToLocalTime()
                .AddMinutes(15);

        var minPrice =
            _visibleSlots.Min(
                x => x.PriceSekPerKwh);

        var maxPrice =
            _visibleSlots.Max(
                x => x.PriceSekPerKwh);

        if (minPrice == maxPrice)
        {
            minPrice -= 0.10m;
            maxPrice += 0.10m;
        }

        var totalMinutes =
            Math.Max(
                15.0,
                (_visibleEnd - _visibleStart).TotalMinutes);

        var priceRange =
            (double)(maxPrice - minPrice);

        DrawRunBackgrounds(
            _visibleSlots,
            totalMinutes);

        DrawHorizontalGridAndLabels(
            minPrice,
            maxPrice);

        DrawVerticalTimeGridAndLabels(
            totalMinutes);

        DrawDateChangeLines(
            totalMinutes);

        _pricePoints = BuildPricePoints(
            _visibleSlots,
            totalMinutes,
            minPrice,
            priceRange);

        DrawPriceLine(_pricePoints);

        DrawCurrentTimeMarker(
            totalMinutes,
            _pricePoints);

        DrawPlotBorder();
    }

    private void DrawEmptyState()
    {
        var text = new TextBlock
        {
            Text = "Ingen prisdata att visa.",
            Foreground = CreateBrush(170, 180, 195),
            FontSize = 13
        };

        PlotCanvas.Children.Add(text);

        Canvas.SetLeft(text, 16);
        Canvas.SetTop(text, 16);
    }

    /// <summary>
    /// Ritar ett enda sammanhängande fält per ON-block.
    /// Därmed försvinner hack/taggar mellan varje kvart.
    /// </summary>
    private void DrawRunBackgrounds(
        IReadOnlyList<ChartSlot> slots,
        double totalMinutes)
    {
        var blocks = BuildRunBlocks(slots);

        foreach (var block in blocks)
        {
            var x1 =
                TimeToX(
                    block.Start,
                    totalMinutes);

            var x2 =
                TimeToX(
                    block.End,
                    totalMinutes);

            var rectangle = new Rectangle
            {
                Width = Math.Max(1, x2 - x1),
                Height = _plotHeight,
                RadiusX = 4,
                RadiusY = 4,
                Fill = new SolidColorBrush(
                    Color.FromArgb(
                        68,
                        55,
                        214,
                        122))
            };

            PlotCanvas.Children.Add(rectangle);

            Canvas.SetLeft(rectangle, x1);
            Canvas.SetTop(rectangle, TopPadding);
        }
    }

    private void DrawHorizontalGridAndLabels(
        decimal minPrice,
        decimal maxPrice)
    {
        const int horizontalLineCount = 5;

        for (var index = 0;
             index <= horizontalLineCount;
             index++)
        {
            var ratio =
                index / (double)horizontalLineCount;

            var y =
                TopPadding +
                ratio * _plotHeight;

            var line = new Line
            {
                X1 = LeftPadding,
                X2 = LeftPadding + _plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(
                    Color.FromArgb(
                        58,
                        118,
                        130,
                        148)),
                StrokeThickness = 0.7
            };

            PlotCanvas.Children.Add(line);

            var value =
                maxPrice -
                (decimal)ratio *
                (maxPrice - minPrice);

            var label = new TextBlock
            {
                Text = value.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture),
                Foreground = CreateBrush(
                    180,
                    191,
                    207),
                FontSize = 10
            };

            PlotCanvas.Children.Add(label);

            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 8);
        }
    }

    private void DrawVerticalTimeGridAndLabels(
        double totalMinutes)
    {
        var totalHours =
            (_visibleEnd - _visibleStart).TotalHours;

        var hourStep =
            GetHourStep(totalHours);

        var firstTick =
            new DateTimeOffset(
                _visibleStart.Year,
                _visibleStart.Month,
                _visibleStart.Day,
                _visibleStart.Hour,
                0,
                0,
                _visibleStart.Offset);

        while (firstTick < _visibleStart)
        {
            firstTick =
                firstTick.AddHours(1);
        }

        while (firstTick.Hour % hourStep != 0)
        {
            firstTick =
                firstTick.AddHours(1);
        }

        for (var tick = firstTick;
             tick <= _visibleEnd;
             tick = tick.AddHours(hourStep))
        {
            var x =
                TimeToX(
                    tick,
                    totalMinutes);

            var line = new Line
            {
                X1 = x,
                X2 = x,
                Y1 = TopPadding,
                Y2 = TopPadding + _plotHeight,
                Stroke = new SolidColorBrush(
                    Color.FromArgb(
                        42,
                        118,
                        130,
                        148)),
                StrokeThickness = 0.6
            };

            PlotCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = tick.ToString("HH:mm"),
                Foreground = CreateBrush(
                    180,
                    191,
                    207),
                FontSize = 10
            };

            PlotCanvas.Children.Add(label);

            Canvas.SetLeft(label, x - 17);
            Canvas.SetTop(
                label,
                TopPadding + _plotHeight + 7);
        }
    }

    /// <summary>
    /// Markerar varje datumbyte med en tydligare vertikal linje.
    /// </summary>
    private void DrawDateChangeLines(
        double totalMinutes)
    {
        var midnight =
            new DateTimeOffset(
                _visibleStart.Year,
                _visibleStart.Month,
                _visibleStart.Day,
                0,
                0,
                0,
                _visibleStart.Offset)
            .AddDays(1);

        while (midnight < _visibleEnd)
        {
            if (midnight > _visibleStart)
            {
                var x =
                    TimeToX(
                        midnight,
                        totalMinutes);

                var line = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = TopPadding,
                    Y2 = TopPadding + _plotHeight,
                    Stroke = CreateBrush(
                        105,
                        120,
                        142),
                    StrokeThickness = 1.2
                };

                PlotCanvas.Children.Add(line);

                var dateLabel = new TextBlock
                {
                    Text = midnight.ToString(
                        "dd MMM",
                        CultureInfo.CurrentCulture),
                    Foreground = CreateBrush(
                        204,
                        213,
                        228),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };

                PlotCanvas.Children.Add(dateLabel);

                Canvas.SetLeft(dateLabel, x + 5);
                Canvas.SetTop(dateLabel, TopPadding + 3);
            }

            midnight =
                midnight.AddDays(1);
        }
    }

    private List<ChartPoint> BuildPricePoints(
        IReadOnlyList<ChartSlot> slots,
        double totalMinutes,
        decimal minPrice,
        double priceRange)
    {
        var result =
            new List<ChartPoint>();

        foreach (var slot in slots)
        {
            var centerTime =
                slot.StartsAt
                    .ToLocalTime()
                    .AddMinutes(7.5);

            var x =
                TimeToX(
                    centerTime,
                    totalMinutes);

            var normalizedPrice =
                (double)(
                    slot.PriceSekPerKwh -
                    minPrice) /
                priceRange;

            var y =
                TopPadding +
                _plotHeight -
                normalizedPrice * _plotHeight;

            result.Add(
                new ChartPoint
                {
                    Time = centerTime,
                    X = x,
                    Y = y,
                    Price = slot.PriceSekPerKwh,
                    ShouldRun = slot.ShouldRun
                });
        }

        return result;
    }

    private void DrawPriceLine(
        IReadOnlyList<ChartPoint> points)
    {
        if (points.Count == 0)
            return;

        var polyline = new Polyline
        {
            Stroke = CreateBrush(
                18,
                162,
                255),
            StrokeThickness = 2.1,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        foreach (var point in points)
        {
            polyline.Points.Add(
                new Point(
                    point.X,
                    point.Y));
        }

        PlotCanvas.Children.Add(polyline);
    }

    private void DrawCurrentTimeMarker(
        double totalMinutes,
        IReadOnlyList<ChartPoint> points)
    {
        var now =
            DateTimeOffset.Now;

        if (now < _visibleStart ||
            now > _visibleEnd ||
            points.Count == 0)
        {
            return;
        }

        var x =
            TimeToX(
                now,
                totalMinutes);

        var line = new Line
        {
            X1 = x,
            X2 = x,
            Y1 = TopPadding,
            Y2 = TopPadding + _plotHeight,
            Stroke = CreateBrush(
                255,
                194,
                51),
            StrokeThickness = 1.1,
            StrokeDashArray =
                new DoubleCollection
                {
                    4,
                    4
                }
        };

        PlotCanvas.Children.Add(line);

        var y =
            GetInterpolatedYForTime(
                now,
                points);

        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = CreateBrush(
                255,
                194,
                51),
            Stroke = CreateBrush(
                20,
                24,
                31),
            StrokeThickness = 2
        };

        PlotCanvas.Children.Add(dot);

        Canvas.SetLeft(
            dot,
            x - dot.Width / 2);

        Canvas.SetTop(
            dot,
            y - dot.Height / 2);
    }

    private void DrawPlotBorder()
    {
        var border = new Rectangle
        {
            Width = _plotWidth,
            Height = _plotHeight,
            Stroke = new SolidColorBrush(
                Color.FromArgb(
                    105,
                    78,
                    90,
                    108)),
            StrokeThickness = 0.8
        };

        PlotCanvas.Children.Add(border);

        Canvas.SetLeft(
            border,
            LeftPadding);

        Canvas.SetTop(
            border,
            TopPadding);
    }

    private void PlotCanvas_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_pricePoints.Count == 0)
            return;

        var mousePosition =
            e.GetPosition(PlotCanvas);

        var plotRight =
            LeftPadding + _plotWidth;

        var plotBottom =
            TopPadding + _plotHeight;

        if (mousePosition.X < LeftPadding ||
            mousePosition.X > plotRight ||
            mousePosition.Y < TopPadding ||
            mousePosition.Y > plotBottom)
        {
            HideHoverElements();
            return;
        }

        var nearestPoint =
            _pricePoints
                .OrderBy(
                    point =>
                        Math.Abs(
                            point.X -
                            mousePosition.X))
                .First();

        EnsureHoverElements();

        if (_hoverLine == null ||
            _hoverDot == null ||
            _hoverInfo == null)
        {
            return;
        }

        _hoverLine.Visibility =
            Visibility.Visible;

        _hoverLine.X1 =
            nearestPoint.X;

        _hoverLine.X2 =
            nearestPoint.X;

        _hoverLine.Y1 =
            TopPadding;

        _hoverLine.Y2 =
            plotBottom;

        _hoverDot.Visibility =
            Visibility.Visible;

        Canvas.SetLeft(
            _hoverDot,
            nearestPoint.X -
            _hoverDot.Width / 2);

        Canvas.SetTop(
            _hoverDot,
            nearestPoint.Y -
            _hoverDot.Height / 2);

        UpdateHoverInfo(
            nearestPoint,
            mousePosition);
    }

    private void PlotCanvas_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        HideHoverElements();
    }

    private void EnsureHoverElements()
    {
        if (_hoverLine != null &&
            _hoverDot != null &&
            _hoverInfo != null)
        {
            return;
        }

        _hoverLine = new Line
        {
            Stroke = new SolidColorBrush(
                Color.FromArgb(
                    95,
                    55,
                    214,
                    122)),
            StrokeThickness = 1,
            Visibility = Visibility.Collapsed
        };

        _hoverDot = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = CreateBrush(
                55,
                214,
                122),
            Stroke = CreateBrush(
                17,
                22,
                29),
            StrokeThickness = 2,
            Visibility = Visibility.Collapsed
        };

        _hoverInfo = new Border
        {
            Background = new SolidColorBrush(
                Color.FromArgb(
                    242,
                    24,
                    29,
                    38)),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(
                    180,
                    72,
                    83,
                    102)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(
                9,
                7,
                9,
                7),
            Visibility = Visibility.Collapsed
        };

        PlotCanvas.Children.Add(_hoverLine);
        PlotCanvas.Children.Add(_hoverDot);
        PlotCanvas.Children.Add(_hoverInfo);

        Panel.SetZIndex(_hoverLine, 100);
        Panel.SetZIndex(_hoverDot, 101);
        Panel.SetZIndex(_hoverInfo, 102);
    }

    private void UpdateHoverInfo(
        ChartPoint point,
        Point mousePosition)
    {
        if (_hoverInfo == null)
            return;

        var stackPanel =
            new StackPanel();

        stackPanel.Children.Add(
            new TextBlock
            {
                Text = point.Time.ToString(
                    "ddd dd MMM HH:mm",
                    CultureInfo.CurrentCulture),
                Foreground = CreateBrush(
                    238,
                    243,
                    250),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            });

        stackPanel.Children.Add(
            new TextBlock
            {
                Text =
                    $"Pris: {point.Price:0.000} SEK/kWh",
                Foreground = CreateBrush(
                    196,
                    207,
                    222),
                FontSize = 11,
                Margin = new Thickness(
                    0,
                    4,
                    0,
                    0)
            });

        stackPanel.Children.Add(
            new TextBlock
            {
                Text =
                    point.ShouldRun
                        ? "Pump: PÅ"
                        : "Pump: AV",
                Foreground =
                    point.ShouldRun
                        ? CreateBrush(
                            55,
                            214,
                            122)
                        : CreateBrush(
                            196,
                            207,
                            222),
                FontSize = 11,
                Margin = new Thickness(
                    0,
                    2,
                    0,
                    0)
            });

        _hoverInfo.Child =
            stackPanel;

        _hoverInfo.Visibility =
            Visibility.Visible;

        _hoverInfo.Measure(
            new Size(
                double.PositiveInfinity,
                double.PositiveInfinity));

        var desiredWidth =
            _hoverInfo.DesiredSize.Width;

        var desiredHeight =
            _hoverInfo.DesiredSize.Height;

        var left =
            mousePosition.X + 14;

        var top =
            mousePosition.Y -
            desiredHeight -
            12;

        if (left + desiredWidth >
            ActualWidth - 6)
        {
            left =
                mousePosition.X -
                desiredWidth -
                14;
        }

        if (top < 6)
        {
            top =
                mousePosition.Y +
                14;
        }

        Canvas.SetLeft(
            _hoverInfo,
            left);

        Canvas.SetTop(
            _hoverInfo,
            top);
    }

    private void HideHoverElements()
    {
        if (_hoverLine != null)
        {
            _hoverLine.Visibility =
                Visibility.Collapsed;
        }

        if (_hoverDot != null)
        {
            _hoverDot.Visibility =
                Visibility.Collapsed;
        }

        if (_hoverInfo != null)
        {
            _hoverInfo.Visibility =
                Visibility.Collapsed;
        }
    }

    private double TimeToX(
        DateTimeOffset time,
        double totalMinutes)
    {
        return
            LeftPadding +
            ((time - _visibleStart).TotalMinutes /
             totalMinutes) *
            _plotWidth;
    }

    private static double GetInterpolatedYForTime(
        DateTimeOffset time,
        IReadOnlyList<ChartPoint> points)
    {
        if (points.Count == 0)
            return 0;

        if (points.Count == 1)
            return points[0].Y;

        if (time <= points[0].Time)
            return points[0].Y;

        if (time >= points[^1].Time)
            return points[^1].Y;

        for (var index = 1;
             index < points.Count;
             index++)
        {
            var previous =
                points[index - 1];

            var current =
                points[index];

            if (time > current.Time)
                continue;

            var segmentMinutes =
                (current.Time -
                 previous.Time)
                .TotalMinutes;

            if (segmentMinutes <= 0)
                return previous.Y;

            var ratio =
                (time -
                 previous.Time)
                .TotalMinutes /
                segmentMinutes;

            return
                previous.Y +
                (current.Y -
                 previous.Y) *
                ratio;
        }

        return points[^1].Y;
    }

    private static List<RunBlockRange> BuildRunBlocks(
        IReadOnlyList<ChartSlot> slots)
    {
        var result =
            new List<RunBlockRange>();

        DateTimeOffset? currentStart =
            null;

        DateTimeOffset? currentEnd =
            null;

        foreach (var slot in slots)
        {
            var slotStart =
                slot.StartsAt.ToLocalTime();

            var slotEnd =
                slotStart.AddMinutes(15);

            if (!slot.ShouldRun)
            {
                if (currentStart.HasValue &&
                    currentEnd.HasValue)
                {
                    result.Add(
                        new RunBlockRange
                        {
                            Start = currentStart.Value,
                            End = currentEnd.Value
                        });

                    currentStart = null;
                    currentEnd = null;
                }

                continue;
            }

            if (!currentStart.HasValue)
            {
                currentStart = slotStart;
                currentEnd = slotEnd;
                continue;
            }

            if (currentEnd == slotStart)
            {
                currentEnd = slotEnd;
                continue;
            }

            result.Add(
                new RunBlockRange
                {
                    Start = currentStart.Value,
                    End = currentEnd!.Value
                });

            currentStart = slotStart;
            currentEnd = slotEnd;
        }

        if (currentStart.HasValue &&
            currentEnd.HasValue)
        {
            result.Add(
                new RunBlockRange
                {
                    Start = currentStart.Value,
                    End = currentEnd.Value
                });
        }

        return result;
    }

    private static int GetHourStep(
        double totalHours)
    {
        if (totalHours <= 8)
            return 1;

        if (totalHours <= 16)
            return 2;

        if (totalHours <= 30)
            return 3;

        if (totalHours <= 48)
            return 4;

        return 6;
    }

    private static List<ChartSlot> ReadSlots(
        IEnumerable? source)
    {
        var result =
            new List<ChartSlot>();

        if (source == null)
            return result;

        foreach (var item in source)
        {
            if (item == null)
                continue;

            var startsAt =
                TryGetDateTimeOffset(
                    item,
                    "StartsAt");

            var price =
                TryGetDecimal(
                    item,
                    "PriceSekPerKwh")
                ?? TryGetDecimal(
                    item,
                    "Price");

            if (!startsAt.HasValue ||
                !price.HasValue)
            {
                continue;
            }

            var shouldRun =
                TryGetBoolean(
                    item,
                    "ShouldRun")
                ?? TryGetBoolean(
                    item,
                    "IsRunning")
                ?? TryGetBoolean(
                    item,
                    "IsOn")
                ?? ParseRunText(item)
                ?? false;

            result.Add(
                new ChartSlot
                {
                    StartsAt =
                        startsAt.Value,

                    PriceSekPerKwh =
                        price.Value,

                    ShouldRun =
                        shouldRun
                });
        }

        return result
            .OrderBy(
                x => x.StartsAt)
            .ToList();
    }

    private static DateTimeOffset? TryGetDateTimeOffset(
        object source,
        string propertyName)
    {
        var value =
            TryGetPropertyValue(
                source,
                propertyName);

        if (value == null)
            return null;

        if (value is DateTimeOffset dateTimeOffset)
            return dateTimeOffset;

        if (value is DateTime dateTime)
            return new DateTimeOffset(dateTime);

        if (value is string text &&
            DateTimeOffset.TryParse(
                text,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryGetDecimal(
        object source,
        string propertyName)
    {
        var value =
            TryGetPropertyValue(
                source,
                propertyName);

        if (value == null)
            return null;

        try
        {
            return Convert.ToDecimal(
                value,
                CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private static bool? TryGetBoolean(
        object source,
        string propertyName)
    {
        var value =
            TryGetPropertyValue(
                source,
                propertyName);

        if (value == null)
            return null;

        if (value is bool booleanValue)
            return booleanValue;

        if (value is string text)
        {
            if (string.Equals(
                    text,
                    "ON",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(
                    text,
                    "OFF",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (bool.TryParse(
                    text,
                    out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool? ParseRunText(
        object source)
    {
        var value =
            TryGetPropertyValue(
                source,
                "RunText")
            ?? TryGetPropertyValue(
                source,
                "Status");

        if (value is not string text)
            return null;

        if (string.Equals(
                text,
                "ON",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(
                text,
                "OFF",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static object? TryGetPropertyValue(
        object source,
        string propertyName)
    {
        var property =
            source.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.IgnoreCase);

        return property?.GetValue(source);
    }

    private static SolidColorBrush CreateBrush(
        byte red,
        byte green,
        byte blue)
    {
        return new SolidColorBrush(
            Color.FromRgb(
                red,
                green,
                blue));
    }

    private sealed class ChartSlot
    {
        public DateTimeOffset StartsAt { get; set; }

        public decimal PriceSekPerKwh { get; set; }

        public bool ShouldRun { get; set; }
    }

    private sealed class ChartPoint
    {
        public DateTimeOffset Time { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public decimal Price { get; set; }

        public bool ShouldRun { get; set; }
    }

    private sealed class RunBlockRange
    {
        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }
    }
}
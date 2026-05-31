using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Gbt.Common;

namespace Gbt.ControlCenter;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    // Editable working copies of the two curves (records are immutable, so edits replace entries).
    private List<FanCurvePoint> _cpu = DefaultCpu();
    private List<FanCurvePoint> _gpu = DefaultGpu();
    private bool _editingGpu;

    // Rolling temperature history for the chart (~2 min at 1 Hz).
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _gpuHistory = new();
    private const int HistoryCapacity = 120;

    // Editor axes: full 0-100 °C range.
    private const double TMin = 0, TMax = 100, Pad = 16, ThumbR = 7;

    private static readonly Brush CpuBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00));
    private static readonly Brush GpuBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x31));
    private static readonly Brush AxisTextBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x85));

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.TemperaturesSampled += OnTemperaturesSampled;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.ConnectAsync();
        LoadCurvesFromProfile(_viewModel.CurrentProfile);
        RenderCurve();
        HighlightActiveMode();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.TemperaturesSampled -= OnTemperaturesSampled;
        await _viewModel.DisposeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentMode))
        {
            HighlightActiveMode();
        }
    }

    // ---- Active performance-mode highlight ----
    private void HighlightActiveMode()
    {
        var mode = _viewModel.CurrentMode;
        SetActive(QuietButton, mode == "Quiet");
        SetActive(NormalButton, mode == "Normal");
        SetActive(GamingButton, mode == "Gaming");
        SetActive(BoostButton, mode == "Boost");
    }

    private void SetActive(Button button, bool active)
    {
        if (active)
        {
            button.Background = (Brush)FindResource("Accent");
            button.Foreground = Brushes.Black;
            button.BorderThickness = new Thickness(0);
        }
        else
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x34));
            button.Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xEA));
            button.BorderThickness = new Thickness(1);
        }
    }

    // ---- Temperature history chart ----
    private void OnTemperaturesSampled(double cpu, double gpu)
    {
        _cpuHistory.Enqueue(cpu);
        _gpuHistory.Enqueue(gpu);
        while (_cpuHistory.Count > HistoryCapacity)
        {
            _cpuHistory.Dequeue();
        }
        while (_gpuHistory.Count > HistoryCapacity)
        {
            _gpuHistory.Dequeue();
        }
        RenderTempChart();
    }

    private void OnTempCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RenderTempChart();

    private void RenderTempChart()
    {
        var canvas = TempCanvas;
        if (canvas is null)
        {
            return;
        }

        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 50 || h < 30)
        {
            return;
        }

        const double left = 26, right = 6, top = 8;
        var bottom = h - 6;

        // Horizontal gridlines + labels across the FULL 0-100 °C range.
        for (var t = 0; t <= 100; t += 25)
        {
            var y = bottom - t / 100.0 * (bottom - top);
            canvas.Children.Add(new Line { X1 = left, Y1 = y, X2 = w - right, Y2 = y, Stroke = GridBrush, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = $"{t}", Foreground = AxisTextBrush, FontSize = 9 };
            Canvas.SetLeft(lbl, 4);
            Canvas.SetTop(lbl, y - 7);
            canvas.Children.Add(lbl);
        }

        DrawSeries(canvas, _cpuHistory, CpuBrush, left, right, top, bottom, w);
        DrawSeries(canvas, _gpuHistory, GpuBrush, left, right, top, bottom, w);
    }

    private static void DrawSeries(Canvas canvas, Queue<double> history, Brush brush,
        double left, double right, double top, double bottom, double w)
    {
        if (history.Count < 2)
        {
            return;
        }

        var arr = history.ToArray();
        var n = arr.Length;
        var dx = (w - left - right) / (HistoryCapacity - 1);
        var line = new Polyline { Stroke = brush, StrokeThickness = 1.5 };
        for (var i = 0; i < n; i++)
        {
            var x = (w - right) - (n - 1 - i) * dx; // newest sample pinned to the right edge
            var y = bottom - Math.Clamp(arr[i], 0, 100) / 100.0 * (bottom - top);
            line.Points.Add(new Point(x, y));
        }
        canvas.Children.Add(line);
    }

    // ---- Fan curve editor ----
    private void LoadCurvesFromProfile(PerformanceProfile? profile)
    {
        if (profile?.FanCurve is { } fc)
        {
            _cpu = fc.Cpu.ToList();
            _gpu = fc.Gpu.ToList();
        }
    }

    private List<FanCurvePoint> Active => _editingGpu ? _gpu : _cpu;

    private void OnCurveTargetChanged(object sender, RoutedEventArgs e)
    {
        _editingGpu = GpuRadio?.IsChecked == true;
        RenderCurve();
    }

    private void OnCurveCanvasSizeChanged(object sender, SizeChangedEventArgs e) => RenderCurve();

    private void OnResetCurve(object sender, RoutedEventArgs e)
    {
        if (_editingGpu)
        {
            _gpu = DefaultGpu();
        }
        else
        {
            _cpu = DefaultCpu();
        }
        RenderCurve();
    }

    private async void OnApplyCurve(object sender, RoutedEventArgs e)
    {
        try
        {
            var curve = new FanCurve(_cpu, _gpu);
            await _viewModel.ApplyCurveAsync(curve);
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus($"Curve rejected: {ex.Message}");
        }
    }

    private void RenderCurve()
    {
        var canvas = CurveCanvas;
        if (canvas is null)
        {
            return;
        }

        canvas.Children.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 60 || h < 60)
        {
            return;
        }

        // Vertical gridlines + temperature labels across the full 0-100 °C axis.
        for (var t = 0; t <= 100; t += 20)
        {
            var x = MapX(t, w);
            canvas.Children.Add(new Line { X1 = x, Y1 = Pad, X2 = x, Y2 = h - Pad, Stroke = GridBrush, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = $"{t}°", Foreground = AxisTextBrush, FontSize = 10 };
            Canvas.SetLeft(lbl, x - 9);
            Canvas.SetTop(lbl, h - Pad + 2);
            canvas.Children.Add(lbl);
        }

        // Horizontal gridlines (duty %).
        for (var d = 0; d <= 100; d += 25)
        {
            var y = MapY(d, h);
            canvas.Children.Add(new Line { X1 = Pad, Y1 = y, X2 = w - Pad, Y2 = y, Stroke = GridBrush, StrokeThickness = 1 });
        }

        var pts = Active;

        var poly = new Polyline { Stroke = CpuBrush, StrokeThickness = 2 };
        foreach (var p in pts)
        {
            poly.Points.Add(new Point(MapX(p.TempCelsius, w), MapY(p.DutyPercent, h)));
        }
        canvas.Children.Add(poly);

        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var thumb = new Thumb { Style = (Style)FindResource("PointThumb"), Tag = i };
            Canvas.SetLeft(thumb, MapX(p.TempCelsius, w) - ThumbR);
            Canvas.SetTop(thumb, MapY(p.DutyPercent, h) - ThumbR);
            thumb.DragDelta += OnThumbDrag;
            canvas.Children.Add(thumb);
        }
    }

    private void OnThumbDrag(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not int i)
        {
            return;
        }

        var canvas = CurveCanvas;
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        var pts = Active;
        if (i < 0 || i >= pts.Count)
        {
            return;
        }

        var x = Canvas.GetLeft(thumb) + ThumbR + e.HorizontalChange;
        var y = Canvas.GetTop(thumb) + ThumbR + e.VerticalChange;

        var temp = (int)Math.Round(UnmapX(x, w));
        var duty = (int)Math.Round(UnmapY(y, h));

        var minT = i > 0 ? pts[i - 1].TempCelsius + 1 : (int)TMin;
        var maxT = i < pts.Count - 1 ? pts[i + 1].TempCelsius - 1 : (int)TMax;
        temp = Math.Clamp(temp, minT, maxT);
        duty = Math.Clamp(duty, 0, 100);

        pts[i] = new FanCurvePoint(temp, duty);
        RenderCurve();
    }

    private static double MapX(double t, double w) => Pad + (t - TMin) / (TMax - TMin) * (w - 2 * Pad);
    private static double MapY(double d, double h) => (h - Pad) - d / 100.0 * (h - 2 * Pad);
    private static double UnmapX(double x, double w) => TMin + (x - Pad) / (w - 2 * Pad) * (TMax - TMin);
    private static double UnmapY(double y, double h) => ((h - Pad) - y) / (h - 2 * Pad) * 100.0;

    private static List<FanCurvePoint> DefaultCpu() => new()
    {
        new FanCurvePoint(40, 25), new FanCurvePoint(60, 40), new FanCurvePoint(75, 65), new FanCurvePoint(90, 95),
    };

    private static List<FanCurvePoint> DefaultGpu() => new()
    {
        new FanCurvePoint(45, 30), new FanCurvePoint(65, 45), new FanCurvePoint(80, 70), new FanCurvePoint(90, 100),
    };
}

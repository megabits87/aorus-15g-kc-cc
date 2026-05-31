using System;
using System.Collections.Generic;
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

    // Data ranges shown on the editor axes.
    private const double TMin = 20, TMax = 100, Pad = 16, ThumbR = 7;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.ConnectAsync();
        LoadCurvesFromProfile(_viewModel.CurrentProfile);
        RenderCurve();
    }

    private async void OnClosed(object? sender, EventArgs e) => await _viewModel.DisposeAsync();

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

        var grid = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x31));
        var axisText = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x85));

        // Vertical gridlines + temperature labels.
        for (var t = 20; t <= 100; t += 20)
        {
            var x = MapX(t, w);
            canvas.Children.Add(new Line { X1 = x, Y1 = Pad, X2 = x, Y2 = h - Pad, Stroke = grid, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = $"{t}°", Foreground = axisText, FontSize = 10 };
            Canvas.SetLeft(lbl, x - 9);
            Canvas.SetTop(lbl, h - Pad + 2);
            canvas.Children.Add(lbl);
        }

        // Horizontal gridlines (duty %).
        for (var d = 0; d <= 100; d += 25)
        {
            var y = MapY(d, h);
            canvas.Children.Add(new Line { X1 = Pad, Y1 = y, X2 = w - Pad, Y2 = y, Stroke = grid, StrokeThickness = 1 });
        }

        var pts = Active;

        // The curve line.
        var poly = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00)), StrokeThickness = 2 };
        foreach (var p in pts)
        {
            poly.Points.Add(new Point(MapX(p.TempCelsius, w), MapY(p.DutyPercent, h)));
        }
        canvas.Children.Add(poly);

        // Draggable points.
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

        // Keep points strictly ordered by temperature (FanCurve requires it) and within range.
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

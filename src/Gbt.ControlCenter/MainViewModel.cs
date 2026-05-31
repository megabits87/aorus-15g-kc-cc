using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Gbt.Common;

namespace Gbt.ControlCenter;

/// <summary>
/// View-model for the single-page Control Center dashboard. Owns the IPC client, subscribes to the
/// live sensor stream and exposes performance-mode, fan-curve and battery commands. All property
/// writes are marshalled to the UI thread via the application dispatcher.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly GbtServiceClient _client = new();
    private readonly CancellationTokenSource _cts = new();

    private string _status = "Disconnected";
    private string _modelName = "AORUS 15G KC";
    private string _cpuTemp = "—";
    private string _gpuTemp = "—";
    private string _cpuFan = "—";
    private string _gpuFan = "—";
    private string _battery = "—";
    private string _powerLimits = "—";
    private string _currentMode = "—";
    private double _batteryLimit = 100;
    private double _cpuTempValue;
    private double _gpuTempValue;

    public MainViewModel()
    {
        SetQuietCommand = new RelayCommand(() => SetModeAsync(PerformanceMode.Quiet), () => IsConnected);
        SetNormalCommand = new RelayCommand(() => SetModeAsync(PerformanceMode.Normal), () => IsConnected);
        SetGamingCommand = new RelayCommand(() => SetModeAsync(PerformanceMode.Gaming), () => IsConnected);
        SetBoostCommand = new RelayCommand(() => SetModeAsync(PerformanceMode.Boost), () => IsConnected);
        ApplyBatteryCommand = new RelayCommand(ApplyBatteryAsync, () => IsConnected);
    }

    public ICommand SetQuietCommand { get; }
    public ICommand SetNormalCommand { get; }
    public ICommand SetGamingCommand { get; }
    public ICommand SetBoostCommand { get; }
    public ICommand ApplyBatteryCommand { get; }

    public bool IsConnected => _client.IsConnected;

    /// <summary>Last profile read from the service; the fan-curve editor seeds itself from this.</summary>
    public PerformanceProfile? CurrentProfile { get; private set; }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ModelName { get => _modelName; private set => Set(ref _modelName, value); }
    public string CpuTemp { get => _cpuTemp; private set => Set(ref _cpuTemp, value); }
    public string GpuTemp { get => _gpuTemp; private set => Set(ref _gpuTemp, value); }
    public string CpuFan { get => _cpuFan; private set => Set(ref _cpuFan, value); }
    public string GpuFan { get => _gpuFan; private set => Set(ref _gpuFan, value); }
    public string Battery { get => _battery; private set => Set(ref _battery, value); }
    public string PowerLimits { get => _powerLimits; private set => Set(ref _powerLimits, value); }
    public string CurrentMode { get => _currentMode; private set => Set(ref _currentMode, value); }

    /// <summary>CPU package temperature clamped to 0-100 for the progress bar.</summary>
    public double CpuTempValue { get => _cpuTempValue; private set => Set(ref _cpuTempValue, value); }
    public double GpuTempValue { get => _gpuTempValue; private set => Set(ref _gpuTempValue, value); }

    public double BatteryLimit { get => _batteryLimit; set => Set(ref _batteryLimit, value); }

    /// <summary>Lets the code-behind (fan-curve editor) push a status message.</summary>
    public void SetStatus(string message) => Dispatch(() => Status = message);

    public async Task ConnectAsync()
    {
        try
        {
            Status = "Connecting…";
            await _client.ConnectAsync(_cts.Token).ConfigureAwait(true);
            Status = "Connected";
            RaiseCommands();

            await RefreshProfileAsync().ConfigureAwait(true);
            await RefreshBatteryAsync().ConfigureAwait(true);
            _ = Task.Run(() => SubscribeLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            Status = $"Service unavailable: {ex.Message}. Is GbtControlCenter running?";
        }
    }

    private async Task SubscribeLoopAsync(CancellationToken ct)
    {
        var service = _client.Service;
        if (service is null)
        {
            return;
        }

        try
        {
            await foreach (var snap in service.SubscribeAsync(ct).ConfigureAwait(false))
            {
                Dispatch(() => ApplySnapshot(snap));
            }
        }
        catch (OperationCanceledException)
        {
            // window closing
        }
        catch (Exception ex)
        {
            Dispatch(() => Status = $"Sensor stream ended: {ex.Message}");
        }
    }

    private void ApplySnapshot(SensorSnapshot snap)
    {
        ModelName = string.IsNullOrWhiteSpace(snap.ModelName) ? "AORUS 15G KC" : snap.ModelName;
        CpuTemp = FormatTemp(snap.CpuPackageC);
        GpuTemp = FormatTemp(snap.GpuC);
        CpuTempValue = double.IsNaN(snap.CpuPackageC) ? 0 : Math.Clamp(snap.CpuPackageC, 0, 100);
        GpuTempValue = double.IsNaN(snap.GpuC) ? 0 : Math.Clamp(snap.GpuC, 0, 100);
        TemperaturesSampled?.Invoke(CpuTempValue, GpuTempValue);
        CpuFan = $"{snap.CpuFanRpm} rpm";
        GpuFan = $"{snap.GpuFanRpm} rpm";
        Battery = $"{snap.BatteryPercent}% ({(snap.BatteryCharging ? "charging" : "on battery")})";
        PowerLimits = $"PL1 {snap.Pl1Watts} W / PL2 {snap.Pl2Watts} W";
    }

    private async Task SetModeAsync(PerformanceMode mode)
    {
        var service = _client.Service;
        if (service is null)
        {
            return;
        }
        try
        {
            await service.SetPerformanceModeAsync(mode).ConfigureAwait(true);
            await RefreshProfileAsync().ConfigureAwait(true);
            Status = $"Applied {mode} mode";
        }
        catch (Exception ex)
        {
            Status = $"Failed to set {mode}: {ex.Message}";
        }
    }

    /// <summary>Applies an edited fan curve as a Custom profile, keeping the current power limits.</summary>
    public async Task ApplyCurveAsync(FanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        var service = _client.Service;
        if (service is null)
        {
            return;
        }

        var profile = CurrentProfile;
        var pl1 = profile?.Pl1Watts ?? 45;
        var pl2 = profile?.Pl2Watts ?? 60;
        try
        {
            await service.SetCustomProfileAsync(new PerformanceProfile(PerformanceMode.Custom, pl1, pl2, curve)).ConfigureAwait(true);
            await RefreshProfileAsync().ConfigureAwait(true);
            Status = "Custom fan curve applied";
        }
        catch (Exception ex)
        {
            Status = $"Failed to apply curve: {ex.Message}";
        }
    }

    private async Task ApplyBatteryAsync()
    {
        var service = _client.Service;
        if (service is null)
        {
            return;
        }
        try
        {
            await service.SetBatteryChargeLimitAsync(new BatteryChargeLimit((int)Math.Round(BatteryLimit))).ConfigureAwait(true);
            Status = $"Battery charge limit set to {(int)Math.Round(BatteryLimit)}%";
        }
        catch (Exception ex)
        {
            Status = $"Failed to set battery limit: {ex.Message}";
        }
    }

    private async Task RefreshProfileAsync()
    {
        var service = _client.Service;
        if (service is null)
        {
            return;
        }
        var profile = await service.GetProfileAsync().ConfigureAwait(true);
        CurrentProfile = profile;
        CurrentMode = profile.Mode.ToString();
        PowerLimits = $"PL1 {profile.Pl1Watts} W / PL2 {profile.Pl2Watts} W";
    }

    private async Task RefreshBatteryAsync()
    {
        var service = _client.Service;
        if (service is null)
        {
            return;
        }
        var limit = await service.GetBatteryChargeLimitAsync().ConfigureAwait(true);
        BatteryLimit = limit.Percent;
    }

    private static string FormatTemp(double v) =>
        double.IsNaN(v) ? "n/a" : $"{v.ToString("F0", CultureInfo.InvariantCulture)} °C";

    private void RaiseCommands()
    {
        (SetQuietCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SetNormalCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SetGamingCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SetBoostCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyBatteryCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    /// <summary>Raised once per sensor snapshot with CPU/GPU temperatures (clamped 0-100) for the history chart.</summary>
    public event Action<double, double>? TemperaturesSampled;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

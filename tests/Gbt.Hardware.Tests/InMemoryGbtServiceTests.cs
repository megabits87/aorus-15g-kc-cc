using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Gbt.Common;
using Gbt.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gbt.Hardware.Tests;

public class InMemoryGbtServiceTests
{
    private static InMemoryGbtService NewService() => new(NullLogger<InMemoryGbtService>.Instance);

    [Fact]
    public async Task Default_profile_is_normal_with_expected_power_limits()
    {
        var svc = NewService();

        var profile = await svc.GetProfileAsync();

        profile.Mode.Should().Be(PerformanceMode.Normal);
        profile.Pl1Watts.Should().Be(45);
        profile.Pl2Watts.Should().Be(60);
    }

    [Theory]
    [InlineData(PerformanceMode.Quiet, 35, 45)]
    [InlineData(PerformanceMode.Normal, 45, 60)]
    [InlineData(PerformanceMode.Gaming, 60, 90)]
    [InlineData(PerformanceMode.Boost, 90, 110)]
    public async Task SetPerformanceMode_applies_preset_power_limits(PerformanceMode mode, int pl1, int pl2)
    {
        var svc = NewService();

        await svc.SetPerformanceModeAsync(mode);
        var profile = await svc.GetProfileAsync();

        profile.Mode.Should().Be(mode);
        profile.Pl1Watts.Should().Be(pl1);
        profile.Pl2Watts.Should().Be(pl2);
    }

    [Fact]
    public async Task Snapshot_reflects_the_active_profile_power_limits()
    {
        var svc = NewService();
        await svc.SetPerformanceModeAsync(PerformanceMode.Gaming);

        var snap = await svc.GetSnapshotAsync();

        snap.Pl1Watts.Should().Be(60);
        snap.Pl2Watts.Should().Be(90);
        snap.ModelName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SetCustomProfile_rejects_non_custom_mode()
    {
        var svc = NewService();
        var curve = new FanCurve(new[] { new FanCurvePoint(40, 30) }, new[] { new FanCurvePoint(40, 30) });
        var notCustom = new PerformanceProfile(PerformanceMode.Normal, 50, 70, curve);

        var act = async () => await svc.SetCustomProfileAsync(notCustom);

        await act.Should().ThrowAsync<System.ArgumentException>();
    }

    [Fact]
    public async Task SetCustomProfile_is_persisted_for_subsequent_reads()
    {
        var svc = NewService();
        var curve = new FanCurve(new[] { new FanCurvePoint(40, 30) }, new[] { new FanCurvePoint(40, 30) });
        var custom = new PerformanceProfile(PerformanceMode.Custom, 55, 75, curve);

        await svc.SetCustomProfileAsync(custom);
        var profile = await svc.GetProfileAsync();

        profile.Mode.Should().Be(PerformanceMode.Custom);
        profile.Pl1Watts.Should().Be(55);
        profile.Pl2Watts.Should().Be(75);
    }

    [Fact]
    public async Task Battery_charge_limit_round_trips()
    {
        var svc = NewService();

        await svc.SetBatteryChargeLimitAsync(new BatteryChargeLimit(80));
        var limit = await svc.GetBatteryChargeLimitAsync();

        limit.Percent.Should().Be(80);
    }

    [Fact]
    public async Task Subscribe_emits_at_least_one_snapshot_then_honours_cancellation()
    {
        var svc = NewService();
        using var cts = new CancellationTokenSource();
        var count = 0;

        await foreach (var snap in svc.SubscribeAsync(cts.Token))
        {
            snap.Should().NotBeNull();
            count++;
            await cts.CancelAsync();
        }

        count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Diagnostics_reports_phase0_stub_warnings()
    {
        var svc = NewService();

        var report = await svc.RunDiagnosticsAsync();

        report.Warnings.Should().NotBeEmpty();
        report.ServiceVersion.Should().NotBeNullOrEmpty();
    }
}

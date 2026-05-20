using FluentAssertions;
using Xunit;

namespace Gbt.Hardware.Tests;

public class RegisterWhitelistTests
{
    [Fact]
    public void Whitelisted_registers_are_documented()
    {
        RegisterWhitelist.AllowedWrites.Should().Contain(new[]
        {
            RegisterWhitelist.CpuFanDutyRegister,
            RegisterWhitelist.GpuFanDutyRegister,
            RegisterWhitelist.FanControlModeRegister,
        });
    }

    [Theory]
    [InlineData((byte)0xB0)]
    [InlineData((byte)0xB1)]
    [InlineData((byte)0xB2)]
    public void AssertCanWrite_passes_for_whitelisted_registers(byte reg)
    {
        var act = () => RegisterWhitelist.AssertCanWrite(reg);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x60)] // temperature read register — must NOT be writable
    [InlineData((byte)0xFF)]
    public void AssertCanWrite_throws_for_non_whitelisted_registers(byte reg)
    {
        var act = () => RegisterWhitelist.AssertCanWrite(reg);
        act.Should().Throw<EcAccessViolationException>()
            .Which.Register.Should().Be(reg);
    }

    [Fact]
    public void EcAccessViolation_message_includes_register_in_hex()
    {
        var ex = Record.Exception(() => RegisterWhitelist.AssertCanWrite(0xAB));
        ex.Should().BeOfType<EcAccessViolationException>();
        ex!.Message.Should().Contain("0xAB");
    }

    [Fact]
    public void UnverifiedHardwareIds_All_is_non_empty_and_covers_whitelist()
    {
        UnverifiedHardwareIds.All.Should().NotBeEmpty();
        UnverifiedHardwareIds.All.Should().Contain(x => x.Name == nameof(RegisterWhitelist.CpuFanDutyRegister));
        UnverifiedHardwareIds.All.Should().Contain(x => x.Name == nameof(RegisterWhitelist.GpuFanDutyRegister));
        UnverifiedHardwareIds.All.Should().Contain(x => x.Name == nameof(RegisterWhitelist.FanControlModeRegister));
    }
}

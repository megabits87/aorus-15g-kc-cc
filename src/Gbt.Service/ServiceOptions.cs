namespace Gbt.Service;

/// <summary>
/// Strongly-typed binding for the <c>Ipc</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class IpcOptions
{
    public const string SectionName = "Ipc";

    /// <summary>
    /// Name of the Windows named pipe that <c>Gbt.Service</c> listens on.
    /// The pipe is created with an ACL that grants <c>BUILTIN\Users</c> read/write.
    /// </summary>
    public string PipeName { get; init; } = "gbt-service";
}

/// <summary>
/// Strongly-typed binding for the <c>Storage</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Root directory for persisted state (settings.json, log files).
    /// Environment variables (<c>%ProgramData%</c>, <c>%LOCALAPPDATA%</c>) are expanded at runtime.
    /// </summary>
    public string Root { get; init; } = "%ProgramData%\\GbtControlCenter";
}

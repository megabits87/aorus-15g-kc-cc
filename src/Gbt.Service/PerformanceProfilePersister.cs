using System.Text.Json;
using Gbt.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gbt.Service;

/// <summary>The slice of state we persist between service restarts.</summary>
public sealed record PersistedState(PerformanceProfile? Profile, int BatteryPercent)
{
    public static PersistedState Default { get; } = new(null, 100);
}

/// <summary>
/// Loads and atomically saves <see cref="PersistedState"/> to <c>settings.json</c> under the storage
/// root. Writes go to a temp file and are then moved into place so a crash mid-write can never leave a
/// half-written, unparseable settings file.
/// </summary>
public sealed class PerformanceProfilePersister
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<PerformanceProfilePersister> _logger;
    private readonly string _path;
    private readonly object _gate = new();

    public PerformanceProfilePersister(IOptions<StorageOptions> storage, ILogger<PerformanceProfilePersister> logger)
    {
        _logger = logger;
        var root = Environment.ExpandEnvironmentVariables(storage.Value.Root);
        _path = Path.Combine(root, "settings.json");
    }

    public PersistedState Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return PersistedState.Default;
                }

                var json = File.ReadAllText(_path);
                var state = JsonSerializer.Deserialize<PersistedState>(json, Options);
                return state ?? PersistedState.Default;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read {Path}; using defaults", _path);
                return PersistedState.Default;
            }
        }
    }

    public void Save(PersistedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                var json = JsonSerializer.Serialize(state, Options);
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist settings to {Path}", _path);
            }
        }
    }
}

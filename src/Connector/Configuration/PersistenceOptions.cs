using System.ComponentModel.DataAnnotations;

namespace Connector;

/// <summary>
/// Configuration options for persistence behavior.
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>
    /// Command timeout for EF Core database commands, in seconds.
    /// </summary>
    [Range(1, 600, ErrorMessage = "Persistence CommandTimeoutSeconds must be between 1 and 600.")]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts for transient database failures.
    /// </summary>
    [Range(0, 20, ErrorMessage = "Persistence MaxRetryCount must be between 0 and 20.")]
    public int MaxRetryCount { get; set; } = 5;
}

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
    [Required(ErrorMessage = "Persistence CommandTimeoutSeconds is required.")]
    [Range(1, 600, ErrorMessage = "Persistence CommandTimeoutSeconds must be between 1 and 600.")]
    public int? CommandTimeoutSeconds { get; set; }

    /// <summary>
    /// Maximum retry attempts for transient database failures.
    /// </summary>
    [Required(ErrorMessage = "Persistence MaxRetryCount is required.")]
    [Range(0, 20, ErrorMessage = "Persistence MaxRetryCount must be between 0 and 20.")]
    public int? MaxRetryCount { get; set; }
}

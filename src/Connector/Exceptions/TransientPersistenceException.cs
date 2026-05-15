namespace Connector;

/// <summary>
/// Represents a transient persistence failure that can be retried.
/// </summary>
public sealed class TransientPersistenceException : Exception
{
    public TransientPersistenceException()
    {
    }

    public TransientPersistenceException(string message)
        : base(message)
    {
    }

    public TransientPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

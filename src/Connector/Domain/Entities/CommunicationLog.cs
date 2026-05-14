namespace Connector.Domain.Entities;

public class CommunicationLog
{
    public int Id { get; set; }
    public Guid CorrelationId { get; set; }
    public required string MessageBody { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime HandledAt { get; set; }
    public required string Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SourceQueue { get; set; }
}

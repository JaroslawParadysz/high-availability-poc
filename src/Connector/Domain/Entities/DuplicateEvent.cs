namespace Connector.Domain.Entities;

public class DuplicateEvent
{
    public int Id { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string? SourceQueue { get; set; }
}

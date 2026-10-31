namespace BookingService.Entities;

public class OutboxMessage
{
    public long Id { get; set; }
    public string MessageType { get; set; }
    public string Payload { get; set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public OutboxMessage(string messageType, string payload, DateTimeOffset createdAt)
    {
        MessageType = messageType;
        Payload = payload;
        CreatedAt = createdAt;
    }

    public void MargAsProcessed(DateTimeOffset processedAt)
    {
        ProcessedAt = processedAt;
    }
}
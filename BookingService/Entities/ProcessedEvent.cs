namespace BookingService.Entities;

public class ProcessedEvent
{
    public Guid EventId { get; private set; }
    
    public DateTime ProcessedAt { get; private set; }

    public ProcessedEvent(Guid eventId,  DateTime processedAt)
    {
        EventId = eventId;
        ProcessedAt = processedAt;
    }
}
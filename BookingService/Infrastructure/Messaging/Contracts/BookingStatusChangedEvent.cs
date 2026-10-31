using BookingService.Entities;

namespace BookingService.Infrastructure.Messaging.Contracts;

public class BookingStatusChangedEvent
{
    public long BookingId { get; set; }
    public BookingStatus OldStatus { get; set; }
    public BookingStatus NewStatus { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
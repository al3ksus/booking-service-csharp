using BookingService.Entities;

namespace BookingService.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly HttpClient _client;
    private const string Url = "api/notifications";
    
    public NotificationService(HttpClient client)
    {
        _client = client;
    }

    public async Task NotifyStatusChangedAsync(long bookingId, BookingStatus oldStatus, BookingStatus newStatus)
    {
        var notification = new { bookingId, oldStatus, newStatus };
        await _client.PostAsync(Url, JsonContent.Create(notification));
    }
}
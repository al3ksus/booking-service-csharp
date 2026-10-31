using BookingService.Entities;

namespace BookingService.Infrastructure.Notifications;

public class NotificationService : INotificationService
{
    private readonly HttpClient _client;
    private readonly ILogger<NotificationService> _logger;
    private const string Url = "api/notifications";
    
    public NotificationService(HttpClient client, ILogger<NotificationService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task NotifyStatusChangedAsync(long bookingId, BookingStatus oldStatus, BookingStatus newStatus)
    {
        var notification = new { bookingId, oldStatus, newStatus };
        using var response = await _client.PostAsync(Url, JsonContent.Create(notification));
        
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Произошла ошибка при отправке уведомления. {Content}", await response.Content.ReadAsStringAsync());
    }
}
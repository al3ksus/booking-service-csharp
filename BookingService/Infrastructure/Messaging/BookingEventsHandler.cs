using BookingService.Infrastructure.Messaging.Contracts;
using Rebus.Handlers;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Обработчик событий от Catalog Service.
/// Принимает BookingJobConfirmed и BookingJobDenied, делегирует обработку в BookingService.
/// </summary>
public class BookingEventsHandler :
    IHandleMessages<BookingJobConfirmed>,
    IHandleMessages<BookingJobDenied>
{
    private readonly Services.BookingService _bookingService;
    private readonly ILogger<BookingEventsHandler> _logger;

    public BookingEventsHandler(Services.BookingService bookingService, ILogger<BookingEventsHandler> logger)
    {
        _bookingService = bookingService;
        _logger = logger;
    }

    public async Task Handle(BookingJobConfirmed message)
    {
        _logger.LogInformation(
            "Получено событие BookingJobConfirmed: eventId={EventId}, requestId={RequestId}",
            message.EventId, message.RequestId);

        await _bookingService.HandleBookingJobConfirmed(message.RequestId, message.EventId);
    }

    public async Task Handle(BookingJobDenied message)
    {
        _logger.LogInformation(
            "Получено событие BookingJobDenied: eventId={EventId}, requestId={RequestId}",
            message.EventId, message.RequestId);

        await _bookingService.HandleBookingJobDenied(message.RequestId, message.EventId);
    }
}

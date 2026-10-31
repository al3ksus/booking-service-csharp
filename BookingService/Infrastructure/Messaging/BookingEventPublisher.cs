using BookingService.Infrastructure.Messaging.Contracts;
using Rebus.Bus;

namespace BookingService.Infrastructure.Messaging;

/// <summary>
/// Сервис для публикации сообщений в RabbitMQ через Rebus.
/// Rebus автоматически устанавливает заголовки и маршрутизирует сообщения.
/// </summary>
public class BookingEventPublisher
{
    private readonly IBus _bus;
    private readonly ILogger<BookingEventPublisher> _logger;

    public BookingEventPublisher(IBus bus, ILogger<BookingEventPublisher> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    /// Публикует команду создания booking job в Catalog Service
    /// </summary>
    public async Task PublishCreateBookingJob(CreateBookingJobRequest request)
    {
        _logger.LogInformation(
            "Публикация команды CreateBookingJob: requestId={RequestId}, resourceId={ResourceId}, dates={Start} - {End}",
            request.RequestId, request.ResourceId, request.StartDate, request.EndDate);

        await _bus.Publish(request);

        _logger.LogInformation("Команда CreateBookingJob отправлена в RabbitMQ");
    }

    /// <summary>
    /// Публикует команду отмены booking job в Catalog Service
    /// </summary>
    public async Task PublishCancelBookingJob(CancelBookingJobByRequestIdRequest request)
    {
        _logger.LogInformation(
            "Публикация команды CancelBookingJob: requestId={RequestId}",
            request.RequestId);

        await _bus.Publish(request);

        _logger.LogInformation("Команда CancelBookingJob отправлена в RabbitMQ");
    }

    public async Task PublishStatusChanged(BookingStatusChangedEvent bookingStatusChanged)
    {
        _logger.LogInformation(
            "Публикация события BookingStatusChangedEvent: bookingId={BookingId}, newStatus={NewStatus}, oldStatus={OldStatus}",
            bookingStatusChanged.BookingId,  bookingStatusChanged.NewStatus, bookingStatusChanged.OldStatus);

        await _bus.Publish(bookingStatusChanged);

        _logger.LogInformation("Событие BookingStatusChangedEvent отправлено в RabbitMQ");
    }
}

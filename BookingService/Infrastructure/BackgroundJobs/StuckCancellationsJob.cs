using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;

namespace BookingService.Infrastructure.BackgroundJobs;

public class StuckCancellationsJob : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<StuckCancellationsJob> _logger;
    
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);

    public StuckCancellationsJob(IServiceScopeFactory serviceScopeFactory, ILogger<StuckCancellationsJob> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();

            var bookingRepository = scope.ServiceProvider.GetRequiredService<BookingRepository>();
            var bookingEventPublisher = scope.ServiceProvider.GetRequiredService<BookingEventPublisher>();

            var cancellationRequestedBefore = DateTimeOffset.UtcNow - _timeout;
            var bookings = await bookingRepository.FindStuckCancellationsAsync(cancellationRequestedBefore);

            foreach (var booking in bookings)
            {
                if (booking.CatalogRequestId.HasValue)
                    await bookingEventPublisher.PublishCancelBookingJob(
                        new CancelBookingJobByRequestIdRequest
                        {
                            RequestId = booking.CatalogRequestId.Value
                        });
            }
        }
    }
}
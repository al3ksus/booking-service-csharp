using BookingService.Entities;
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
            List<Booking> bookings = [];
            
            try
            {
                bookings.AddRange(await bookingRepository.FindStuckCancellationsAsync(cancellationRequestedBefore));
                
                _logger.LogInformation("Found {BookingsCount} stuck cancellations", bookings.Count);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while trying to get stuck cancellations");
            }

            if (bookings.Count == 0)
                continue;

            foreach (var booking in bookings)
            {
                if (!booking.CatalogRequestId.HasValue)
                    continue;
                
                try
                {
                    await bookingEventPublisher.PublishCancelBookingJob(
                        new CancelBookingJobByRequestIdRequest
                        {
                            RequestId = booking.CatalogRequestId.Value
                        });
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error while trying to cancel booking");
                }
            }
        }
    }
}
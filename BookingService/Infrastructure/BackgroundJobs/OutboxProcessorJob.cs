using System.Text.Json;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;
using Rebus.Bus;

namespace BookingService.Infrastructure.BackgroundJobs;

public class OutboxProcessorJob : BackgroundService
{
    private readonly ILogger<OutboxProcessorJob> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    
    private readonly IReadOnlyDictionary<string, Type> _messageTypes= new Dictionary<string, Type>
    {
        { typeof(CreateBookingJobRequest).FullName!, typeof(CreateBookingJobRequest) },
        { typeof(CancelBookingJobByRequestIdRequest).FullName!, typeof(CancelBookingJobByRequestIdRequest) },
        { typeof(BookingStatusChangedEvent).FullName!, typeof(BookingStatusChangedEvent) }
    };

    public OutboxProcessorJob(ILogger<OutboxProcessorJob> logger, IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessorJob Started");
        
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _serviceScopeFactory.CreateAsyncScope();

                var outboxMessagesRepository = scope.ServiceProvider.GetRequiredService<OutboxMessageRepository>();
                var bus = scope.ServiceProvider.GetRequiredService<IBus>();

                var messages = await outboxMessagesRepository.GetNonProcessedMessagesAsync();

                _logger.LogInformation("Non-processed messages count: {Count}", messages.Count);

                foreach (var message in messages)
                {
                    if (!_messageTypes.TryGetValue(message.MessageType, out var messageType))
                    {
                        _logger.LogWarning("Неизвестный тип сообщения. MessageId - {MessageId}", message.Id);

                        try
                        {
                            message.MargAsProcessed(DateTimeOffset.UtcNow);
                            await outboxMessagesRepository.SaveAsync(message);
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(
                                "Ошибка сохранения сообщения с неизвестным типом, Id - {MessageId}. {Message}",
                                message.Id, e.Message);
                        }

                        continue;
                    }

                    try
                    {
                        var request = JsonSerializer.Deserialize(message.Payload, messageType);
                        await bus.Publish(request);
                        message.MargAsProcessed(DateTimeOffset.UtcNow);
                        await outboxMessagesRepository.SaveAsync(message);

                        _logger.LogError("Сообщение с Id - {MessageId} Успешно обработано", message.Id);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError("Ошибка обработки сообщения с Id - {MessageId}. {Message}", message.Id,
                            e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
        }
    }
}
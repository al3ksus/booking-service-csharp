using System.Text.Json;
using BookingService.Configuration;
using BookingService.Dto.Response;
using BookingService.Entities;
using BookingService.Exceptions;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Data.Transactions;
using BookingService.Infrastructure.Messaging;
using BookingService.Infrastructure.Messaging.Contracts;
using BookingService.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BookingService.Services;

/// <summary>
/// Сервис для работы с бронированиями.
/// Объединяет CRUD операции, бизнес-логику и обработку событий от Catalog Service.
/// </summary>
public class BookingService
{
    private readonly BookingRepository _repository;
    private readonly ProcessedEventRepository _processedEventRepository;
    private readonly OutboxMessageRepository _outboxMessageRepository;
    private readonly BookingTransactionManager _transactionManager;
    private readonly BookingEventPublisher _publisher;
    private readonly ICurrentDateTimeProvider _dateTimeProvider;
    private readonly ILogger<BookingService> _logger;

    // [Task 09] Опциональная зависимость — существующие тесты создают сервис без неё
    private readonly INotificationService? _notificationService;

    // [Task 10] Опциональная зависимость — существующие тесты создают сервис без неё
    private readonly IMemoryCache? _cache;

    public BookingService(
        BookingRepository repository,
        BookingEventPublisher publisher,
        ICurrentDateTimeProvider dateTimeProvider,
        ILogger<BookingService> logger, ProcessedEventRepository processedEventRepository, BookingTransactionManager transactionManager, OutboxMessageRepository outboxMessageRepository, INotificationService? notificationService = null,
        IMemoryCache? cache = null)
    {
        _repository = repository;
        _publisher = publisher;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _processedEventRepository = processedEventRepository;
        _transactionManager = transactionManager;
        _outboxMessageRepository = outboxMessageRepository;
        _notificationService = notificationService;
        _cache = cache;
    }

    // === КОМАНДЫ (Use Cases) ===

    /// <summary>
    /// Создать новое бронирование.
    /// Отправляет асинхронную команду в Catalog Service для резервирования ресурса.
    /// </summary>
    /// <returns>ID созданного бронирования</returns>
    public async Task<long> CreateBooking(long userId, long resourceId, DateOnly bookedFrom, DateOnly bookedTo)
    {
        var now = _dateTimeProvider.UtcNow();
        var booking = Booking.Create(userId, resourceId, bookedFrom, bookedTo, now);

        var requestId = Guid.NewGuid();

        await using var transaction = await _transactionManager.BeginTransactionAsync();
        
        booking.SetCatalogRequestId(requestId);

        await _repository.SaveAsync(booking);

        var command = new CreateBookingJobRequest
        {
            EventId = Guid.NewGuid(),
            RequestId = requestId,
            ResourceId = booking.ResourceId,
            StartDate = booking.BookedFrom,
            EndDate = booking.BookedTo
        };

        var message = new OutboxMessage(
            typeof(CreateBookingJobRequest).FullName!,
            JsonSerializer.Serialize(command),
            _dateTimeProvider.UtcNow());

        await _outboxMessageRepository.SaveAsync(message);
        await transaction.CommitAsync();

        _logger.LogInformation("Создано бронирование с ID: {Id}, requestId: {RequestId}", booking.Id, requestId);

        return booking.Id;
    }

    /// <summary>
    /// Отменить бронирование.
    /// Для подтверждённых бронирований переходит в CancellationPending (Задача 01).
    /// </summary>
    public async Task CancelBooking(long id)
    {
        await using var transaction = await _transactionManager.BeginTransactionAsync();
        
        var booking = await _repository.FindByIdAsync(id)
            ?? throw new BusinessException($"Бронирование с указанным id: '{id}' не найдено.");

        var currentDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow().UtcDateTime);
        var oldStatus = booking.Status;
        booking.Cancel(currentDate);

        await _repository.SaveAsync(booking);

        var statusChangedEvent = new BookingStatusChangedEvent
        {
            BookingId = booking.Id,
            ChangedAt = _dateTimeProvider.UtcNow(),
            NewStatus = booking.Status,
            OldStatus = oldStatus
        };

        await _outboxMessageRepository.SaveAsync(new OutboxMessage(
            typeof(BookingStatusChangedEvent).FullName!,
            JsonSerializer.Serialize(statusChangedEvent),
            _dateTimeProvider.UtcNow()));   

        if (booking.CatalogRequestId is not null)
        {
            var command = new CancelBookingJobByRequestIdRequest
            {
                EventId = Guid.NewGuid(),
                RequestId = booking.CatalogRequestId.Value
            };

            await _outboxMessageRepository.SaveAsync(new OutboxMessage(
                typeof(CancelBookingJobByRequestIdRequest).FullName!,
                JsonSerializer.Serialize(command),
                _dateTimeProvider.UtcNow())); 
        }

        await transaction.CommitAsync();

        _logger.LogInformation("Инициирована отмена бронирования с ID: {Id}, новый статус: {Status}",
            id, booking.Status);
    }

    // === ЗАПРОСЫ (Queries) ===

    /// <summary>Получить бронирование по ID</summary>
    public async Task<Booking> GetById(long id)
        => await _repository.FindByIdAsync(id)
           ?? throw new BusinessException($"Бронирование с указанным id: '{id}' не найдено.");

    /// <summary>Получить бронирования по фильтрам с пагинацией</summary>
    public async Task<List<Booking>> GetByFilter(
        long? userId,
        long? resourceId,
        BookingStatus? status,
        int pageNumber,
        int pageSize)
        => await _repository.FindByFilterAsync(userId, resourceId, status, pageNumber, pageSize);

    /// <summary>Получить только статус бронирования по ID</summary>
    public async Task<BookingStatus?> GetStatusById(long id)
        => await _repository.FindStatusByIdAsync(id);

    // === EVENT HANDLERS (Обработка асинхронных событий от Catalog Service) ===

    /// <summary>
    /// Обработать событие подтверждения booking job от Catalog Service.
    /// Переводит бронирование в статус Confirmed.
    /// </summary>
    public async Task HandleBookingJobConfirmed(Guid requestId, Guid eventId)
    {
        _logger.LogInformation("Получено событие BookingJobConfirmed: requestId={RequestId}", requestId);
        
        var result = await ProcessEventAsync(eventId, async () =>
        {
            var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
            if (booking is null)
            {
                _logger.LogWarning("Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.", requestId);
                return null;
            }

            _logger.LogInformation("Найдено бронирование: id={Id}, статус={Status}. Подтверждаем...",
                booking.Id, booking.Status);

            var oldStatus = booking.Status;
            ConfirmBooking(booking);

            try
            {
                await _repository.SaveAsync(booking);
            }
            catch (DbUpdateConcurrencyException)
            {
                await _repository.ReloadAsync(booking);
                oldStatus = booking.Status;
                ConfirmBooking(booking);
                await _repository.SaveAsync(booking);
            }

            if (oldStatus == booking.Status)
                return booking;
            
            var statusChangedEvent = new BookingStatusChangedEvent
            {
                BookingId = booking.Id,
                ChangedAt = _dateTimeProvider.UtcNow(),
                NewStatus = booking.Status,
                OldStatus = oldStatus
            };

            await _outboxMessageRepository.SaveAsync(new OutboxMessage(
                typeof(BookingStatusChangedEvent).FullName!,
                JsonSerializer.Serialize(statusChangedEvent),
                _dateTimeProvider.UtcNow()));

            return booking;
        });
        
        if (result.IsSuccess && result.Booking is not null)
            _logger.LogInformation("Бронирование успешно подтверждено: id={Id}, новый статус={Status}",
                result.Booking.Id, result.Booking.Status);
    }

    /// <summary>
    /// Обработать событие отклонения booking job от Catalog Service.
    /// Отменяет бронирование.
    /// </summary>
    public async Task HandleBookingJobDenied(Guid requestId, Guid eventId)
    {
        _logger.LogInformation("Получено событие BookingJobDenied: requestId={RequestId}", requestId);
        
        var result = await ProcessEventAsync(eventId, async () =>
        {
            var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
            if (booking is null)
            {
                _logger.LogWarning("Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.", requestId);
                return null;
            }

            _logger.LogInformation("Найдено бронирование: id={Id}, статус={Status}. Отменяем...",
                booking.Id, booking.Status);

            var currentDate = DateOnly.FromDateTime(_dateTimeProvider.UtcNow().UtcDateTime);
            var oldStatus = booking.Status;
            booking.Cancel(currentDate);
            
            await _repository.SaveAsync(booking);
            
            var statusChangedEvent = new BookingStatusChangedEvent
            {
                BookingId = booking.Id,
                ChangedAt = _dateTimeProvider.UtcNow(),
                NewStatus = booking.Status,
                OldStatus = oldStatus
            };

            await _outboxMessageRepository.SaveAsync(new OutboxMessage(
                typeof(BookingStatusChangedEvent).FullName!,
                JsonSerializer.Serialize(statusChangedEvent),
                _dateTimeProvider.UtcNow()));

            return booking;
        });
        
        if (result.IsSuccess && result.Booking is not null)
            _logger.LogInformation("Бронирование успешно отменено: id={Id}, новый статус={Status}",
            result.Booking.Id, result.Booking.Status);
    }

    /// <summary>
    /// Откатить отмену бронирования.
    /// </summary>
    public async Task HandleCancellationError(Guid requestId, Guid eventId)
    {
        await ProcessEventAsync(eventId, async () =>
        {
            var booking = await _repository.FindByCatalogRequestIdAsync(requestId);
            if (booking is null)
            {
                _logger.LogWarning("Бронирование не найдено по requestId: {RequestId}. Событие проигнорировано.", requestId);
                return null;
            }

            var oldStatus = booking.Status;
            booking.RollbackCancellation();

            await _repository.SaveAsync(booking);

            var statusChangedEvent = new BookingStatusChangedEvent
            {
                BookingId = booking.Id,
                ChangedAt = _dateTimeProvider.UtcNow(),
                NewStatus = booking.Status,
                OldStatus = oldStatus
            };

            await _outboxMessageRepository.SaveAsync(new OutboxMessage(
            typeof(BookingStatusChangedEvent).FullName!,
            JsonSerializer.Serialize(statusChangedEvent),
            _dateTimeProvider.UtcNow()));

            return booking;
        });
    }

    private async Task<ProcessEventResult> ProcessEventAsync(Guid eventId, Func<Task<Booking?>> handle)
    {
        var processedEvent = await _processedEventRepository.FindByEventIdAsync(eventId);
        if (processedEvent is not null)
        {
            _logger.LogWarning("Сообщение с EventId {eventId} уже обработано.", eventId);
            return ProcessEventResult.Failed();
        }
        
        await using var transaction = await _transactionManager.BeginTransactionAsync();

        try
        {
            try
            {
                await _processedEventRepository.SaveAsync(
                    new ProcessedEvent(eventId, DateTimeOffset.UtcNow.UtcDateTime));
            }
            catch (DbUpdateException)
            {
                _logger.LogWarning("Сообщение с EventId {eventId} уже обработано.", eventId);
                await transaction.RollbackAsync();
                return ProcessEventResult.Failed();
            }

            var booking = await handle();

            if (booking == null)
            {
                await transaction.RollbackAsync();
                return ProcessEventResult.Failed();
            }
            
            await transaction.CommitAsync();

            return ProcessEventResult.Success(booking);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>Устаревший метод-заглушка — используйте HandleCancellationError</summary>
    // public Task HandleError(Guid requestId) => HandleCancellationError(requestId);

    public async Task<StatisticsResponse> GetStatistics()
    {
        return await _repository.GetStatisticsAsync();
    }

    public async Task<List<BookingStatusHistory>> GetBookingHistory(long bookingId)
    {
        return await _repository.GetBookingHistoryAsync(bookingId);
    }

    private bool ConfirmBooking(Booking booking)
    {
        if (booking.Status == BookingStatus.CancellationPending)
        {
            _logger.LogWarning("Ошибка при подтверждении бронирования. Бронирование находится в процессе отмены.");
            return false;
        }

        booking.Confirm();

        return true;
    }

    private record ProcessEventResult
    {
        public bool IsSuccess { get; set; }
        public Booking? Booking { get; set; }

        public static ProcessEventResult Success(Booking booking)
            => new ProcessEventResult { IsSuccess = true, Booking = booking };
        
        public static ProcessEventResult Failed()
            => new ProcessEventResult { IsSuccess = false, Booking = null };
    }
}

using BookingService.Configuration;
using BookingService.Entities;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Data.Transactions;
using BookingService.Infrastructure.Messaging;
using BookingService.Services;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 09 — Notification Service (HTTP-уведомления).
///
/// Проверяют:
/// 1. INotificationService вызывается при каждом изменении статуса бронирования
/// 2. Корректность передаваемых параметров (bookingId, oldStatus, newStatus)
/// 3. Graceful degradation: ошибка уведомления не ломает основной сценарий
/// 4. Уведомление НЕ отправляется при no-op операциях (бронирование не найдено)
///
/// Дополнительно (вне этих тестов): реальное HTTP-взаимодействие с Polly retry
/// можно проверить с помощью WireMock.Net — запускать WireMock-сервер в тесте
/// и настраивать временные ответы 500 для проверки стратегии повторных попыток.
/// </summary>
public class Task09_NotificationServiceTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Вспомогательный метод: создаёт BookingService с мок-уведомлениями
    // -----------------------------------------------------------------------

    private INotificationService CreateNotificationMock()
        => Substitute.For<INotificationService>();

    private Services.BookingService CreateServiceWithNotifications(INotificationService notificationService)
        => new Services.BookingService(
            new BookingRepository(Context),
            new BookingEventPublisher(BusMock, NullLogger<BookingEventPublisher>.Instance),
            new CurrentDateTimeProvider(),
            NullLogger<Services.BookingService>.Instance,
            new ProcessedEventRepository(Context),
            new BookingTransactionManager(Context),
            new OutboxMessageRepository(Context),
            notificationService: notificationService);

    // -----------------------------------------------------------------------
    // Уведомление при подтверждении бронирования
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobConfirmed_CallsNotificationService()
    {
        // Arrange
        var notificationMock = CreateNotificationMock();
        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act
        await service.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: уведомление отправлено ровно раз
        await notificationMock.Received(1)
            .NotifyStatusChangedAsync(bookingId, Arg.Any<BookingStatus>(), BookingStatus.Confirmed);
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_Notification_HasCorrectNewStatus()
    {
        // Arrange
        var notificationMock = CreateNotificationMock();
        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act
        await service.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: в уведомлении передан правильный новый статус
        await notificationMock.Received(1)
            .NotifyStatusChangedAsync(bookingId, BookingStatus.AwaitConfirmation, BookingStatus.Confirmed);
    }

    // -----------------------------------------------------------------------
    // Уведомление при отмене бронирования
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CancelBooking_Confirmed_CallsNotificationService()
    {
        // Arrange
        var notificationMock = CreateNotificationMock();
        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await service.HandleBookingJobConfirmed(catalogRequestId);
        notificationMock.ClearReceivedCalls();

        // Act
        await service.CancelBooking(bookingId);

        // Assert
        await notificationMock.Received(1)
            .NotifyStatusChangedAsync(bookingId, BookingStatus.Confirmed, BookingStatus.CancellationPending);
    }

    [Fact]
    public async Task HandleCancellationError_CallsNotificationService()
    {
        // Arrange: CancellationPending → Confirmed (компенсирующая транзакция)
        var notificationMock = CreateNotificationMock();
        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await service.HandleBookingJobConfirmed(catalogRequestId);
        await service.CancelBooking(bookingId);
        notificationMock.ClearReceivedCalls();

        // Act
        await service.HandleCancellationError(catalogRequestId);

        // Assert
        await notificationMock.Received(1)
            .NotifyStatusChangedAsync(bookingId, BookingStatus.CancellationPending, BookingStatus.Confirmed);
    }

    // -----------------------------------------------------------------------
    // Graceful degradation: ошибка уведомления не ломает основной сценарий
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenNotificationThrows_DoesNotPropagateException()
    {
        // Arrange: симулируем недоступность Notification Service
        var notificationMock = CreateNotificationMock();
        notificationMock.NotifyStatusChangedAsync(default, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("Notification Service unavailable"));

        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act
        var act = async () => await service.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: основной сценарий не должен падать из-за проблем с уведомлением
        await act.Should().NotThrowAsync(
            because: "Ошибка Notification Service (недоступность, таймаут, 5xx) " +
                     "должна обрабатываться gracefully — бронирование уже подтверждено");
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenNotificationThrows_BookingStatusIsStillConfirmed()
    {
        // Arrange
        var notificationMock = CreateNotificationMock();
        notificationMock.NotifyStatusChangedAsync(default, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("Notification Service unavailable"));

        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();

        // Act: уведомление падает, но операция всё равно должна завершиться
        await service.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: статус бронирования изменился, несмотря на ошибку уведомления
        var booking = await Context.Bookings.FindAsync(bookingId);
        booking!.Status.Should().Be(BookingStatus.Confirmed,
            because: "Статус бронирования должен быть сохранён в БД независимо от результата уведомления");
    }

    [Fact]
    public async Task CancelBooking_WhenNotificationThrows_DoesNotPropagateException()
    {
        // Arrange
        var notificationMock = CreateNotificationMock();
        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await service.HandleBookingJobConfirmed(catalogRequestId);

        notificationMock.NotifyStatusChangedAsync(default, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("503 Service Unavailable"));

        // Act
        var act = async () => await service.CancelBooking(bookingId);

        // Assert
        await act.Should().NotThrowAsync(
            because: "Недоступность Notification Service при отмене не должна откатывать отмену бронирования");
    }

    // -----------------------------------------------------------------------
    // Уведомление НЕ отправляется при no-op операциях
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenBookingNotFound_DoesNotCallNotificationService()
    {
        // Arrange
        var notificationMock = CreateNotificationMock();
        var service = CreateServiceWithNotifications(notificationMock);
        var unknownRequestId = Guid.NewGuid();

        // Act
        await service.HandleBookingJobConfirmed(unknownRequestId);

        // Assert
        await notificationMock.DidNotReceiveWithAnyArgs()
            .NotifyStatusChangedAsync(default, default, default);
    }

    [Fact]
    public async Task HandleBookingJobConfirmed_WhenBookingIsCancellationPending_DoesNotCallNotificationService()
    {
        // Arrange: гард от гонки — позднее подтверждение игнорируется
        var notificationMock = CreateNotificationMock();
        var service = CreateServiceWithNotifications(notificationMock);
        var (bookingId, catalogRequestId) = await CreateBookingAsync();
        await service.HandleBookingJobConfirmed(catalogRequestId);
        await service.CancelBooking(bookingId);
        notificationMock.ClearReceivedCalls();

        // Act: запоздалое повторное подтверждение — должно быть проигнорировано
        await service.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: раз статус не изменился — уведомление тоже не отправляется
        await notificationMock.DidNotReceiveWithAnyArgs()
            .NotifyStatusChangedAsync(default, default, default);
    }
}

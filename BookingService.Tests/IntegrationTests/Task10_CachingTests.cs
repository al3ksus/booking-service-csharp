using BookingService.Configuration;
using BookingService.Entities;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Data.Transactions;
using BookingService.Infrastructure.Messaging;
using BookingService.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Tests.IntegrationTests;

/// <summary>
/// Интеграционные тесты для Задачи 10 — Кэширование статистики.
///
/// Проверяют:
/// 1. GetStatistics() возвращает кэшированный результат при повторном вызове
/// 2. Создание бронирования инвалидирует кэш
/// 3. Отмена бронирования инвалидирует кэш
/// 4. Изменение статуса через событие инвалидирует кэш
/// 5. Без кэша каждый вызов GetStatistics() отражает актуальное состояние БД
///
/// Ключевой паттерн кэширования:
///   GET /statistics → если кэш есть → вернуть кэш
///                  → если нет → запрос к БД → сохранить в кэш → вернуть
/// Инвалидация при: CreateBooking, CancelBooking, HandleBookingJobConfirmed,
///                  HandleBookingJobDenied, HandleCancellationError
/// </summary>
public class Task10_CachingTests : IntegrationTestBase
{
    // -----------------------------------------------------------------------
    // Вспомогательные методы для создания сервиса с кэшем
    // -----------------------------------------------------------------------

    private static IMemoryCache CreateFreshCache()
        => new MemoryCache(new MemoryCacheOptions());

    private Services.BookingService CreateServiceWithCache(IMemoryCache cache)
        => new Services.BookingService(
            new BookingRepository(Context),
            new BookingEventPublisher(BusMock, NullLogger<BookingEventPublisher>.Instance),
            new CurrentDateTimeProvider(),
            NullLogger<Services.BookingService>.Instance,
            new ProcessedEventRepository(Context),
            new BookingTransactionManager(Context),
            cache: cache);

    // -----------------------------------------------------------------------
    // Кэш работает: повторный вызов возвращает кэшированный результат
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatistics_CalledTwice_SecondCallReturnsCachedResult()
    {
        // Arrange: создаём сервис с кэшем
        var cache = CreateFreshCache();
        var service = CreateServiceWithCache(cache);

        // Act: первый вызов — прогрев кэша
        var firstResult = await service.GetStatistics();
        firstResult.TotalCount.Should().Be(0, "precondition: БД пуста");

        // Вставляем запись напрямую в БД (обходя сервис, чтобы не инвалидировать кэш)
        await Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO bookings (status, user_id, resource_id, booked_from, booked_to, created_at) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            (int)BookingStatus.AwaitConfirmation, 99L, 99L,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(13)),
            DateTimeOffset.UtcNow);

        // Act: второй вызов — должен вернуть кэшированный результат (0), не актуальный из БД (1)
        var secondResult = await service.GetStatistics();

        // Assert
        secondResult.TotalCount.Should().Be(0,
            because: "Второй вызов GetStatistics должен вернуть кэшированный результат, " +
                     "а не читать из БД — прямая вставка не инвалидирует кэш");
    }

    // -----------------------------------------------------------------------
    // Инвалидация кэша при CreateBooking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatistics_AfterCreateBooking_ReturnsFreshResult()
    {
        // Arrange: прогреваем кэш (TotalCount = 0)
        var cache = CreateFreshCache();
        var service = CreateServiceWithCache(cache);

        var cachedStats = await service.GetStatistics();
        cachedStats.TotalCount.Should().Be(0, "precondition");

        // Act: CreateBooking должен инвалидировать кэш
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        await service.CreateBooking(1, 1, from, from.AddDays(3));

        // Assert: следующий вызов должен показать актуальные данные
        var freshStats = await service.GetStatistics();
        freshStats.TotalCount.Should().Be(1,
            because: "После CreateBooking кэш должен быть инвалидирован — " +
                     "GetStatistics должен вернуть актуальное число бронирований");
    }

    // -----------------------------------------------------------------------
    // Инвалидация кэша при CancelBooking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatistics_AfterCancelBooking_ReturnsFreshStatusBreakdown()
    {
        // Arrange: создаём бронирование и прогреваем кэш
        var cache = CreateFreshCache();
        var service = CreateServiceWithCache(cache);
        var (bookingId, _) = await CreateBookingAsync();

        // Первый вызов: кэш прогрет, статистика содержит AwaitConfirmation
        var beforeCancel = await service.GetStatistics();
        var awaitCountBefore = beforeCancel.ByStatus
            .FirstOrDefault(s => s.Status == BookingStatus.AwaitConfirmation)?.Count ?? 0;

        // Act: отмена → переводит в Cancelled → инвалидирует кэш
        await service.CancelBooking(bookingId);

        // Assert: следующий вызов должен отразить новый статус
        var afterCancel = await service.GetStatistics();
        var cancelledCount = afterCancel.ByStatus
            .FirstOrDefault(s => s.Status == BookingStatus.Cancelled)?.Count ?? 0;

        cancelledCount.Should().BeGreaterThan(0,
            because: "После CancelBooking кэш должен быть инвалидирован и возвращать " +
                     "актуальную разбивку по статусам, включая Cancelled");
    }

    // -----------------------------------------------------------------------
    // Инвалидация кэша при HandleBookingJobConfirmed
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatistics_AfterHandleBookingJobConfirmed_ReturnsFreshResult()
    {
        // Arrange
        var cache = CreateFreshCache();
        var service = CreateServiceWithCache(cache);
        var (_, catalogRequestId) = await CreateBookingAsync();

        // Прогрев кэша
        await service.GetStatistics();

        // Act
        await service.HandleBookingJobConfirmed(catalogRequestId);

        // Assert: разбивка по статусам обновилась
        var stats = await service.GetStatistics();
        var confirmedCount = stats.ByStatus
            .FirstOrDefault(s => s.Status == BookingStatus.Confirmed)?.Count ?? 0;

        confirmedCount.Should().BeGreaterThan(0,
            because: "После HandleBookingJobConfirmed кэш должен инвалидироваться, " +
                     "а статус Confirmed должен появиться в статистике");
    }

    // -----------------------------------------------------------------------
    // Без кэша каждый вызов актуален
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatistics_WithoutCache_AlwaysReturnsCurrentData()
    {
        // Arrange: сервис БЕЗ кэша (базовый BookingService из IntegrationTestBase)
        var statsBefore = await BookingService.GetStatistics();
        statsBefore.TotalCount.Should().Be(0, "precondition: БД пуста");

        // Act: создаём бронирование
        await CreateBookingAsync();

        // Assert: без кэша следующий вызов всегда актуален
        var statsAfter = await BookingService.GetStatistics();
        statsAfter.TotalCount.Should().Be(1,
            because: "Без кэша каждый GetStatistics() читает из БД и возвращает актуальные данные");
    }

    // -----------------------------------------------------------------------
    // Кэш работает для TopResources и ByStatus, а не только TotalCount
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatistics_CacheCoversAllFields_TopResourcesAlsoCached()
    {
        // Arrange: создаём бронирование для ресурса 42
        var cache = CreateFreshCache();
        var service = CreateServiceWithCache(cache);
        await CreateBookingAsync(resourceId: 42);

        // Прогрев кэша
        var cachedStats = await service.GetStatistics();
        cachedStats.TopResources.Should().Contain(r => r.ResourceId == 42, "precondition");

        // Вставляем бронирование для другого ресурса (100) напрямую в БД
        await Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO bookings (status, user_id, resource_id, booked_from, booked_to, created_at) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
            (int)BookingStatus.AwaitConfirmation, 88L, 100L,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(13)),
            DateTimeOffset.UtcNow);

        // Act: повторный вызов — должен вернуть кэш
        var stillCachedStats = await service.GetStatistics();

        // Assert: ресурс 100 не должен появиться (он был добавлен в обход кэша)
        stillCachedStats.TopResources.Should().NotContain(r => r.ResourceId == 100,
            because: "TopResources также должно кэшироваться — прямая вставка не инвалидирует кэш");
    }
}

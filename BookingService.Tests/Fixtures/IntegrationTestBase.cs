using BookingService.Configuration;
using BookingService.Infrastructure.Data;
using BookingService.Infrastructure.Data.Transactions;
using BookingService.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Rebus.Bus;
using Testcontainers.PostgreSql;

namespace BookingService.Tests.Fixtures;

/// <summary>
/// Базовый класс для интеграционных тестов.
/// Поднимает реальный PostgreSQL через Testcontainers, применяет миграции.
/// Каждый тестовый класс-наследник получает изолированный контейнер.
///
/// Использование:
///   public class MyTests : IntegrationTestBase
///   {
///       [Fact]
///       public async Task MyTest() { ... }
///   }
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("booking_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    protected BookingDbContext Context { get; private set; } = null!;
    protected Services.BookingService BookingService { get; private set; } = null!;
    protected IBus BusMock { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<BookingDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        Context = new BookingDbContext(options);

        // Применяем все миграции (включая те, что добавлены в задачах)
        await Context.Database.MigrateAsync();

        // IBus мокируем — не нужен реальный RabbitMQ
        BusMock = Substitute.For<IBus>();

        var publisher = new BookingEventPublisher(
            BusMock,
            NullLogger<BookingEventPublisher>.Instance);

        BookingService = new Services.BookingService(
            new BookingRepository(Context),
            publisher,
            new CurrentDateTimeProvider(),
            NullLogger<Services.BookingService>.Instance,
            new ProcessedEventRepository(Context),
            new BookingTransactionManager(Context));
    }

    public async Task DisposeAsync()
    {
        await Context.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Вспомогательный метод: создаёт бронирование через сервис и возвращает
    /// (bookingId, catalogRequestId) для последующих манипуляций в тестах.
    /// </summary>
    protected async Task<(long bookingId, Guid catalogRequestId)> CreateBookingAsync(
        long userId = 1,
        long resourceId = 1,
        int daysFromNow = 7,
        int durationDays = 3)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysFromNow));
        var to = from.AddDays(durationDays);

        var bookingId = await BookingService.CreateBooking(userId, resourceId, from, to);

        // Перезагружаем, чтобы получить CatalogRequestId присвоенный сервисом
        await Context.Entry(Context.Bookings.Local.First(b => b.Id == bookingId))
            .ReloadAsync();

        var booking = await Context.Bookings.FindAsync(bookingId);
        return (bookingId, booking!.CatalogRequestId!.Value);
    }

    /// <summary>
    /// Переводит бронирование в статус Confirmed через HandleBookingJobConfirmed
    /// </summary>
    protected async Task ConfirmBookingAsync(Guid catalogRequestId)
        => await BookingService.HandleBookingJobConfirmed(catalogRequestId);
}

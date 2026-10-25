using BookingService.Dto.Response;
using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

/// <summary>
/// Репозиторий для работы с бронированиями через EF Core
/// </summary>
public class BookingRepository
{
    private readonly BookingDbContext _context;

    public BookingRepository(BookingDbContext context)
    {
        _context = context;
    }

    public async Task<Booking?> FindByIdAsync(long id)
        => await _context.Bookings.FindAsync(id);

    public async Task<Booking?> FindByCatalogRequestIdAsync(Guid catalogRequestId)
        => await _context.Bookings.FirstOrDefaultAsync(b => b.CatalogRequestId == catalogRequestId);

    /// <summary>
    /// Найти бронирования по опциональным фильтрам с пагинацией
    /// </summary>
    public async Task<List<Booking>> FindByFilterAsync(
        long? userId,
        long? resourceId,
        BookingStatus? status,
        int pageNumber,
        int pageSize)
    {
        var query = _context.Bookings.AsQueryable();

        if (userId.HasValue)
            query = query.Where(b => b.UserId == userId.Value);

        if (resourceId.HasValue)
            query = query.Where(b => b.ResourceId == resourceId.Value);

        if (status.HasValue)
            query = query.Where(b => b.Status == status.Value);

        return await query
            .Skip(pageNumber * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    /// <summary>
    /// Получить только статус бронирования по ID
    /// </summary>
    public async Task<BookingStatus?> FindStatusByIdAsync(long id)
        => await _context.Bookings
            .Where(b => b.Id == id)
            .Select(b => (BookingStatus?)b.Status)
            .FirstOrDefaultAsync();

    public async Task SaveAsync(Booking booking)
    {
        if (booking.Id == 0)
            _context.Bookings.Add(booking);

        await _context.SaveChangesAsync();
    }

    public async Task<StatisticsResponse> GetStatisticsAsync()
    {
        var totalCount = await _context.Bookings.CountAsync();
        
        var byStatus = await _context.Bookings
            .GroupBy(b => b.Status)
            .Select(b => new StatusCount
            {
                Status = b.Key,
                Count = b.Count()
            })
            .ToListAsync();
        
        var topResources = await _context.Bookings
            .GroupBy(b => b.ResourceId)
            .OrderByDescending(b => b.Count())
            .Take(5)
            .Select(b => new ResourceCount
            {
                ResourceId = b.Key,
                BookingCount = b.Count()
            })
            .ToListAsync();

        return new StatisticsResponse
        {
            TotalCount = totalCount,
            ByStatus = byStatus,
            TopResources = topResources
        };
    }

    public Task<List<Booking>> FindStuckCancellationsAsync(DateTimeOffset cancellationRequestedBefore)
    {
        var now = DateTimeOffset.UtcNow;
        
        return _context.Bookings
            .Where(b => b.Status == BookingStatus.CancellationPending)
            .Where(b => b.CancellationRequestedAt <= cancellationRequestedBefore)
            .ToListAsync();
    }

    public async Task ReloadAsync(Booking booking)
    {
        await _context.Entry(booking).ReloadAsync();
    }

    public async Task<List<BookingStatusHistory>> GetBookingHistoryAsync(long bookingId)
    {
        return await _context.BookingStatusHistories
            .Where(h => h.Id == bookingId)
            .ToListAsync();
    }
}

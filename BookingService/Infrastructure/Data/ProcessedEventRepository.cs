using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

public class ProcessedEventRepository
{
    private readonly BookingDbContext _context;

    public ProcessedEventRepository(BookingDbContext context)
    {
        _context = context;
    }

    public async Task<ProcessedEvent?> FindByEventIdAsync(Guid eventId)
    {
        return await _context.ProcessedEvents
            .Where(e => e.EventId == eventId)
            .FirstOrDefaultAsync();
    }
    
    public async Task SaveAsync(ProcessedEvent processedEvent)
    {
        _context.ProcessedEvents.Add(processedEvent);

        await _context.SaveChangesAsync();
    }
}
using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

public class OutboxMessageRepository
{
    private readonly BookingDbContext _context;

    public OutboxMessageRepository(BookingDbContext context)
    {
        _context = context;
    }

    public async Task<List<OutboxMessage>> GetNonProcessedMessagesAsync()
    {
        return await _context.OutboxMessages
            .Where(m => !m.ProcessedAt.HasValue)
            .ToListAsync();
    }
    
    public async Task SaveAsync(OutboxMessage message)
    {
        if (message.Id == 0)
            await _context.OutboxMessages.AddAsync(message);
        
        await _context.SaveChangesAsync();
    }
}
using Microsoft.EntityFrameworkCore.Storage;

namespace BookingService.Infrastructure.Data.Transactions;

public class BookingTransactionManager
{
    private readonly BookingDbContext _context;

    public BookingTransactionManager(BookingDbContext context)
    {
        _context = context;
    }

    public async Task<BookingTransaction> BeginTransactionAsync()
    {
        return new BookingTransaction(await _context.Database.BeginTransactionAsync());
    }
}
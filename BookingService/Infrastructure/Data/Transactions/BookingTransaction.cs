using System.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace BookingService.Infrastructure.Data.Transactions;

public class BookingTransaction
{
    private readonly IDbContextTransaction _transaction;

    public BookingTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public async Task CommitAsync()
    {
        await _transaction.CommitAsync();
    }
    
    public async Task RollbackAsync()
    {
        await _transaction.RollbackAsync();
    }
}
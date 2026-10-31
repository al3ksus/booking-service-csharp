using Microsoft.EntityFrameworkCore;

namespace BookingService.Tests.Fixtures;

public class BookingIndexesTest : IntegrationTestBase
{
    [Fact]
    public async Task IsIndexExists_Booking_CatalogRequestId()
    {
        var indexExists = await Context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int FROM pg_indexes " +
            "WHERE tablename = 'bookings' AND indexname = 'idx_bookings_catalog_request_id'")
            .FirstOrDefaultAsync();
        
        Assert.Equal(1, indexExists);
    }
    
    [Fact]
    public async Task IsIndexExists_Booking_UserId_Status()
    {
        var indexExists = await Context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int FROM pg_indexes " +
                "WHERE tablename = 'bookings' AND indexname = 'idx_bookings_user_id_status'")
            .FirstOrDefaultAsync();
        
        Assert.Equal(1, indexExists);
    }
    
    [Fact]
    public async Task IsIndexExists_Booking_ResourceId_Dates()
    {
        var indexExists = await Context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int FROM pg_indexes " +
                "WHERE tablename = 'bookings' AND indexname = 'idx_bookings_resource_id_dates'")
            .FirstOrDefaultAsync();
        
        Assert.Equal(1, indexExists);
    }
    
    [Fact]
    public async Task IsIndexExists_Booking_CreatedAt()
    {
        var indexExists = await Context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int FROM pg_indexes " +
                "WHERE tablename = 'bookings' AND indexname = 'idx_bookings_created_at'")
            .FirstOrDefaultAsync();
        
        Assert.Equal(1, indexExists);
    }
}
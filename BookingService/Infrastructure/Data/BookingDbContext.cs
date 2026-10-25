using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

public class BookingDbContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingStatusHistory> BookingStatusHistories => Set<BookingStatusHistory>();

    public BookingDbContext(DbContextOptions<BookingDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("bookings");

            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(b => b.Status)
                .HasColumnName("status")
                .HasConversion<int>()
                .IsRequired();

            entity.Property(b => b.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(b => b.ResourceId)
                .HasColumnName("resource_id")
                .IsRequired();

            entity.Property(b => b.BookedFrom)
                .HasColumnName("booked_from")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(b => b.BookedTo)
                .HasColumnName("booked_to")
                .HasColumnType("date")
                .IsRequired();

            entity.Property(b => b.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(b => b.CatalogRequestId)
                .HasColumnName("catalog_request_id")
                .HasColumnType("uuid");

            entity.Property(b => b.CancellationRequestedAt)
                .HasColumnName("cancellation_requested_at")
                .HasColumnType("timestamp with time zone");

            entity.Property(b => b.Version)
                .HasColumnName("xmin")
                .IsRowVersion();

            entity.HasIndex(b => b.Status)
                .HasDatabaseName("idx_bookings_status");

            entity.HasIndex(b => b.UserId)
                .HasDatabaseName("idx_bookings_user_id");

            entity.HasIndex(b => b.ResourceId)
                .HasDatabaseName("idx_bookings_resource_id");

            entity.HasIndex(b => b.CancellationRequestedAt)
                .HasDatabaseName("idx_bookings_cancellation_pending")
                .HasFilter("status = 4");
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.ToTable("booking_status_history");

            entity.HasKey(b => b.Id);

            entity.Property(e => e.BookingId)
                .HasColumnName("booking_id")
                .IsRequired();

            entity.Property(e => e.ChangedAt)
                .HasColumnName("changed_at")
                .IsRequired();

            entity.Property(e => e.StatusFrom)
                .HasColumnName("status_from");

            entity.Property(e => e.StatusTo)
                .HasColumnName("status_to")
                .IsRequired();
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var changed = ChangeTracker.Entries<Booking>()
            .Where(h => h.State is EntityState.Added or EntityState.Modified)
            .ToList();
        
        foreach (var bookingEntry in changed)
        {
            var booking = bookingEntry.Entity;
            var statusProperty = bookingEntry.Property(b => b.Status);

            if (statusProperty.CurrentValue == statusProperty.OriginalValue && bookingEntry.State == EntityState.Modified)
                continue;
            
            BookingStatusHistories.Add(BookingStatusHistory.Create(
                booking.Id,
                bookingEntry.State == EntityState.Modified ? statusProperty.OriginalValue : null,
                statusProperty.CurrentValue));
        }
        
        return await base.SaveChangesAsync(cancellationToken);
    }
}

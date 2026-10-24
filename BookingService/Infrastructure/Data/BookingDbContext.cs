using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

public class BookingDbContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();

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
    }
}

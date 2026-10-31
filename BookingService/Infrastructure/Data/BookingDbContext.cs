using BookingService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Infrastructure.Data;

public class BookingDbContext : DbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingStatusHistory> BookingStatusHistories => Set<BookingStatusHistory>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

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
            
            entity.HasIndex(b => b.CancellationRequestedAt)
                .HasDatabaseName("idx_bookings_catalog_request_id")
                .IsUnique();

            entity.HasIndex(b => new { b.UserId, b.Status })
                .HasDatabaseName("idx_bookings_user_id_status");
            
            entity.HasIndex(b => new { b.ResourceId, b.BookedFrom, b.BookedTo })
                .HasDatabaseName("idx_bookings_resource_id_dates");
            
            entity.HasIndex(b => b.CreatedAt)
                .HasDatabaseName("idx_bookings_created_at")
                .IsDescending();
        });

        modelBuilder.Entity<BookingStatusHistory>(entity =>
        {
            entity.ToTable("booking_status_history");

            entity.HasKey(b => b.Id);

            entity.Property(e => e.BookingId)
                .HasColumnName("booking_id")
                .IsRequired();

            entity.HasOne(h => h.Booking)
                .WithMany()
                .HasForeignKey(h => h.BookingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.ChangedAt)
                .HasColumnName("changed_at")
                .IsRequired();

            entity.Property(e => e.StatusFrom)
                .HasColumnName("status_from");

            entity.Property(e => e.StatusTo)
                .HasColumnName("status_to")
                .IsRequired();
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events");

            entity.Property(e => e.EventId)
                .HasColumnName("event_id")
                .HasColumnType("uuid");

            entity.HasKey(e => e.EventId);
            
            entity.Property(e => e.ProcessedAt)
                .HasColumnName("processed_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.MessageType)
                .HasColumnName("message_type")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.Payload)
                .HasColumnName("payload")
                .HasColumnType("jsonb")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(e => e.ProcessedAt)
                .HasColumnName("processed_at")
                .HasColumnType("timestamp with time zone");
            
            entity.HasIndex(b => b.ProcessedAt)
                .HasDatabaseName("idx_outbox_messages_non_processed")
                .HasFilter("processed_at IS NULL");
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
                booking,
                bookingEntry.State == EntityState.Modified ? statusProperty.OriginalValue : null,
                statusProperty.CurrentValue));
        }
        
        return await base.SaveChangesAsync(cancellationToken);
    }
}

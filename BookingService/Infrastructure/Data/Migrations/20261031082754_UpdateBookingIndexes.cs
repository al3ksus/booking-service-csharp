using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBookingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_bookings_catalog_request_id",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "idx_bookings_cancellation_pending",
                table: "bookings",
                column: "cancellation_requested_at",
                filter: "status = 4");

            migrationBuilder.CreateIndex(
                name: "idx_bookings_catalog_request_id",
                table: "bookings",
                column: "catalog_request_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_bookings_cancellation_pending",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "idx_bookings_catalog_request_id",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "idx_bookings_catalog_request_id",
                table: "bookings",
                column: "cancellation_requested_at",
                unique: true,
                filter: "status = 4");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingStatusHistoryFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_booking_status_history_booking_id",
                table: "booking_status_history",
                column: "booking_id");

            migrationBuilder.AddForeignKey(
                name: "FK_booking_status_history_bookings_booking_id",
                table: "booking_status_history",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_booking_status_history_bookings_booking_id",
                table: "booking_status_history");

            migrationBuilder.DropIndex(
                name: "IX_booking_status_history_booking_id",
                table: "booking_status_history");
        }
    }
}

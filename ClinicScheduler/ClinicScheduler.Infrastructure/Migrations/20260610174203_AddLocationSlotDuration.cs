using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationSlotDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SlotDurationMinutes",
                table: "Locations",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Location_SlotDuration",
                table: "Locations",
                sql: "\"SlotDurationMinutes\" BETWEEN 5 AND 240");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Location_SlotDuration",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "SlotDurationMinutes",
                table: "Locations");
        }
    }
}

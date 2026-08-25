using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFhirIdsToTherapistLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FhirId",
                table: "Therapists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FhirId",
                table: "Locations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FhirId",
                table: "Therapists");

            migrationBuilder.DropColumn(
                name: "FhirId",
                table: "Locations");
        }
    }
}

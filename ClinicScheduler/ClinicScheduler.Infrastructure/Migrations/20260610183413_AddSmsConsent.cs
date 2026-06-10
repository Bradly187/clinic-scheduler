using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SmsConsentUpdatedAt",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmsRemindersConsent",
                table: "Patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmsConsentUpdatedAt",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SmsRemindersConsent",
                table: "Patients");
        }
    }
}

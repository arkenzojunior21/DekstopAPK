using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laundry.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncRetryBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                table: "SyncOutbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                table: "SyncInbox",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                table: "SyncOutbox");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                table: "SyncInbox");
        }
    }
}

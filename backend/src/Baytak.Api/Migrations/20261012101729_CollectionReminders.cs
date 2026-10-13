using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baytak.Wallet.Api.Migrations
{
    /// <inheritdoc />
    public partial class CollectionReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderAt",
                table: "Receipts",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReminderAt",
                table: "Receipts");
        }
    }
}

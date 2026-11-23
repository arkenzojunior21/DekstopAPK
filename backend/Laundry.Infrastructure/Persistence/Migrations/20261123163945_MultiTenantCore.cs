using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laundry.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SyncOutbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SyncInbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SyncConflicts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SyncCheckpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Services",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Refunds",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ReceiptSnapshots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PickupDeliverySlots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NotificationOutbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "FulfillmentTasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CreditLedger",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Complaints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ComplaintMessages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BackupHistories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AppSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Nama = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Alamat = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Telepon = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    TemaWarna = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncOutbox_TenantId",
                table: "SyncOutbox",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncInbox_TenantId",
                table: "SyncInbox",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_AggregateType",
                table: "SyncConflicts",
                column: "AggregateType");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflicts_TenantId",
                table: "SyncConflicts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_TenantId",
                table: "SyncCheckpoints",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_TenantId",
                table: "Services",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_TenantId",
                table: "Refunds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptSnapshots_TenantId",
                table: "ReceiptSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PickupDeliverySlots_TenantId",
                table: "PickupDeliverySlots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                table: "Payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId",
                table: "Orders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_TenantId",
                table: "NotificationOutbox",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FulfillmentTasks_TenantId",
                table: "FulfillmentTasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_TenantId",
                table: "CreditLedger",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_TenantId",
                table: "Complaints",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintMessages_TenantId",
                table: "ComplaintMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupHistories_TenantId",
                table: "BackupHistories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_TenantId",
                table: "AppSettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AppSettings_Tenants_TenantId",
                table: "AppSettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Tenants_TenantId",
                table: "AuditLogs",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BackupHistories_Tenants_TenantId",
                table: "BackupHistories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplaintMessages_Tenants_TenantId",
                table: "ComplaintMessages",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Complaints_Tenants_TenantId",
                table: "Complaints",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditLedger_CustomerProfiles_CustomerId",
                table: "CreditLedger",
                column: "CustomerId",
                principalTable: "CustomerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditLedger_Tenants_TenantId",
                table: "CreditLedger",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FulfillmentTasks_Tenants_TenantId",
                table: "FulfillmentTasks",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationOutbox_Tenants_TenantId",
                table: "NotificationOutbox",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Tenants_TenantId",
                table: "Orders",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Tenants_TenantId",
                table: "Payments",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PickupDeliverySlots_Tenants_TenantId",
                table: "PickupDeliverySlots",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReceiptSnapshots_Tenants_TenantId",
                table: "ReceiptSnapshots",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Refunds_Tenants_TenantId",
                table: "Refunds",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Services_Tenants_TenantId",
                table: "Services",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncCheckpoints_Tenants_TenantId",
                table: "SyncCheckpoints",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncConflicts_Tenants_TenantId",
                table: "SyncConflicts",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncInbox_Tenants_TenantId",
                table: "SyncInbox",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SyncOutbox_Tenants_TenantId",
                table: "SyncOutbox",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppSettings_Tenants_TenantId",
                table: "AppSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Tenants_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_BackupHistories_Tenants_TenantId",
                table: "BackupHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_ComplaintMessages_Tenants_TenantId",
                table: "ComplaintMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Complaints_Tenants_TenantId",
                table: "Complaints");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditLedger_CustomerProfiles_CustomerId",
                table: "CreditLedger");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditLedger_Tenants_TenantId",
                table: "CreditLedger");

            migrationBuilder.DropForeignKey(
                name: "FK_FulfillmentTasks_Tenants_TenantId",
                table: "FulfillmentTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_NotificationOutbox_Tenants_TenantId",
                table: "NotificationOutbox");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Tenants_TenantId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Tenants_TenantId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PickupDeliverySlots_Tenants_TenantId",
                table: "PickupDeliverySlots");

            migrationBuilder.DropForeignKey(
                name: "FK_ReceiptSnapshots_Tenants_TenantId",
                table: "ReceiptSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_Refunds_Tenants_TenantId",
                table: "Refunds");

            migrationBuilder.DropForeignKey(
                name: "FK_Services_Tenants_TenantId",
                table: "Services");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncCheckpoints_Tenants_TenantId",
                table: "SyncCheckpoints");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncConflicts_Tenants_TenantId",
                table: "SyncConflicts");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncInbox_Tenants_TenantId",
                table: "SyncInbox");

            migrationBuilder.DropForeignKey(
                name: "FK_SyncOutbox_Tenants_TenantId",
                table: "SyncOutbox");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Tenants_TenantId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_SyncOutbox_TenantId",
                table: "SyncOutbox");

            migrationBuilder.DropIndex(
                name: "IX_SyncInbox_TenantId",
                table: "SyncInbox");

            migrationBuilder.DropIndex(
                name: "IX_SyncConflicts_AggregateType",
                table: "SyncConflicts");

            migrationBuilder.DropIndex(
                name: "IX_SyncConflicts_TenantId",
                table: "SyncConflicts");

            migrationBuilder.DropIndex(
                name: "IX_SyncCheckpoints_TenantId",
                table: "SyncCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_Services_TenantId",
                table: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_TenantId",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptSnapshots_TenantId",
                table: "ReceiptSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_PickupDeliverySlots_TenantId",
                table: "PickupDeliverySlots");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutbox_TenantId",
                table: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_FulfillmentTasks_TenantId",
                table: "FulfillmentTasks");

            migrationBuilder.DropIndex(
                name: "IX_CreditLedger_TenantId",
                table: "CreditLedger");

            migrationBuilder.DropIndex(
                name: "IX_Complaints_TenantId",
                table: "Complaints");

            migrationBuilder.DropIndex(
                name: "IX_ComplaintMessages_TenantId",
                table: "ComplaintMessages");

            migrationBuilder.DropIndex(
                name: "IX_BackupHistories_TenantId",
                table: "BackupHistories");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_TenantId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SyncOutbox");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SyncInbox");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SyncConflicts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SyncCheckpoints");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReceiptSnapshots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PickupDeliverySlots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NotificationOutbox");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FulfillmentTasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CreditLedger");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ComplaintMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BackupHistories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AppSettings");
        }
    }
}

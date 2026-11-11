using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laundry.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncEventSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nomor event dari sequence database. Baris lama dinomori ulang urut waktu dibuat
            // agar kelanjutan sequence tidak menabrak angka yang sudah dipakai.
            migrationBuilder.Sql("""
                CREATE SEQUENCE IF NOT EXISTS sync_event_sequence;
                WITH terurut AS (
                    SELECT "Id" AS id, ROW_NUMBER() OVER (ORDER BY "Sequence", "CreatedAtUtc", "EventId") AS nomor
                    FROM "SyncOutbox"
                )
                UPDATE "SyncOutbox" o SET "Sequence" = t.nomor FROM terurut t WHERE o."Id" = t.id;
                SELECT setval('sync_event_sequence',
                    (SELECT COALESCE(MAX("Sequence"), 0) + 1 FROM "SyncOutbox"), false);
                """);

            migrationBuilder.AlterColumn<long>(
                name: "Sequence",
                table: "SyncOutbox",
                type: "bigint",
                nullable: false,
                defaultValueSql: "nextval('sync_event_sequence')",
                oldClrType: typeof(long),
                oldType: "bigint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "Sequence",
                table: "SyncOutbox",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldDefaultValueSql: "nextval('sync_event_sequence')");

            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS sync_event_sequence;");
        }
    }
}

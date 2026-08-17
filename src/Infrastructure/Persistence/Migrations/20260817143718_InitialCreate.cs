using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kart.Shipping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.entry_id);
                });

            migrationBuilder.CreateTable(
                name: "shipment_idempotency_keys",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_idempotency_keys", x => x.idempotency_key);
                });

            migrationBuilder.CreateTable(
                name: "shipment_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    outbox_seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    projected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    carrier = table.Column<string>(type: "text", nullable: true),
                    tracking_id = table.Column<string>(type: "text", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_audit_log_entity",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "idx_shipment_outbox_shipment",
                table: "shipment_outbox",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_order_id",
                table: "shipments",
                column: "order_id",
                unique: true);

            // --- Raw SQL below: everything EF Core's fluent API has no first-class mapping for,
            // ported verbatim from database-design.md (CHECK constraints, the status-guard
            // trigger) plus the three outbox partial indexes each background worker/poller claims
            // its own rows from via `SELECT ... FOR UPDATE SKIP LOCKED` (contracts/README.md's
            // outbox_seq addition orders all three, including the new read-model-projection one).

            migrationBuilder.Sql("""
                ALTER TABLE shipments ADD CONSTRAINT chk_shipment_status_shape CHECK (
                    (status = 'Pending'    AND carrier IS NULL     AND tracking_id IS NULL     AND failure_reason IS NULL)
                 OR (status = 'Dispatched' AND carrier IS NOT NULL AND tracking_id IS NOT NULL AND failure_reason IS NULL)
                 OR (status = 'Failed'     AND carrier IS NULL     AND tracking_id IS NULL     AND failure_reason IS NOT NULL)
                );
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION enforce_shipment_status_transition() RETURNS trigger AS $$
                BEGIN
                    IF OLD.status IN ('Dispatched', 'Failed') AND NEW.status <> OLD.status THEN
                        RAISE EXCEPTION 'illegal ShipmentStatus transition: % is terminal, cannot move to %', OLD.status, NEW.status;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_shipments_status_guard
                    BEFORE UPDATE OF status ON shipments
                    FOR EACH ROW EXECUTE FUNCTION enforce_shipment_status_transition();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE shipment_outbox ADD CONSTRAINT chk_shipment_outbox_message_type CHECK (
                    message_type IN ('CarrierCallRequested', 'ShipmentDispatched', 'ShipmentCreationFailed')
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_shipment_outbox_pending_carrier_calls ON shipment_outbox (outbox_seq)
                    WHERE message_type = 'CarrierCallRequested' AND processed_at IS NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_shipment_outbox_pending_publish ON shipment_outbox (outbox_seq)
                    WHERE message_type IN ('ShipmentDispatched', 'ShipmentCreationFailed') AND processed_at IS NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX idx_shipment_outbox_pending_projection ON shipment_outbox (outbox_seq)
                    WHERE projected_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "shipment_idempotency_keys");

            migrationBuilder.DropTable(
                name: "shipment_outbox");

            migrationBuilder.DropTable(
                name: "shipments");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS enforce_shipment_status_transition() CASCADE;");
        }
    }
}

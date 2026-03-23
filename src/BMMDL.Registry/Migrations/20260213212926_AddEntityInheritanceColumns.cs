using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMMDL.Registry.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityInheritanceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BodyDefinitionHash",
                schema: "registry",
                table: "service_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BodyRootStatementId",
                schema: "registry",
                table: "service_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                schema: "registry",
                table: "service_operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Cardinality",
                schema: "registry",
                table: "entity_associations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxCardinality",
                schema: "registry",
                table: "entity_associations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinCardinality",
                schema: "registry",
                table: "entity_associations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DiscriminatorValue",
                schema: "registry",
                table: "entities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAbstract",
                schema: "registry",
                table: "entities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParentEntityName",
                schema: "registry",
                table: "entities",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "service_event_handlers",
                schema: "registry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventName = table.Column<string>(type: "text", nullable: false),
                    BodyDefinitionHash = table.Column<string>(type: "text", nullable: true),
                    BodyRootStatementId = table.Column<Guid>(type: "uuid", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_event_handlers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_service_event_handlers_services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "registry",
                        principalTable: "services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_service_event_handlers_statement_nodes_BodyRootStatementId",
                        column: x => x.BodyRootStatementId,
                        principalSchema: "registry",
                        principalTable: "statement_nodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "service_operation_emits",
                schema: "registry",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventName = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_operation_emits", x => new { x.OperationId, x.EventName });
                    table.ForeignKey(
                        name: "FK_service_operation_emits_service_operations_OperationId",
                        column: x => x.OperationId,
                        principalSchema: "registry",
                        principalTable: "service_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_event_handlers_BodyRootStatementId",
                schema: "registry",
                table: "service_event_handlers",
                column: "BodyRootStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_service_event_handlers_ServiceId_EventName",
                schema: "registry",
                table: "service_event_handlers",
                columns: new[] { "ServiceId", "EventName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_event_handlers",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "service_operation_emits",
                schema: "registry");

            migrationBuilder.DropColumn(
                name: "BodyDefinitionHash",
                schema: "registry",
                table: "service_operations");

            migrationBuilder.DropColumn(
                name: "BodyRootStatementId",
                schema: "registry",
                table: "service_operations");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "registry",
                table: "service_operations");

            migrationBuilder.DropColumn(
                name: "Cardinality",
                schema: "registry",
                table: "entity_associations");

            migrationBuilder.DropColumn(
                name: "MaxCardinality",
                schema: "registry",
                table: "entity_associations");

            migrationBuilder.DropColumn(
                name: "MinCardinality",
                schema: "registry",
                table: "entity_associations");

            migrationBuilder.DropColumn(
                name: "DiscriminatorValue",
                schema: "registry",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "IsAbstract",
                schema: "registry",
                table: "entities");

            migrationBuilder.DropColumn(
                name: "ParentEntityName",
                schema: "registry",
                table: "entities");
        }
    }
}

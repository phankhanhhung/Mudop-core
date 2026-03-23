using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMMDL.Registry.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumnsFromAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcludedFieldsJson",
                schema: "registry",
                table: "views",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProjection",
                schema: "registry",
                table: "views",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParsedSelectJson",
                schema: "registry",
                table: "views",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectionEntityName",
                schema: "registry",
                table: "views",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectionFieldsJson",
                schema: "registry",
                table: "views",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForEntityName",
                schema: "registry",
                table: "services",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcludeFieldsJson",
                schema: "registry",
                table: "service_exposed_entities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IncludeFieldsJson",
                schema: "registry",
                table: "service_exposed_entities",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "registry",
                table: "modules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportsJson",
                schema: "registry",
                table: "modules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishesJson",
                schema: "registry",
                table: "modules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TenantAware",
                schema: "registry",
                table: "modules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Expression",
                schema: "registry",
                table: "entity_indexes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComputedStrategy",
                schema: "registry",
                table: "entity_fields",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnDeleteAction",
                schema: "registry",
                table: "entity_associations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MigrationDefRecordId",
                schema: "registry",
                table: "annotations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                schema: "registry",
                table: "access_rules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConditionExprRootId",
                schema: "registry",
                table: "access_field_restrictions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "migration_defs",
                schema: "registry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NamespaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    QualifiedName = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: true),
                    Author = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Breaking = table.Column<bool>(type: "boolean", nullable: false),
                    DependenciesJson = table.Column<string>(type: "jsonb", nullable: true),
                    SourceFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartLine = table.Column<int>(type: "integer", nullable: true),
                    EndLine = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_defs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_migration_defs_modules_ModuleId",
                        column: x => x.ModuleId,
                        principalSchema: "registry",
                        principalTable: "modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_migration_defs_namespaces_NamespaceId",
                        column: x => x.NamespaceId,
                        principalSchema: "registry",
                        principalTable: "namespaces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_migration_defs_source_files_SourceFileId",
                        column: x => x.SourceFileId,
                        principalSchema: "registry",
                        principalTable: "source_files",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_migration_defs_tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "registry",
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "migration_steps",
                schema: "registry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationDefId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    StepType = table.Column<string>(type: "text", nullable: false),
                    StepJson = table.Column<string>(type: "jsonb", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_migration_steps_migration_defs_MigrationDefId",
                        column: x => x.MigrationDefId,
                        principalSchema: "registry",
                        principalTable: "migration_defs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_annotations_MigrationDefRecordId",
                schema: "registry",
                table: "annotations",
                column: "MigrationDefRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_access_field_restrictions_ConditionExprRootId",
                schema: "registry",
                table: "access_field_restrictions",
                column: "ConditionExprRootId");

            migrationBuilder.CreateIndex(
                name: "IX_migration_defs_ModuleId",
                schema: "registry",
                table: "migration_defs",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_migration_defs_NamespaceId",
                schema: "registry",
                table: "migration_defs",
                column: "NamespaceId");

            migrationBuilder.CreateIndex(
                name: "IX_migration_defs_SourceFileId",
                schema: "registry",
                table: "migration_defs",
                column: "SourceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_migration_defs_TenantId_ModuleId_QualifiedName",
                schema: "registry",
                table: "migration_defs",
                columns: new[] { "TenantId", "ModuleId", "QualifiedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_migration_steps_MigrationDefId_Direction_Position",
                schema: "registry",
                table: "migration_steps",
                columns: new[] { "MigrationDefId", "Direction", "Position" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_access_field_restrictions_expression_nodes_ConditionExprRoo~",
                schema: "registry",
                table: "access_field_restrictions",
                column: "ConditionExprRootId",
                principalSchema: "registry",
                principalTable: "expression_nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_annotations_migration_defs_MigrationDefRecordId",
                schema: "registry",
                table: "annotations",
                column: "MigrationDefRecordId",
                principalSchema: "registry",
                principalTable: "migration_defs",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_access_field_restrictions_expression_nodes_ConditionExprRoo~",
                schema: "registry",
                table: "access_field_restrictions");

            migrationBuilder.DropForeignKey(
                name: "FK_annotations_migration_defs_MigrationDefRecordId",
                schema: "registry",
                table: "annotations");

            migrationBuilder.DropTable(
                name: "migration_steps",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "migration_defs",
                schema: "registry");

            migrationBuilder.DropIndex(
                name: "IX_annotations_MigrationDefRecordId",
                schema: "registry",
                table: "annotations");

            migrationBuilder.DropIndex(
                name: "IX_access_field_restrictions_ConditionExprRootId",
                schema: "registry",
                table: "access_field_restrictions");

            migrationBuilder.DropColumn(
                name: "ExcludedFieldsJson",
                schema: "registry",
                table: "views");

            migrationBuilder.DropColumn(
                name: "IsProjection",
                schema: "registry",
                table: "views");

            migrationBuilder.DropColumn(
                name: "ParsedSelectJson",
                schema: "registry",
                table: "views");

            migrationBuilder.DropColumn(
                name: "ProjectionEntityName",
                schema: "registry",
                table: "views");

            migrationBuilder.DropColumn(
                name: "ProjectionFieldsJson",
                schema: "registry",
                table: "views");

            migrationBuilder.DropColumn(
                name: "ForEntityName",
                schema: "registry",
                table: "services");

            migrationBuilder.DropColumn(
                name: "ExcludeFieldsJson",
                schema: "registry",
                table: "service_exposed_entities");

            migrationBuilder.DropColumn(
                name: "IncludeFieldsJson",
                schema: "registry",
                table: "service_exposed_entities");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "registry",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "ImportsJson",
                schema: "registry",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "PublishesJson",
                schema: "registry",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "TenantAware",
                schema: "registry",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "Expression",
                schema: "registry",
                table: "entity_indexes");

            migrationBuilder.DropColumn(
                name: "ComputedStrategy",
                schema: "registry",
                table: "entity_fields");

            migrationBuilder.DropColumn(
                name: "OnDeleteAction",
                schema: "registry",
                table: "entity_associations");

            migrationBuilder.DropColumn(
                name: "MigrationDefRecordId",
                schema: "registry",
                table: "annotations");

            migrationBuilder.DropColumn(
                name: "Scope",
                schema: "registry",
                table: "access_rules");

            migrationBuilder.DropColumn(
                name: "ConditionExprRootId",
                schema: "registry",
                table: "access_field_restrictions");
        }
    }
}

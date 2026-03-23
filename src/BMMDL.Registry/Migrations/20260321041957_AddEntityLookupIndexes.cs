using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMMDL.Registry.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_entity_fields_EntityId_Position",
                schema: "registry",
                table: "entity_fields",
                columns: new[] { "EntityId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_entities_TenantId_Name",
                schema: "registry",
                table: "entities",
                columns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_entity_fields_EntityId_Position",
                schema: "registry",
                table: "entity_fields");

            migrationBuilder.DropIndex(
                name: "IX_entities_TenantId_Name",
                schema: "registry",
                table: "entities");
        }
    }
}

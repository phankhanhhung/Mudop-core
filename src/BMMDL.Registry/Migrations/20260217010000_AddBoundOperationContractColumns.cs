using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMMDL.Registry.Migrations
{
    /// <inheritdoc />
    public partial class AddBoundOperationContractColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreconditionExprIds",
                schema: "registry",
                table: "entity_bound_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostconditionExprIds",
                schema: "registry",
                table: "entity_bound_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModifiesJson",
                schema: "registry",
                table: "entity_bound_operations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreconditionExprIds",
                schema: "registry",
                table: "entity_bound_operations");

            migrationBuilder.DropColumn(
                name: "PostconditionExprIds",
                schema: "registry",
                table: "entity_bound_operations");

            migrationBuilder.DropColumn(
                name: "ModifiesJson",
                schema: "registry",
                table: "entity_bound_operations");
        }
    }
}

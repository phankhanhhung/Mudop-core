using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMMDL.Registry.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityExtendsFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtendsFrom",
                schema: "registry",
                table: "entities",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtendsFrom",
                schema: "registry",
                table: "entities");
        }
    }
}

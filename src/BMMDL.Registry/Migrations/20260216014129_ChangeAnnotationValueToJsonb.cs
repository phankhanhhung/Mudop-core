using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMMDL.Registry.Migrations
{
    /// <inheritdoc />
    public partial class ChangeAnnotationValueToJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Convert existing plain-text values to valid JSON before changing column type.
            // Only runs if the column is still text (skipped on fresh databases where it's already jsonb).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'registry'
                        AND table_name = 'annotations'
                        AND column_name = 'Value'
                        AND data_type = 'text'
                    ) THEN
                        UPDATE registry.annotations
                        SET "Value" = CASE
                            WHEN "Value" IS NULL THEN NULL
                            WHEN "Value" ~ '^\s*[\{\[]' THEN "Value"
                            WHEN "Value" ~ '^\s*".*"\s*$' THEN "Value"
                            WHEN "Value" ~ '^\s*(true|false|null)\s*$' THEN "Value"
                            WHEN "Value" ~ '^\s*-?[0-9]' THEN "Value"
                            ELSE to_jsonb("Value")::text
                        END;
                    END IF;
                END $$
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Value",
                schema: "registry",
                table: "annotations",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Value",
                schema: "registry",
                table: "annotations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}

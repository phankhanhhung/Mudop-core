using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BMMDL.Registry.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleRejectionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModifiesJson",
                schema: "registry",
                table: "service_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostconditionExprIds",
                schema: "registry",
                table: "service_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreconditionExprIds",
                schema: "registry",
                table: "service_operations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultValue",
                schema: "registry",
                table: "operation_parameters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultValueExprRootId",
                schema: "registry",
                table: "operation_parameters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                schema: "registry",
                table: "modules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedBy",
                schema: "registry",
                table: "modules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedReason",
                schema: "registry",
                table: "modules",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModifiesJson",
                schema: "registry",
                table: "service_operations");

            migrationBuilder.DropColumn(
                name: "PostconditionExprIds",
                schema: "registry",
                table: "service_operations");

            migrationBuilder.DropColumn(
                name: "PreconditionExprIds",
                schema: "registry",
                table: "service_operations");

            migrationBuilder.DropColumn(
                name: "DefaultValue",
                schema: "registry",
                table: "operation_parameters");

            migrationBuilder.DropColumn(
                name: "DefaultValueExprRootId",
                schema: "registry",
                table: "operation_parameters");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                schema: "registry",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "RejectedBy",
                schema: "registry",
                table: "modules");

            migrationBuilder.DropColumn(
                name: "RejectedReason",
                schema: "registry",
                table: "modules");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garbati.OrchestratorApi.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCodeToAccountOpenings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_openings_current_step",
                table: "account_openings");

            migrationBuilder.AddColumn<string>(
                name: "product_code",
                table: "account_openings",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_openings_current_step",
                table: "account_openings",
                sql: "\"current_step\" IN ('CreateCustomer', 'VerifyKyc', 'CreateLedgerAccount', 'AttachMandate', 'BindProduct', 'Complete')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_openings_current_step",
                table: "account_openings");

            migrationBuilder.DropColumn(
                name: "product_code",
                table: "account_openings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_openings_current_step",
                table: "account_openings",
                sql: "\"current_step\" IN ('CreateCustomer', 'VerifyKyc', 'CreateLedgerAccount', 'AttachMandate', 'Complete')");
        }
    }
}

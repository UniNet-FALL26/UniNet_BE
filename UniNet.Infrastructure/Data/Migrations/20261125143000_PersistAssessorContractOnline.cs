using Microsoft.EntityFrameworkCore.Migrations;

namespace UniNet.Infrastructure.Data.Migrations;

public partial class PersistAssessorContractOnline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ContractText", table: "AssessorOnboardings", type: "text", nullable: true);
        migrationBuilder.AddColumn<byte[]>(
            name: "ContractOriginalPdf", table: "AssessorOnboardings", type: "bytea", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ContractText", table: "AssessorOnboardings");
        migrationBuilder.DropColumn(name: "ContractOriginalPdf", table: "AssessorOnboardings");
    }
}

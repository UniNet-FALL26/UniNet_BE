using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppUserAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-authorization flows have no OAuth connection binding and cannot safely
            // complete. Preserve every original field while removing them from callback lookup.
            // EF executes these statements and the subsequent FK creation atomically.
            migrationBuilder.Sql("""
                LOCK TABLE "GitHubAppAuthorizationFlows" IN ACCESS EXCLUSIVE MODE;
                CREATE TABLE "GitHubAppAuthorizationFlowQuarantine" AS
                    SELECT * FROM "GitHubAppAuthorizationFlows";
                ALTER TABLE "GitHubAppAuthorizationFlowQuarantine" ADD PRIMARY KEY ("Id");
                DELETE FROM "GitHubAppAuthorizationFlows";
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitHubAppAuthorizationFlow_Status",
                table: "GitHubAppAuthorizationFlows");

            migrationBuilder.AddColumn<Guid>(
                name: "OAuthConnectionId",
                table: "GitHubAppAuthorizationFlows",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "GitHubAppUserCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    GitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    Login = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubAppUserCredentials", x => x.Id);
                    table.CheckConstraint("CK_GitHubAppUserCredential_User", "\"GitHubUserId\" > 0");
                    table.ForeignKey(
                        name: "FK_GitHubAppUserCredentials_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppAuthorizationFlows_OAuthConnectionId",
                table: "GitHubAppAuthorizationFlows",
                column: "OAuthConnectionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitHubAppAuthorizationFlow_Status",
                table: "GitHubAppAuthorizationFlows",
                sql: "\"Status\" IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_GitHubAppUserCredentials_AccountId_GitHubUserId",
                table: "GitHubAppUserCredentials",
                columns: new[] { "AccountId", "GitHubUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GitHubAppAuthorizationFlows_GitHubOAuthConnections_OAuthCon~",
                table: "GitHubAppAuthorizationFlows",
                column: "OAuthConnectionId",
                principalTable: "GitHubOAuthConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GitHubAppAuthorizationFlows_GitHubOAuthConnections_OAuthCon~",
                table: "GitHubAppAuthorizationFlows");

            migrationBuilder.DropTable(
                name: "GitHubAppUserCredentials");

            migrationBuilder.DropIndex(
                name: "IX_GitHubAppAuthorizationFlows_OAuthConnectionId",
                table: "GitHubAppAuthorizationFlows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GitHubAppAuthorizationFlow_Status",
                table: "GitHubAppAuthorizationFlows");

            migrationBuilder.DropColumn(
                name: "OAuthConnectionId",
                table: "GitHubAppAuthorizationFlows");

            // Databases that applied the original migration have no quarantine table.
            // Restore quarantined rows before removing the archive; any conflict rolls
            // back the complete migration rather than discarding either copy.
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    IF to_regclass('"GitHubAppAuthorizationFlowQuarantine"') IS NOT NULL THEN
                        INSERT INTO "GitHubAppAuthorizationFlows"
                            SELECT * FROM "GitHubAppAuthorizationFlowQuarantine";
                        DROP TABLE "GitHubAppAuthorizationFlowQuarantine";
                    END IF;
                END $migration$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_GitHubAppAuthorizationFlow_Status",
                table: "GitHubAppAuthorizationFlows",
                sql: "\"Status\" IN (0, 1, 2)");
        }
    }
}

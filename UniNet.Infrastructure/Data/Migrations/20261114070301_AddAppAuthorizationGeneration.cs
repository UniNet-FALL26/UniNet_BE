using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniNet.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppAuthorizationGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AuthorizationGeneration",
                table: "GitHubAppRepositoryGrants",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "AuthorizationGeneration",
                table: "GitHubAppInstallations",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql("""
                ALTER TABLE "GitHubAppRepositoryGrants" ADD CONSTRAINT "CK_AppGrant_AuthorizationGeneration" CHECK ("AuthorizationGeneration" > 0);
                ALTER TABLE "GitHubAppInstallations" ADD CONSTRAINT "CK_AppInstallation_AuthorizationGeneration" CHECK ("AuthorizationGeneration" > 0);
                CREATE FUNCTION uninet_app_grant_generation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF (OLD."RevokedAt" IS NULL) IS DISTINCT FROM (NEW."RevokedAt" IS NULL)
                     OR OLD."InstallationId" IS DISTINCT FROM NEW."InstallationId"
                     OR OLD."ProviderRepositoryId" IS DISTINCT FROM NEW."ProviderRepositoryId" THEN
                    NEW."AuthorizationGeneration" := OLD."AuthorizationGeneration" + 1;
                  ELSE NEW."AuthorizationGeneration" := OLD."AuthorizationGeneration"; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER uninet_app_grant_generation_trigger BEFORE UPDATE ON "GitHubAppRepositoryGrants"
                FOR EACH ROW EXECUTE FUNCTION uninet_app_grant_generation();
                CREATE FUNCTION uninet_app_installation_generation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD."Status" IS DISTINCT FROM NEW."Status" OR OLD."VerificationUnknown" IS DISTINCT FROM NEW."VerificationUnknown"
                     OR OLD."ProviderInstallationId" IS DISTINCT FROM NEW."ProviderInstallationId"
                     OR OLD."OwnerAccountId" IS DISTINCT FROM NEW."OwnerAccountId" OR OLD."OwnerType" IS DISTINCT FROM NEW."OwnerType" THEN
                    NEW."AuthorizationGeneration" := OLD."AuthorizationGeneration" + 1;
                  ELSE NEW."AuthorizationGeneration" := OLD."AuthorizationGeneration"; END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER uninet_app_installation_generation_trigger BEFORE UPDATE ON "GitHubAppInstallations"
                FOR EACH ROW EXECUTE FUNCTION uninet_app_installation_generation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER uninet_app_grant_generation_trigger ON "GitHubAppRepositoryGrants";
                DROP FUNCTION uninet_app_grant_generation();
                DROP TRIGGER uninet_app_installation_generation_trigger ON "GitHubAppInstallations";
                DROP FUNCTION uninet_app_installation_generation();
                ALTER TABLE "GitHubAppRepositoryGrants" DROP CONSTRAINT "CK_AppGrant_AuthorizationGeneration";
                ALTER TABLE "GitHubAppInstallations" DROP CONSTRAINT "CK_AppInstallation_AuthorizationGeneration";
                """);
            migrationBuilder.DropColumn(
                name: "AuthorizationGeneration",
                table: "GitHubAppRepositoryGrants");

            migrationBuilder.DropColumn(
                name: "AuthorizationGeneration",
                table: "GitHubAppInstallations");
        }
    }
}

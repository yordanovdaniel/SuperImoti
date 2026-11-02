using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperImoti.Web.Data.Migrations
{
    /// <summary>
    /// Adds a full-text index (SQLite FTS5) over Listings.SearchText, kept in sync by triggers,
    /// and a covering index for the district statistics.
    /// </summary>
    public partial class FullTextSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Listings_Stats",
                table: "Listings",
                columns: new[] { "Status", "DistrictId", "DealKind", "PropertyTypeId", "Price", "PricePerSqm" });

            // The index reads its text from Listings (content='Listings'), so the text is not stored twice.
            // unicode61 ignores letter case for Cyrillic too, so "надежда" also finds "Надежда".
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE "ListingSearch" USING fts5(
                    "SearchText",
                    content='Listings',
                    content_rowid='Id',
                    tokenize='unicode61 remove_diacritics 0'
                );
                """);

            // Index the listings that already exist.
            migrationBuilder.Sql("""INSERT INTO "ListingSearch"("ListingSearch") VALUES ('rebuild');""");

            // Keep the index up to date whenever a listing is added, changed or deleted.
            migrationBuilder.Sql("""
                CREATE TRIGGER "Listings_SearchInsert" AFTER INSERT ON "Listings" BEGIN
                    INSERT INTO "ListingSearch"(rowid, "SearchText") VALUES (new."Id", new."SearchText");
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "Listings_SearchDelete" AFTER DELETE ON "Listings" BEGIN
                    INSERT INTO "ListingSearch"("ListingSearch", rowid, "SearchText") VALUES ('delete', old."Id", old."SearchText");
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER "Listings_SearchUpdate" AFTER UPDATE OF "SearchText" ON "Listings" BEGIN
                    INSERT INTO "ListingSearch"("ListingSearch", rowid, "SearchText") VALUES ('delete', old."Id", old."SearchText");
                    INSERT INTO "ListingSearch"(rowid, "SearchText") VALUES (new."Id", new."SearchText");
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "Listings_SearchUpdate";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "Listings_SearchDelete";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "Listings_SearchInsert";""");
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "ListingSearch";""");

            migrationBuilder.DropIndex(
                name: "IX_Listings_Stats",
                table: "Listings");
        }
    }
}

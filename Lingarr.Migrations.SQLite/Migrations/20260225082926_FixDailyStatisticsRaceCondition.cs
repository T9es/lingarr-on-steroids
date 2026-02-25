using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.SQLite.Migrations
{
    /// <inheritdoc />
    public partial class FixDailyStatisticsRaceCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Deduplicate existing DailyStatistics records
            // Keep the record with the highest TranslationCount for each date
            // SQLite uses a different approach - create a temp table with deduplicated data
            migrationBuilder.Sql(@"
                CREATE TEMPORARY TABLE daily_statistics_dedup AS
                SELECT id, date, translation_count, created_at, updated_at
                FROM (
                    SELECT id, date, translation_count, created_at, updated_at,
                           ROW_NUMBER() OVER (PARTITION BY date ORDER BY translation_count DESC, id DESC) as rn
                    FROM daily_statistics
                ) ranked
                WHERE rn = 1
            ");

            migrationBuilder.Sql("DELETE FROM daily_statistics");

            migrationBuilder.Sql(@"
                INSERT INTO daily_statistics (id, date, translation_count, created_at, updated_at)
                SELECT id, date, translation_count, created_at, updated_at
                FROM daily_statistics_dedup
            ");

            migrationBuilder.Sql("DROP TABLE daily_statistics_dedup");

            // Step 2: Create unique index on date column
            // This prevents future race conditions from creating duplicates
            migrationBuilder.CreateIndex(
                name: "ux_daily_statistics_date",
                table: "daily_statistics",
                column: "date",
                unique: true);

            // Step 3: Backfill DailyStatistics from historical TranslationRequests
            // Only count completed translations (Status = 2) with a valid CompletedAt date
            // Use ON CONFLICT to handle any dates that already exist
            // SQLite uses datetime('now') instead of NOW()
            migrationBuilder.Sql(@"
                INSERT INTO daily_statistics (date, translation_count, created_at, updated_at)
                SELECT 
                    date(completed_at) as date,
                    COUNT(*) as translation_count,
                    datetime('now') as created_at,
                    datetime('now') as updated_at
                FROM translation_requests
                WHERE status = 2 
                  AND completed_at IS NOT NULL
                GROUP BY date(completed_at)
                ON CONFLICT (date) DO UPDATE SET
                    translation_count = daily_statistics.translation_count + EXCLUDED.translation_count,
                    updated_at = datetime('now')
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the unique index
            migrationBuilder.DropIndex(
                name: "ux_daily_statistics_date",
                table: "daily_statistics");
            
            // Note: We don't undo the backfill or deduplication
            // as that would lose user data
        }
    }
}

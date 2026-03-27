using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class FixDailyStatisticsRaceCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Deduplicate existing DailyStatistics records
            // Keep the record with the highest TranslationCount for each date
            // This handles the race condition that may have created duplicates
            migrationBuilder.Sql(@"
                DELETE FROM daily_statistics
                WHERE id NOT IN (
                    SELECT id FROM (
                        SELECT id, 
                               ROW_NUMBER() OVER (PARTITION BY date ORDER BY translation_count DESC, id DESC) as rn
                        FROM daily_statistics
                    ) ranked
                    WHERE rn = 1
                )
            ");

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
            migrationBuilder.Sql(@"
                INSERT INTO daily_statistics (date, translation_count, created_at, updated_at)
                SELECT 
                    DATE(completed_at) as date,
                    COUNT(*) as translation_count,
                    NOW() as created_at,
                    NOW() as updated_at
                FROM translation_requests
                WHERE status = 2 
                  AND completed_at IS NOT NULL
                GROUP BY DATE(completed_at)
                ON CONFLICT (date) DO UPDATE SET
                    translation_count = daily_statistics.translation_count + EXCLUDED.translation_count,
                    updated_at = NOW()
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

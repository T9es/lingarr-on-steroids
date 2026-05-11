using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexNonSupplementalTranslationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_TranslationRequests_UniqueNonSupplemental""
                ON translation_requests (workload_item_key, source_language, target_language, source_dedupe_key)
                WHERE workload_item_key IS NOT NULL
                  AND workload_item_key != ''
                  AND source_dedupe_key IS NOT NULL
                  AND (source_subtitle_type IS NULL
                       OR source_subtitle_type NOT IN ('Forced', 'SignsSongs', 'ForcedDialogue'));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_TranslationRequests_UniqueNonSupplemental"";
            ");
        }
    }
}

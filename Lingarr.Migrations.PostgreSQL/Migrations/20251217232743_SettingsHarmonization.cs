using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lingarr.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class SettingsHarmonization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename translation_batch_size to max_batch_size
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value)
                SELECT 'max_batch_size', value FROM settings WHERE key = 'translation_batch_size'
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM settings WHERE key = 'translation_batch_size';
            ");

            // Rename ignore_captions_in_source_file to ignore_captions
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value)
                SELECT 'ignore_captions', value FROM settings WHERE key = 'ignore_captions_in_source_file'
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM settings WHERE key = 'ignore_captions_in_source_file';
            ");

            // Rename libre_translate_url to libretranslate_url
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value)
                SELECT 'libretranslate_url', value FROM settings WHERE key = 'libre_translate_url'
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM settings WHERE key = 'libre_translate_url';
            ");

            // Rename libre_translate_api_key to libretranslate_api_key
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value)
                SELECT 'libretranslate_api_key', value FROM settings WHERE key = 'libre_translate_api_key'
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM settings WHERE key = 'libre_translate_api_key';
            ");

            // Rename translation.language_settings_version to language_settings_version
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value)
                SELECT 'language_settings_version', value FROM settings WHERE key = 'translation.language_settings_version'
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM settings WHERE key = 'translation.language_settings_version';
            ");

            // Convert extract_embedded_subtitles (bool) to subtitle_extraction_mode (string)
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value)
                SELECT 'subtitle_extraction_mode', CASE WHEN value = 'true' THEN 'extract_all' ELSE 'on_demand' END 
                FROM settings WHERE key = 'extract_embedded_subtitles'
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM settings WHERE key = 'extract_embedded_subtitles';
            ");

            // Seed missing add_translator_info if it doesn't exist
            migrationBuilder.Sql(@"
                INSERT INTO settings (key, value) VALUES ('add_translator_info', 'false')
                ON CONFLICT (key) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

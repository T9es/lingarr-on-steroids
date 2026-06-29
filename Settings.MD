# Settings

These are the environment variables currently read by Lingarr on Steroids as of June 29, 2026.

When a variable maps to an application setting, Lingarr applies it on startup and persists it to the database. Not every UI setting is env-backed: multi-instance arrays, token limits, subtitle validation rules, onboarding progress, dashboard layout, and most automation options are still configured through the UI or API.

The v3.0.0 release also added new database-backed settings that are configured in the UI rather than via env vars, including the per-provider circuit breaker threshold, post-translation quality gate tolerance, OCR detection toggles, configurable job scheduling on the new Tasks page (per-job cron expressions and enable flags), the failed-translation retry cap, and the auto source-language detection mode. Database settings still persist across container restarts as long as `/app/config` and the database volume are preserved. See [CHANGELOG.md](CHANGELOG.md) for a complete list.

## Container and runtime

| Environment variable | Description | Default |
|----------------------|-------------|---------|
| `TZ` | Container timezone. Handled by the container/runtime, not by Lingarr code directly. | - |
| `ASPNETCORE_URLS` | HTTP bind address for the ASP.NET app. | `http://+:9876` |

## Database and background jobs

| Environment variable | Description | Default |
|----------------------|-------------|---------|
| `DB_CONNECTION` | Database backend. Supported values: `postgresql`, `sqlite`. | `postgresql` |
| `DB_HOST` | PostgreSQL host. Used when `DB_CONNECTION=postgresql`. | `lingarr-postgres` |
| `DB_PORT` | PostgreSQL port. Used when `DB_CONNECTION=postgresql`. | `5432` |
| `DB_DATABASE` | PostgreSQL database name. Used when `DB_CONNECTION=postgresql`. | `lingarr` |
| `DB_USERNAME` | PostgreSQL username. Used when `DB_CONNECTION=postgresql`. | `lingarr` |
| `DB_PASSWORD` | PostgreSQL password. Used when `DB_CONNECTION=postgresql`. | `lingarr` |
| `SQLITE_DB_PATH` | SQLite file name inside `/app/config`. Used when `DB_CONNECTION=sqlite`. | `local.db` |
| `DB_HANGFIRE_SQLITE_PATH` | Separate SQLite file used by Hangfire storage when `DB_CONNECTION=sqlite`. | `/app/config/Hangfire.db` |
| `MAX_CONCURRENT_JOBS` | Hangfire worker count for sync, system, webhook, and default queues. | `5` |
| `MAX_PARALLEL_TRANSLATIONS` | Startup value for the custom translation worker pool. Applied to the `max_parallel_translations` setting. | `1` |
| `HANGFIRE_USERNAME` | Username for the Hangfire dashboard at `/hangfire`. | `admin` if unset |
| `HANGFIRE_PASSWORD` | Password for the Hangfire dashboard at `/hangfire`. A random password is generated if unset. | random if unset |

## Integration bootstrap

| Environment variable | Description |
|----------------------|-------------|
| `RADARR_URL` | Base URL for Radarr. Example: `http://radarr:7878`. |
| `RADARR_API_KEY` | API key for Radarr. |
| `SONARR_URL` | Base URL for Sonarr. Example: `http://sonarr:8989`. |
| `SONARR_API_KEY` | API key for Sonarr. |
| `SOURCE_LANGUAGES` | Minified JSON array of source languages. |
| `TARGET_LANGUAGES` | Minified JSON array of target languages. |
| `SERVICE_TYPE` | Active translation provider. See supported values below. |
| `AI_PROMPT` | Prompt template used by AI-based providers. |

`SOURCE_LANGUAGES` and `TARGET_LANGUAGES` must be JSON arrays of objects with `name` and `code`.

Example:

```json
[{"name":"English","code":"en"},{"name":"Polish","code":"pl"}]
```

## Supported `SERVICE_TYPE` values

| Value | Description |
|-------|-------------|
| `libretranslate` | LibreTranslate |
| `openai` | OpenAI chat models |
| `anthropic` | Anthropic Claude models |
| `localai` | LocalAI or compatible self-hosted chat endpoint |
| `deepl` | DeepL API |
| `google` | Google Translate via `GTranslate` |
| `bing` | Bing Translate via `GTranslate` |
| `microsoft` | Microsoft Translator via `GTranslate` |
| `yandex` | Yandex Translate via `GTranslate` |
| `gemini` | Google Gemini |
| `deepseek` | DeepSeek |
| `chutes` | Chutes.ai |
| `nanogpt` | NanoGPT (chat model or subscription) |
| `crofai` | CrofAI (credits-only) |

## Provider-specific variables

### LibreTranslate

| Environment variable | Description |
|----------------------|-------------|
| `LIBRE_TRANSLATE_URL` | LibreTranslate base URL. Example: `https://libretranslate.com`. |
| `LIBRE_TRANSLATE_API_KEY` | Optional API key for hosted or private LibreTranslate instances. |

### OpenAI

| Environment variable | Description |
|----------------------|-------------|
| `OPENAI_MODEL` | Model ID used for OpenAI translations. |
| `OPENAI_API_KEY` | OpenAI API key. |

### Anthropic

| Environment variable | Description | Default |
|----------------------|-------------|---------|
| `ANTHROPIC_MODEL` | Model ID used for Anthropic translations. | - |
| `ANTHROPIC_API_KEY` | Anthropic API key. | - |
| `ANTHROPIC_VERSION` | Anthropic API version header. | `2023-06-01` |

### Gemini

| Environment variable | Description |
|----------------------|-------------|
| `GEMINI_MODEL` | Model ID used for Gemini translations. |
| `GEMINI_API_KEY` | Gemini API key. |

### DeepSeek

| Environment variable | Description |
|----------------------|-------------|
| `DEEPSEEK_MODEL` | Model ID used for DeepSeek translations. |
| `DEEPSEEK_API_KEY` | DeepSeek API key. |

### LocalAI

| Environment variable | Description |
|----------------------|-------------|
| `LOCAL_AI_MODEL` | Model ID used for LocalAI translations. |
| `LOCAL_AI_API_KEY` | Optional API key for LocalAI or compatible endpoints. |
| `LOCAL_AI_ENDPOINT` | Base endpoint for LocalAI. Example: `http://localhost:8080`. |

### DeepL

| Environment variable | Description |
|----------------------|-------------|
| `DEEPL_API_KEY` | DeepL API key. |

### Chutes

| Environment variable | Description |
|----------------------|-------------|
| `CHUTES_MODEL` | Model ID used for Chutes translations. |
| `CHUTES_API_KEY` | Chutes API key. |
| `CHUTES_USAGE_LIMIT_OVERRIDE` | Optional daily request limit override used by the quota-aware Chutes integration. |

### NanoGPT

| Environment variable | Description | Default |
|----------------------|-------------|---------|
| `NANOGPT_MODEL` | Model ID used for NanoGPT translations. | service-selected default |
| `NANOGPT_API_KEY` | NanoGPT API key. | - |
| `NANOGPT_SUBSCRIPTION_MODELS_ONLY` | When truthy (`true` / `1` / `yes`), restrict selectable models to NanoGPT subscription plans. Prevents accidental spend on off-subscription models. | `true` |
| `NANOGPT_WEEKLY_TOKEN_ALLOWANCE` | Weekly token budget used by the NanoGPT reserve policy in tokens. The policy treats `0` (or unset) as disabled. | `60000000` (60 M) |
| `NANOGPT_TOKEN_RESERVE` | Tokens held back as a per-window reserve. When remaining weekly tokens drop to or below this value, the worker pauses translations to avoid blowing the budget mid-batch. Set to `0` to disable. | `0` (disabled) |
| `NANOGPT_DAILY_UNIT_RESERVE` | Daily unit reserve measured in NanoGPT `daily` units, evaluated alongside the weekly reserve. Set to `0` to disable. | `0` (disabled) |
| `NANOGPT_MONTHLY_UNIT_RESERVE` | Monthly unit reserve measured in NanoGPT `monthly` units, evaluated alongside the daily and weekly reserves. Set to `0` to disable. | `0` (disabled) |

The reserve windows (daily, weekly, monthly) are evaluated independently by `NanoGptReservePolicy`. If any single window's remaining budget falls to or below its reserve, translations pause and the policy reports the earliest reset time.

### CrofAI

| Environment variable | Description |
|----------------------|-------------|
| `CROFAI_MODEL` | Model ID used for CrofAI translations. |
| `CROFAI_API_KEY` | CrofAI API key. |

CrofAI ships with credits-only usage tracking. Translations auto-pause when the credits balance hits zero. Crof.ai no longer offers subscriptions as of the v3.0.0 release, so the previous daily-quota-driven auto-pause hook is no longer relevant. If you want a soft pause to protect a budget, configure the per-provider circuit breaker threshold via the UI instead.

## Not currently bootstrapped from env

- Multi-instance Radarr and Sonarr definitions are stored as JSON settings in the database and managed through the UI.
- Token-limit controls, Chutes mode, subtitle validation rules, extraction mode, dashboard layout, and onboarding progress are not imported from environment variables on startup.
- The v3.0.0 UI-only settings include: per-provider circuit breaker thresholds, post-translation quality gate tolerance and toggle, OCR detection job toggle, configurable scheduling on the Tasks page (per-job cron expressions and enable flags), the failed-translation retry cap, the auto source-language detection mode, and MKV embedding behaviour. None of these are env-backed. Configure them via the UI.
- If you need those settings to survive reinstalls, keep the application database and `/app/config` volume.

# Lingarr on Steroids

[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker Pulls](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids.svg)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?label=discord&logo=discord)](https://discord.gg/HkubmH2rcR)

**Lingarr on Steroids** is a specialized fork of the original [Lingarr](https://github.com/lingarr-translate/lingarr) application, re-engineered for enhanced reliability, performance, and cost-effective AI usage in subtitle translation workflows.

This fork maintains compatibility with **Radarr** and **Sonarr** while introducing architectural improvements designed for power users managing large media libraries.

## What Makes This Fork Different

This fork builds upon the original Lingarr foundation with significant enhancements to the translation pipeline, subtitle processing, and workflow management. While the core subtitle translation capabilities remain, the underlying architecture has been redesigned for better performance and reliability.

### Core Architecture Enhancements

**Translation Worker Service**
Replaced Hangfire with a custom database-driven BackgroundService supporting 1-20 concurrent workers, priority queues, and automatic crash recovery. This eliminates job queue starvation under heavy loads and provides better control over translation concurrency.

**PostgreSQL as Default Database**
Changed the default database from SQLite to PostgreSQL (with SQLite still supported for simple setups). PostgreSQL's MVCC (Multi-Version Concurrency Control) provides better concurrency handling during parallel processing. **Note:** MySQL/MariaDB support has been removed.

**Intelligent State Management**
Added TranslationState tracking for media items (9 states including Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed). This enables efficient queries for "what needs translation" without redundant scanning and provides stale detection when settings change.

### Subtitle Processing Improvements

**Deep Embedded Subtitle Extraction**
FFmpeg-based probing and extraction of subtitles (SRT, ASS, MOV_TEXT) directly from MKV/MP4 containers. Includes intelligent layer merging for concurrent subtitle entries and deduplication of overlapping timeframes commonly found in animation subtitles.

**Enhanced ASS/SSA Sanitation**
Significantly improved subtitle cleaning for translation:
- Drawing block removal (`{\p1}...{\p0}` vector graphics)
- Heuristic detection of drawing commands (80% density algorithm)
- "Poison" content filtering (musical symbols ♪♫♬, sound effects in brackets, URLs, credit lines)
- Single-character placeholder filtering

This ensures only translatable dialogue reaches translation services, dramatically improving translation quality for ASS/SSA files.

**Sparse Subtitle Detection**
Automatically identifies and skips subtitle tracks that contain only Signs/Songs/forced dialogue (threshold: <100 entries). Prevents wasting API calls on non-dialogue tracks.

**Subtitle Discovery**
Automatic detection of externally-added subtitle files with mtime-based change tracking. Media can remain in "AwaitingSource" state until suitable subtitles appear, enabling queue rotation without manual intervention.

### Translation Service Enhancements

**Deferred Contextual Repair**
Collects failed translations during batch processing and retries them at the end with surrounding context lines (configurable radius, default 10 lines). LLMs translate better with context, significantly improving recovery success rates for lines that fail in isolation.

**Chutes.ai Integration**
Full integration with cost-effective open-source models, including:
- Real-time usage tracking
- Quota enforcement with configurable buffers
- Intelligent pause/resume when quota limits are reached
- 402 PaymentRequired handling

**Extended Batch Translation Support**
Added batch translation capabilities to additional AI services (DeepSeek, Gemini, Chutes.ai), allowing multiple lines to be translated in a single API call for better context and reduced costs. Context wrapper support enables including surrounding lines for improved translation quality.

### Workflow Control

**Priority Queue & Cooperative Cancellation**
- Bump specific media to the front of the queue dynamically via priority flag
- Mid-stream job cancellation via cooperative cancellation service
- Runtime priority reordering (changing a show's priority immediately reorders its episodes)

**Live Test Panel**
Dedicated "Dry Run" interface to test translation settings and prompts on specific samples without modifying files. Features real-time log streaming via SSE for debugging prompt engineering and provider configuration.

**Cron Schedule Selector**
User-friendly dropdown for common cron patterns (every 15/30 minutes, hourly, daily, etc.) with a custom option for advanced users who prefer raw cron syntax.

**Translation Request Deduplication**
Prevents duplicate translation requests for the same media file using database constraints and transaction-safe deduplication, preventing wasted API quota.

### Additional Features

**Automatic Orphaned Subtitle Cleanup**
Detects and removes AI-translated subtitle files that have become orphaned when Sonarr/Radarr upgrades media files (filename changes). Includes audit logging of cleanup operations.

**Bulk Integrity Check**
Settings page for validating translation integrity across the entire library with real-time progress tracking via SignalR.

## Translation Services

Lingarr supports multiple translation services:

- **[LibreTranslate](https://libretranslate.com)** - Self-hosted or cloud translation
- **[DeepL](https://www.deepl.com/)** - Professional translation API
- **[OpenAI](https://openai.com/)** - GPT models with batch translation support
- **[Anthropic](https://www.anthropic.com/)** - Claude models with batch translation support
- **[Google Gemini](https://gemini.google.com/)** - Google's AI models with batch support
- **[DeepSeek](https://deepseek.com)** - Cost-effective AI with batch translation support
- **[Chutes.ai](https://chutes.ai)** - Open-source models with usage tracking and quota management
- **[LocalAI](https://localai.io/)** - Self-hosted models (Ollama compatible) with batch support
- **[Google Translate](https://translate.google.com/)** - Via GTranslate library
- **[Bing Translate](https://www.bing.com/translator)** - Via GTranslate library
- **[Yandex Translate](https://translate.yandex.com/)** - Via GTranslate library
- **[Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)** - Via GTranslate library

Choose the service that best fits your needs, budget, and privacy requirements.

## Usage

### Docker Image Tags

Lingarr provides multi-architecture Docker images that automatically select the correct platform:

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Latest stable release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Specific version | `linux/amd64`, `linux/arm64` |
| `main` | Development build from main branch | `linux/amd64`, `linux/arm64` |

**Note:** All images support both AMD64 (Intel/AMD) and ARM64 (Raspberry Pi, Apple Silicon) architectures.

## Setting up Lingarr

> [!WARNING]
> **Upgrading from v1.x?** Version 2.0.0 introduces breaking changes:
> - **MySQL/MariaDB support has been removed.** You must migrate to PostgreSQL (recommended) or SQLite.
> - **Settings are NOT migrated.** You will need to reconfigure your settings after upgrading (~5 minutes).
> - **Media library re-syncs automatically** from Radarr/Sonarr - no action needed for your media.
> - Your previous database cannot be migrated; this is a fresh start.

> **Recommended**: PostgreSQL is the recommended database for this fork. It uses MVCC (Multi-Version Concurrency Control) which eliminates lock contention issues during heavy parallel processing.

### PostgreSQL Setup (Recommended)

```yaml
version: "3.8"

networks:
  lingarr:

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/London # Replace with your timezone
      - DB_CONNECTION=postgresql
      - DB_HOST=lingarr-postgres
      - DB_PORT=5432
      - DB_DATABASE=lingarr
      - DB_USERNAME=lingarr
      - DB_PASSWORD=CHANGE_ME_SECURE_PASSWORD # ⚠️ CHANGE THIS
    volumes:
      - /path/to/media/movies:/movies
      - /path/to/media/tv:/tv
      - /path/to/config:/app/config
    ports:
      - "9876:9876"
    restart: unless-stopped
    networks:
      - lingarr
    depends_on:
      lingarr-postgres:
        condition: service_healthy

  lingarr-postgres:
    image: postgres:16-alpine
    container_name: lingarr-postgres
    environment:
      POSTGRES_DB: lingarr
      POSTGRES_USER: lingarr
      POSTGRES_PASSWORD: CHANGE_ME_SECURE_PASSWORD # ⚠️ CHANGE THIS (Must match above)
    volumes:
      - lingarr_postgres_data:/var/lib/postgresql/data
    networks:
      - lingarr
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U lingarr -d lingarr"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  lingarr_postgres_data:
```

### SQLite Setup (Simple)

For simple setups or testing, you can use SQLite which requires no additional containers:

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/London # Replace with your timezone
      - DB_CONNECTION=sqlite
    volumes:
      - /path/to/media/movies:/movies
      - /path/to/media/tv:/tv
      - /path/to/config:/app/config
    ports:
      - "9876:9876"
    restart: unless-stopped
```

### Docker CLI Setup

```bash
docker run -d \
  --name lingarr \
  --restart unless-stopped \
  -p 9876:9876 \
  -e ASPNETCORE_URLS=http://+:9876 \
  -v /path/to/media/movies:/movies \
  -v /path/to/media/tv:/tv \
  -v /path/to/config:/app/config \
  --network lingarr \
  ree0/lingarr-on-steroids:latest
```

## Environment Variables

| **Environment Variable** | **Description** |
|--------------------------|-----------------|
| `ASPNETCORE_URLS=http://+:9876` | The internal port that Lingarr will listen on inside the container. |
| `MAX_CONCURRENT_JOBS` | Hangfire worker pool size for sync jobs only (default: 20). Translation concurrency is controlled via the UI setting "Concurrent file translations". |
| `DB_CONNECTION=postgresql` | Specifies the database type. Options are `postgresql` (default) or `sqlite`. |
| `DB_HOST=lingarr-postgres` | The hostname for the PostgreSQL database (required when using `postgresql`). |
| `DB_PORT=5432` | The port for the PostgreSQL database (required when using `postgresql`). |
| `DB_DATABASE=lingarr` | The name of the database (required when using `postgresql`). |
| `DB_USERNAME=lingarr` | The username for the database (required when using `postgresql`). |
| `DB_PASSWORD=lingarr` | The password for the database (required when using `postgresql`). |
| `DB_HANGFIRE_SQLITE_PATH=/app/config/Hangfire.db` | The path of the sqlite database file for Hangfire, when sqlite connection is used. |
| `HANGFIRE_USERNAME` | Username for the Hangfire dashboard (`/hangfire`). Defaults to `admin` if not set. |
| `HANGFIRE_PASSWORD` | Password for the Hangfire dashboard. If not set, a random password is generated and printed to the console on startup. |

Additional [settings](Settings.MD) can be configured as environment variables to persist settings across reinstalls.

## Setting up LibreTranslate

This step is optional if you are using a translation service other than LibreTranslate.

```yaml
  libretranslate:
    container_name: libretranslate
    image: libretranslate/libretranslate:latest
    restart: unless-stopped
    environment:
      - LT_LOAD_ONLY=en,nl  # Important, replace with your preferred languages
    ports:
      - 5000:5000
    volumes:
      - /path/to/config:/home/libretranslate/.local/share/argos-translate
    networks:
      - lingarr
    healthcheck:
      test: ["CMD-SHELL", "./venv/bin/python scripts/healthcheck.py"]
```

### LibreTranslate Docker CLI

```bash
mkdir -p /apps/libretranslate/{local,db}
chmod -R 777 /apps/libretranslate

docker run -d \
  --name libretranslate \
  -p 5000:5000 \
  -v /path/to/libretranslate/db:/app/db \
  -v /path/to/libretranslate/local:/home/libretranslate/.local \
  libretranslate/libretranslate \
  --disable-web-ui \
  --load-only=en,nl     # Important, replace with your preferred languages
```

### LibreTranslate Environment Variables

| Parameter | Function |
|-----------|----------|
| `LT_LOAD_ONLY` | Allows you to add source languages by their [ISO code](https://libretranslate.com/languages) |
| `LT_DISABLE_WEB_UI` | Enables or disables a Google Translate-like web UI |

## API Integration

Lingarr provides a RESTful API that allows you to integrate subtitle translation capabilities into your applications. Complete API documentation with Swagger definitions is available at:

[Lingarr API Documentation](https://lingarr.com/docs/api/)

## Contributing

We welcome contributions to Lingarr on Steroids! Whether it's bug reports, feature requests, or code contributions, please feel free to help out. Visit the [Lingarr on Steroids](https://github.com/T9es/lingarr-on-steroids) GitHub repository to get started.

## Credits

This project builds upon the foundation of the original [Lingarr](https://github.com/lingarr-translate/lingarr) project by rowanfuchs.

- Icons: [Lucide](https://lucide.dev/icons)
- Subtitle Parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser)
- Translation Services: [LibreTranslate](https://libretranslate.com)
- GTranslate: [GTranslate](https://github.com/d4n3436/GTranslate)

## Special Thanks

For supporting open source:
- [selfh.st by Ethan](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

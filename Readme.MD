# Lingarr on Steroids

<div align="center">

[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker Pulls](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Advanced subtitle translation for power users**  
*Built for reliability, performance, and cost-effective AI workflows*

[Getting Started](#getting-started) • [Features](#what-makes-this-fork-different) • [Translation Services](#translation-services) • [Documentation](https://lingarr.com/docs/api/)

</div>

---

## Overview

**Lingarr on Steroids** is a specialized fork of [Lingarr](https://github.com/lingarr-translate/lingarr) re-engineered for enhanced reliability, performance, and cost-effective AI usage in subtitle translation workflows.

While maintaining full compatibility with **Radarr** and **Sonarr**, this fork introduces architectural improvements designed for power users managing large media libraries.

## What Makes This Fork Different

This fork builds upon the original Lingarr foundation with significant enhancements to the translation pipeline, subtitle processing, and workflow management. While the core subtitle translation capabilities remain, the underlying architecture has been redesigned for better performance and reliability.

### Core Architecture Enhancements

| Enhancement | Description | Benefit |
|-------------|-------------|---------|
| **Custom Translation Worker** | Database-driven BackgroundService with 1-20 concurrent workers | Eliminates queue starvation, automatic crash recovery |
| **PostgreSQL by Default** | Changed from SQLite to PostgreSQL with MVCC support | Better concurrency, eliminates lock contention |
| **Intelligent State Tracking** | 9-state TranslationState system for media items | Efficient "what needs translation" queries without redundant scanning |

**Key Details:**

**Translation Worker Service**  
Replaced Hangfire with a custom database-driven BackgroundService supporting 1-20 concurrent workers, priority queues, and automatic crash recovery. This eliminates job queue starvation under heavy loads and provides better control over translation concurrency.

**PostgreSQL as Default Database**  
Changed the default database from SQLite to PostgreSQL (with SQLite still supported for simple setups). PostgreSQL's MVCC (Multi-Version Concurrency Control) provides better concurrency handling during parallel processing. **Note:** MySQL/MariaDB support has been removed.

**Intelligent State Management**  
Added TranslationState tracking for media items (9 states including Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed). This enables efficient queries for "what needs translation" without redundant scanning and provides stale detection when settings change.

### Subtitle Processing Improvements

| Feature | What It Does |
|---------|--------------|
| **Deep Embedded Extraction** | FFmpeg-based probing and extraction of subtitles (SRT, ASS, MOV_TEXT) directly from MKV/MP4 containers with intelligent layer merging |
| **Enhanced ASS/SSA Sanitation** | Drawing block removal, heuristic detection, "poison" content filtering |
| **Sparse Subtitle Detection** | Automatically skips tracks with <100 entries (Signs/Songs-only) |
| **Subtitle Discovery** | Automatic detection of externally-added subtitle files with mtime tracking |

**Enhanced ASS/SSA Sanitation:**
- **Drawing block removal** (`{\p1}...{\p0}` vector graphics)
- **Heuristic detection** of drawing commands (80% density algorithm)
- **"Poison" content filtering** (musical symbols ♪♫♬, sound effects in brackets, URLs, credit lines)
- **Single-character placeholder filtering**

This ensures only translatable dialogue reaches translation services, dramatically improving translation quality for ASS/SSA files.

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

| Feature | Description |
|---------|-------------|
| **Priority Queue** | Bump media to front of queue dynamically via priority flag |
| **Cooperative Cancellation** | Mid-stream job cancellation via cancellation service |
| **Runtime Reordering** | Changing show priority immediately reorders episodes |
| **Live Test Panel** | "Dry Run" interface with real-time SSE log streaming |
| **Cron Schedule Selector** | User-friendly dropdown for common patterns (15/30 min, hourly, daily) |
| **Deduplication** | Prevents duplicate translation requests using database constraints |

### Additional Features

**Automatic Orphaned Subtitle Cleanup**  
Detects and removes AI-translated subtitle files that have become orphaned when Sonarr/Radarr upgrades media files (filename changes). Includes audit logging of cleanup operations.

**Bulk Integrity Check**  
Settings page for validating translation integrity across the entire library with real-time progress tracking via SignalR.

---

## Translation Services

Lingarr supports multiple translation services to fit your needs, budget, and privacy requirements:

**AI-Powered Translation**
- **[OpenAI](https://openai.com/)** - GPT models with batch translation support
- **[Anthropic](https://www.anthropic.com/)** - Claude models with batch translation support
- **[Google Gemini](https://gemini.google.com/)** - Google's AI models with batch support
- **[DeepSeek](https://deepseek.com)** - Cost-effective AI with batch translation support
- **[Chutes.ai](https://chutes.ai)** - Open-source models with usage tracking and quota management
- **[LocalAI](https://localai.io/)** - Self-hosted models (Ollama compatible) with batch support

**Cloud Translation APIs**
- **[LibreTranslate](https://libretranslate.com)** - Self-hosted or cloud translation
- **[DeepL](https://www.deepl.com/)** - Professional translation API
- **[Google Translate](https://translate.google.com/)** - Via GTranslate library
- **[Bing Translate](https://www.bing.com/translator)** - Via GTranslate library
- **[Yandex Translate](https://translate.yandex.com/)** - Via GTranslate library
- **[Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)** - Via GTranslate library

---

## Getting Started

### Docker Image Tags

Lingarr provides multi-architecture Docker images:

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Latest stable release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Specific version | `linux/amd64`, `linux/arm64` |
| `main` | Development build | `linux/amd64`, `linux/arm64` |

> **Note:** All images support both AMD64 (Intel/AMD) and ARM64 (Raspberry Pi, Apple Silicon) architectures.

### Quick Start

> [!WARNING]
> **Upgrading from v1.x?** Version 2.0.0 introduces breaking changes:
> - **MySQL/MariaDB support has been removed.** Migrate to PostgreSQL (recommended) or SQLite.
> - **Settings are NOT migrated.** Reconfigure after upgrading (~5 minutes).
> - **Media library re-syncs automatically** from Radarr/Sonarr - no action needed.
> - Previous databases cannot be migrated; this is a fresh start.

**Recommended:** PostgreSQL is the recommended database for this fork. It uses MVCC (Multi-Version Concurrency Control) which eliminates lock contention issues during heavy parallel processing.

<details>
<summary><b>PostgreSQL Setup (Recommended)</b></summary>

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

</details>

<details>
<summary><b>SQLite Setup (Simple)</b></summary>

For simple setups or testing, use SQLite which requires no additional containers:

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

</details>

<details>
<summary><b>Docker CLI Setup</b></summary>

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

</details>

---

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | Internal port Lingarr listens on | `http://+:9876` |
| `MAX_CONCURRENT_JOBS` | Hangfire worker pool size for sync jobs | `20` |
| `DB_CONNECTION` | Database type: `postgresql` or `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL hostname (required for PostgreSQL) | - |
| `DB_PORT` | PostgreSQL port (required for PostgreSQL) | `5432` |
| `DB_DATABASE` | Database name (required for PostgreSQL) | - |
| `DB_USERNAME` | Database username (required for PostgreSQL) | - |
| `DB_PASSWORD` | Database password (required for PostgreSQL) | - |
| `DB_HANGFIRE_SQLITE_PATH` | SQLite path for Hangfire (SQLite only) | `/app/config/Hangfire.db` |
| `HANGFIRE_USERNAME` | Hangfire dashboard username | `admin` |
| `HANGFIRE_PASSWORD` | Hangfire dashboard password | Random (printed on startup) |

Additional settings can be configured as environment variables to persist across reinstalls. See [Settings.MD](Settings.MD) for the complete list.

### LibreTranslate Setup

Optional if using a different translation service.

<details>
<summary><b>Docker Compose</b></summary>

```yaml
  libretranslate:
    container_name: libretranslate
    image: libretranslate/libretranslate:latest
    restart: unless-stopped
    environment:
      - LT_LOAD_ONLY=en,nl  # Replace with your preferred languages
    ports:
      - 5000:5000
    volumes:
      - /path/to/config:/home/libretranslate/.local/share/argos-translate
    networks:
      - lingarr
    healthcheck:
      test: ["CMD-SHELL", "./venv/bin/python scripts/healthcheck.py"]
```

</details>

<details>
<summary><b>Docker CLI</b></summary>

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
  --load-only=en,nl     # Replace with your preferred languages
```

</details>

**LibreTranslate Environment Variables:**

| Variable | Description |
|----------|-------------|
| `LT_LOAD_ONLY` | Source languages by [ISO code](https://libretranslate.com/languages) |
| `LT_DISABLE_WEB_UI` | Disables the web UI (set to any value) |

---

## API Integration

Lingarr provides a RESTful API for integrating subtitle translation capabilities into your applications. Complete API documentation with Swagger definitions is available at:

[Lingarr API Documentation](https://lingarr.com/docs/api/)

---

## Contributing

We welcome contributions! Whether it's bug reports, feature requests, or code contributions, please feel free to help out.

Visit the [Lingarr on Steroids](https://github.com/T9es/lingarr-on-steroids) GitHub repository to get started.

---

## Credits

This project builds upon the foundation of the original [Lingarr](https://github.com/lingarr-translate/lingarr) project by rowanfuchs.

- Icons: [Lucide](https://lucide.dev/icons)
- Subtitle Parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser)
- Translation Services: [LibreTranslate](https://libretranslate.com)
- GTranslate: [GTranslate](https://github.com/d4n3436/GTranslate)

---

## Special Thanks

For supporting open source:
- [selfh.st by Ethan](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

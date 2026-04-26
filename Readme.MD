# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Subtitle translation for real-world Radarr/Sonarr libraries.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [Chinese](Readme.zh.md)

---

> This README reflects our fork and the upstream Lingarr project as they looked on March 27, 2026. If upstream changes later, a few details here may drift over time.
>
> Upgrading from v1.x? Version 2.0.0 has breaking changes. MySQL/MariaDB is no longer supported, settings are not migrated automatically, and a clean start is required.

---

## What is this?

Lingarr on Steroids is a fork of [Lingarr](https://github.com/lingarr-translate/lingarr). We kept the core idea (translate subtitles via Radarr/Sonarr) but rebuilt most of the backend and added a lot of UI improvements.

It still inherits Lingarr's base workflow: index media through Radarr and Sonarr, discover subtitle tracks, translate them with supported providers, and manage everything from a web UI. This fork focuses on queue reliability, multi-instance libraries, subtitle repair, and operational visibility that we need for larger installs.

---

## What we changed

### Backend and queueing

| Area | What changed in our fork |
|------|--------------------------|
| Custom translation worker | Translation jobs run through a custom `BackgroundService` with configurable parallel workers instead of relying only on Hangfire queues. |
| PostgreSQL-first deployment | PostgreSQL is the default database. SQLite remains supported for smaller installs. |
| Media translation states | Media uses a 9-state model: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `AwaitingSource`, `NoSuitableSubtitles`, `Failed`. |
| Multi-instance support | Movies and shows track `SourceInstanceId`, so one Lingarr install can connect to multiple Radarr and Sonarr instances. |
| Deferred repair | Failed lines can be retried with surrounding context so repair passes are less brittle and usually translate better. |

### Subtitle processing

- FFmpeg extraction can pull text subtitles out of MKV and MP4 containers when they are embedded.
- ASS/SSA cleanup strips drawing commands, musical markers, placeholder effects, and URLs before translation.
- Sparse subtitle tracks with fewer than 50 dialogue entries are skipped.
- External subtitle discovery picks up subtitle files you add manually and keeps them tracked.

### UI and operations

- The onboarding wizard guides first-time setup for Radarr and Sonarr.
- Dashboard widgets support drag-and-drop layouts and live updates over SignalR.
- Job queue and translation history widgets add visibility that upstream does not currently ship.
- The API usage widget tracks usage metrics such as calls, tokens, latency, errors, and success rate.
- The client includes 11 built-in themes, not just a light/dark toggle.
- The UI is translated into English, Dutch, German, French, Spanish, Polish, and Simplified Chinese.

### Reliability

- Orphan subtitle cleanup detects renamed media that left translated subtitle files behind.
- Bulk integrity checks can validate translated subtitles across the library.
- Ghost-job protection avoids overwriting terminal request states and cleans up interrupted work after restarts.
- Exponential backoff and delayed requeue logic reduce pressure on unstable providers.
- Chutes and NanoGPT integrations include quota-aware usage handling and provider-specific controls in this fork.

---

## Supported services

This is what works in our fork today. Some of these are supported upstream as well, so this section is about compatibility, not exclusivity.

**AI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (with quota tracking & auto-pause)
- [NanoGPT](https://nano-gpt.com/) (with subscription usage, reserves & auto-pause)
- LocalAI / Ollama (self-hosted)

**Cloud APIs:**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## Getting started

### Docker image tags

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Latest stable release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Specific version | `linux/amd64`, `linux/arm64` |
| `main` | Development build | `linux/amd64`, `linux/arm64` |

PostgreSQL is recommended. SQLite works for small setups (single user, <1000 media items).

> **Note:** All images support both AMD64 (Intel/AMD) and ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (recommended)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Your/Timezone
      - DB_CONNECTION=postgresql
      - DB_HOST=postgres
      - DB_PORT=5432
      - DB_DATABASE=lingarr
      - DB_USERNAME=lingarr
      - DB_PASSWORD=your_secure_password
    volumes:
      - ./movies:/movies
      - ./tv:/tv
      - ./config:/app/config
    ports:
      - "9876:9876"
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy

  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: lingarr
      POSTGRES_USER: lingarr
      POSTGRES_PASSWORD: your_secure_password
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U lingarr -d lingarr"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  postgres_data:
```

### SQLite (quick start)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    environment:
      - TZ=Your/Timezone
      - DB_CONNECTION=sqlite
      - SQLITE_DB_PATH=lingarr.db
    volumes:
      - ./movies:/movies
      - ./tv:/tv
      - ./config:/app/config
    ports:
      - "9876:9876"
    restart: unless-stopped
```

---

## Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `TZ` | Container timezone | - |
| `ASPNETCORE_URLS` | HTTP bind address | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` or `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | SQLite file name inside `/app/config` | `local.db` |
| `DB_HOST` | PostgreSQL host | - |
| `DB_PORT` | PostgreSQL port | `5432` |
| `DB_DATABASE` | Database name | - |
| `DB_USERNAME` | DB username | - |
| `DB_PASSWORD` | DB password | - |
| `MAX_PARALLEL_TRANSLATIONS` | Startup value for custom translation workers | `1` |
| `MAX_CONCURRENT_JOBS` | Hangfire worker count for sync and system queues | `5` |
| `RADARR_URL` | Your Radarr URL | - |
| `RADARR_API_KEY` | Radarr API key | - |
| `SONARR_URL` | Your Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API key | - |

Full environment reference in [Settings.MD](Settings.MD).

---

## Credits

Original Lingarr by [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Icons: [Lucide](https://lucide.dev/icons).  
Subtitle parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Translation: LibreTranslate, GTranslate library.

---

## Thanks

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

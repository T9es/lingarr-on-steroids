# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Subtitle translation that actually works** - for people running media libraries at scale.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Upgrading from v1.x?** Version 2.0.0 has breaking changes - MySQL/MariaDB removed, settings are NOT migrated, fresh start required. See below for details.

---

## What is this?

Lingarr on Steroids is a fork of [Lingarr](https://github.com/lingarr-translate/lingarr). We kept the core idea (translate subtitles via Radarr/Sonarr) but rebuilt most of the backend and added a lot of UI improvements.

It started because the original Lingarr had reliability issues under load. We needed something that wouldn't fall over when you have thousands of shows.

---

## What we changed

### Backend

| What | Why |
|------|-----|
| Custom translation worker | Hangfire was choking on large queues. We wrote our own BackgroundService that handles 1-20 parallel workers, priority queuing, and auto-recovers from crashes. |
| PostgreSQL by default | SQLite locks up with concurrent workers. MVCC in PostgreSQL actually works. We kept SQLite as an option for tiny setups. |
| 9-state translation tracking | The original had no good way to answer "what needs translation?" We added states (Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted) so queries are fast. |
| Multi-instance support | One Radarr/Sonarr instance isn't enough for some people. You can now connect multiple *arr servers to one Lingarr. |
| Deferred repair | Failed lines get retried with surrounding context (10 lines by default). LLM translation quality jumps significantly when the AI can see what's happening before/after. |

### Subtitle processing

- **FFmpeg extraction** - pulls subtitles out of MKV/MP4 containers when they're embedded
- **ASS/SSA cleanup** - strips drawing commands, musical symbols, sound effect placeholders, URLs
- **Sparse track filter** - skips tracks with <100 entries (signs, songs)
- **External subtitle discovery** - finds subtitle files you add manually and tracks them

### UI/UX

- **Dashboard widgets** - drag-and-drop layout, real-time updates via SignalR
- **Job queue widget** - shows what's running, what's scheduled, what's failed
- **Translation history** - chart + list showing what translated when
- **API usage tracker** - sparkline charts showing spend per service
- **Onboarding wizard** - first-time setup guides you through Radarr/Sonarr config
- **Theme support** - dark/light with CSS variables so you can match your Setup
- **7 languages** - EN, NL, DE, FR, ES, PL, ZH
- **Offline detection** - shows when the app is unreachable

### Reliability

- **Orphan cleanup** - detects when upgrade renames the file and your AI translations are now orphaned
- **Bulk integrity check** - validates every translation in your library
- **Ghost job cleanup** - removes stuck jobs that never finished
- **Exponential backoff** - retries with jitter so you don't hammer failed APIs

---

## Supported services

**AI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (with quota tracking & auto-pause)
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
| `ASPNETCORE_URLS` | Port | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` or `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL host | - |
| `DB_PORT` | PostgreSQL port | `5432` |
| `DB_DATABASE` | Database name | - |
| `DB_USERNAME` | DB username | - |
| `DB_PASSWORD` | DB password | - |
| `RADARR_URL` | Your Radarr URL | - |
| `RADARR_API_KEY` | Radarr API key | - |
| `SONARR_URL` | Your Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API key | - |

Full list in [Settings.MD](Settings.MD).

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
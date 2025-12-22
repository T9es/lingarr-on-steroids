# Lingarr on Steroids

[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker Pulls](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids.svg)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?label=discord&logo=discord)](https://discord.gg/HkubmH2rcR)

**Lingarr on Steroids** is a specialized fork of the original Lingarr application, re-engineered for performance, reliability, and cost-effective AI usage.

While staying true to the core mission of seamless **Radarr** and **Sonarr** subtitle translation, this fork introduces significant architectural improvements for power users.

## Feature Comparison

| Feature | Original Lingarr | Lingarr on Steroids |
| :--- | :--- | :--- |
| **Translation Engine** | Serial execution (one file at a time). | **Custom Translation Worker Service**<br>Database-driven job queue with dynamic worker scaling (up to 20 concurrent), priority queue support, and automatic crash recovery. |
| **Resilience Strategy** | Basic retry on failure. | **Deferred Contextual Repair**<br>Collects failed translations during processing, then retries them together at the end with surrounding context lines. The LLM sees what came before/after, dramatically improving recovery success. |
| **Subtitle Sources** | External subtitle files only. | **Deep Embedded Extraction**<br>Uses FFmpeg to probe and extract subtitles (SRT, ASS, MOV_TEXT) directly from MKV/MP4 containers. Automatically merges concurrent layers and dedupes overlapping timeframes. |
| **Cleaning Logic** | Basic HTML tag stripping. | **Advanced ASS/SSA Sanitation**<br>Heuristic algorithms detect and remove vector drawing commands, karaoke tags, and positioning artifacts that confuse LLMs. |
| **Quality Control** | None. | **Integrity Validation**<br>Line-count verification (95% tolerance) ensures the translated file matches the structure of the source, protecting against hallucinations. |
| **Database** | SQLite only. | **PostgreSQL + SQLite**<br>PostgreSQL (recommended) for high-concurrency environments with MVCC for zero lock contention. SQLite for simple setups. |
| **Cost Efficiency** | Standard pay-per-token. | **Chutes.ai Integration**<br>Usage-tracking capable implementation for Chutes.ai, enabling high-quality open-source models at fraction of the cost. |
| **Workflow Control** | Fire-and-forget. | **Cooperative Cancellation & Priorities**<br>Jobs can be elegantly cancelled mid-stream. Priority queue allows bumping specific media to the front. |
| **Automation** | Basic Cron strings. | **Visual Schedule Builder**<br>User-friendly UI for scheduling automated tasks without needing cron syntax knowledge. |
| **Debugging** | Server logs only. | **Live Test Panel**<br>Dedicated "Dry Run" interface to test translation settings and prompts on specific text samples without modifying files. |

## Improvements
This fork focuses on **reliability at scale**. It transforms Lingarr from a simple script-runner into a robust background worker capable of handling TB-sized libraries.

It supports all major translation services including LibreTranslate, DeepL, OpenAI, Anthropic, Chutes.ai, LocalAI, and more.

https://github.com/user-attachments/assets/be753d77-ca53-4867-adea-363145613d2b

### 🌟 Multiple Translation Service Support
Lingarr now offers multiple services for automated translation:

- **[LibreTranslate](https://libretranslate.com)**
- **Local AI (with Ollama, or any other OpenAPI compatible model/router)**
- **[DeepL](https://www.deepl.com/)**  (without pt-BR)
- **[Anthropic](https://www.anthropic.com/)**
- **[Chutes.ai](https://chutes.ai)**
- **[OpenAI](https://openai.com/)**
- **[DeepSeek](https://deepseek.com)**
- **[Gemini](https://gemini.google.com/)**
- **[Google](https://translate.google.com/)** 
- **[Bing](https://www.bing.com/translator)**
- **[Yandex](https://translate.yandex.com/)**
- **[Azure](https://www.microsoft.com/en-us/translator/business/translator-api/)**

Choose the service that best fits your needs.

## Usage

### Docker Image Tags
Lingarr provides multi-architecture Docker images that automatically select the correct platform:

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Latest stable release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Specific version | `linux/amd64`, `linux/arm64` |
| `main` | ⚠️ Development build from main branch | `linux/amd64`, `linux/arm64` |

**Note:** As of 1.0.3 all images support both AMD64 (Intel/AMD) and ARM64 (Raspberry Pi, Apple Silicon) architectures. Docker will automatically pull the correct architecture for your system.

## Setting up Lingarr

> [!WARNING]
> **Upgrading from v1.x?** Version 2.0.0 introduces breaking changes:
> - **MySQL/MariaDB support has been removed.** You must migrate to PostgreSQL (recommended) or SQLite.
> - **Settings are NOT migrated.** You will need to reconfigure your settings after upgrading (~5 minutes).
> - **Media library re-syncs automatically** from Radarr/Sonarr - no action needed for your media.
> - Your previous database cannot be migrated; this is a fresh start.

> ✅ **Recommended**: PostgreSQL is the recommended database for this fork. It uses MVCC (Multi-Version Concurrency Control) which eliminates lock contention issues during heavy parallel processing.

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

### Setting up Lingarr using Docker CLI
Follow the following steps to set up Lingarr and LibreTranslate via docker CLI

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

### Lingarr environment variables
These variables can be used

| **Environment Variable**                          | **Description**                                                                   |
|---------------------------------------------------|-----------------------------------------------------------------------------------|
| `ASPNETCORE_URLS=http://+:9876`                   | The internal port that Lingarr will listen on inside the container.               |
| `MAX_CONCURRENT_JOBS`                             | Hangfire worker pool size for sync jobs only (default: 20). Translation concurrency is controlled via the UI setting "Concurrent file translations". |
| `DB_CONNECTION=postgresql`                        | Specifies the database type. Options are `postgresql` (default) or `sqlite`.      |
| `DB_HOST=lingarr-postgres`                        | The hostname for the PostgreSQL database (required when using `postgresql`).      |
| `DB_PORT=5432`                                    | The port for the PostgreSQL database (required when using `postgresql`).          |
| `DB_DATABASE=lingarr`                             | The name of the database (required when using `postgresql`).                      |
| `DB_USERNAME=lingarr`                             | The username for the database (required when using `postgresql`).                 |
| `DB_PASSWORD=lingarr`                             | The password for the database (required when using `postgresql`).                 |
| `DB_HANGFIRE_SQLITE_PATH=/app/config/Hangfire.db` | The path of the sqlite database file for Hangfire, when sqlite connection is used |
| `HANGFIRE_USERNAME`                               | Username for the Hangfire dashboard (`/hangfire`). Defaults to `admin` if not set. |
| `HANGFIRE_PASSWORD`                               | Password for the Hangfire dashboard. If not set, a random password is generated and printed to the console on startup. |

Additional [settings](Settings.MD) can be found here that can be set as environment variables to persist settings for each reinstall

## Setting up LibreTranslate
This step is optional if you are using a translation service other than LibreTranslate.

```yaml
  LibreTranslate:
    container_name: LibreTranslate
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

### Setting up LibreTranslate using Docker CLI

Create necessary directories and set permissions:
```bash
mkdir -p /apps/libretranslate/{local,db}
chmod -R 777 /apps/libretranslate
```
Run LibreTranslate Docker container:
```bash
docker run -d \
  --name LibreTranslate \
  -p 5000:5000 \
  -v /path/to/libretranslate/db:/app/db \
  -v /path/to/libretranslate/local:/home/libretranslate/.local \
  libretranslate/libretranslate \
  --disable-web-ui \
  --load-only=en,nl     # Important, replace with your preferred languages
```

### LibreTranslate environment variables
| Parameter                      | Function                                                                        |
|--------------------------------|---------------------------------------------------------------------------------|
| `LT_LOAD_ONLY`                  | Allows you to add source languages by their [iso code](https://libretranslate.com/languages)                    |
| `LT_DISABLE_WEB_UI`              | Enables or disables a Google translate like web ui                            |

## API Integration
Lingarr provides a RESTful API that allows you to integrate subtitle translation capabilities into your applications. You can find the complete API documentation, including a Swagger definition of all available endpoints at the 
[Lingarr API Documentation](https://lingarr.com/docs/api/)

### 🤝 Contributing:
We welcome contributions to Lingarr! Whether it's bug reports, 
feature requests, or code contributions, please feel free to help out. Visit the [Lingarr](https://github.com/lingarr-translate/lingarr) GitHub repository to get started.

### 🙏 Credits:
Icons: [Lucide](https://lucide.dev/icons)  
Subtitle Parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser)    
Translation Services: [libretranslate](https://libretranslate.com)  
GTranslate: [GTranslate](https://github.com/d4n3436/GTranslate)

### 🙏 Special thanks:
For supporting open source:  
[selfh.st by Ethan](https://selfh.st/?ref=lingarr)  
[r/selfhosted](https://www.reddit.com/r/selfhosted/)  
[FrankieBBBB](https://github.com/FrankieBBBB)  

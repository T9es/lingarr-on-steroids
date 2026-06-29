# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Subtitle translation for real-world Radarr/Sonarr libraries.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [Chinese](Readme.zh.md)

---

> This README reflects our fork and the upstream Lingarr project as they looked on June 29, 2026. If upstream changes later, a few details here may drift over time.
>
> Upgrading from v1.x? Version 2.0.0 has breaking changes. MySQL/MariaDB is no longer supported, settings are not migrated automatically, and a clean start is required.
>
> Upgrading from v2.x to v3.0.0? See the [CHANGELOG](CHANGELOG.md) for migration notes. The Schedule page was renamed to Tasks. The onboarding wizard, configurable job scheduling, and post-translation quality gate have changed. CrofAI joined the supported AI providers. The version badge in the bottom-left now shows the real dev-build version instead of "Dev Build".

---

## What's new in v3.0.0

The v3 release is a substantial change over v2.5.0. If you only read one section, read this one.

- **Git-aware release versioning.** The assembly version is now resolved from `git describe` at build time, and the Docker build forwards a `VERSION` build-arg. Cut a release by tagging `v3.0.0` and pushing the tag. No more hand-editing `Lingarr.Core.csproj` for a release.
- **Dev-build badge shows the real version.** The bottom-left aside badge now reads `Dev <version>` (for example, `Dev 3.0.0-216-g39ae09b2`) instead of the previous generic `Dev Build` text.
- **CrofAI is now a supported AI provider** with credits-only usage tracking. Translations auto-pause when your CrofAI credits balance hits zero. See the new `CROFAI_*` env vars in [Settings.MD](Settings.MD).
- **OCR for bitmap subtitles.** DVD/VobSub, PGS, and other image-based subtitle tracks are OCR'd into text and then translated like any other source. Two new media states (`OcrPending`, `OcrBlocked`) cover the OCR lifecycle.
- **Per-provider circuit breaker.** A provider that starts throwing 5xx errors will trip a circuit and pause requests to it for a while, instead of burning your API quota during a provider outage.
- **Paused translation recovery.** Provider 429s (for example, Gemini rate limits) no longer kill a translation. The worker holds the slot and resumes when the limit clears.
- **Post-translation quality gate.** After a batch finishes, surviving paragraphs are scored. The UI lets you review, edit, accept, or reject items that fall outside the tolerance, with a Requeue All / Dismiss All bulk action.
- **Auto source-language mode.** Source language can be auto-detected per cue using NLLB (FLORES-200 spBLEU), LLM tier scoring, and language-family heuristics. Toggle is in onboarding and in the source-language settings.
- **Configurable job scheduling on the new Tasks page.** Each Hangfire and translation job has its own enable toggle and a per-job cron expression. The Tasks page is the renamed and redesigned Schedule page, wrapped in shared CardComponent cards with a responsive 1/2/3-column grid, loading and empty states, and a corrected SignalR cleanup. The old automation block on the limits card is gone.
- **Configurable embedding and language-detection, with new UI.** Frontend settings UI for MKV embedded-subtitle behaviour, untagged-stream language detection, and a request retry cap.
- **MKV embed fallback for long output paths.** If the translated subtitle path would exceed common filesystem limits (long anime file names are the usual culprit), Lingarr embeds the translation back into the original MKV instead.
- **Upload workspace moved under translations.** The Upload Workspace now lives as a tab inside the Translations page, so you no longer jump between top-level pages to manage uploads.
- **Dashboard infinite scroll, completed translation compare viewer, API usage widget enhancements.** Quality-of-life updates that pay off once your library is large.
- **GitHub issue templates** for bug, feature, and setup questions now sit under `.github/ISSUE_TEMPLATE/`. Please use them when opening issues.

The full migration walkthrough is in [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300).

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
| 11-state media model | Media track translation status across 11 states including OCR lifecycles: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `NoSuitableSubtitles`, `Failed`, `AwaitingSource`, `OcrPending`, `OcrBlocked`. Decision logic lives in `MediaStateService`. |
| Multi-instance support | Movies and shows track `SourceInstanceId`, so one Lingarr install can connect to multiple Radarr and Sonarr instances. |
| Deferred repair | Failed lines can be retried with surrounding context so repair passes are less brittle and usually translate better. |
| Per-provider circuit breaker | A singleton circuit breaker tracks failures per translation provider and backs off automatically when error thresholds are crossed, so a flaky Gemini or OpenAI endpoint will not drain your API quota. |
| Paused translation recovery | Translation requests that hit provider rate limits (for example, Gemini 429) pause into a holding slot instead of failing, and resume automatically once the limit clears. |
| Post-translation quality gate | After a batch finishes, surviving paragraphs are scored with a configurable tolerance. The UI lets you edit or reject anything that fails the gate. Default is on, with a settings toggle. |

### Subtitle processing

- FFmpeg extraction can pull text subtitles out of MKV and MP4 containers when they are embedded.
- ASS/SSA cleanup strips drawing commands, musical markers, placeholder effects, and URLs before translation.
- Sparse subtitle tracks with fewer than 50 dialogue entries are skipped.
- External subtitle discovery picks up subtitle files you add manually and keeps them tracked.
- Bitmap subtitle tracks (DVD/VobSub, PGS, etc.) can be OCR'd into text and then translated like any other source.
- ASS integrity checks catch leaked tag fragments so translation prompts do not pick up drawing commands as dialogue.
- Long translate output paths that would overflow common filesystem limits are embedded back into the original MKV instead of being written next to the media file.

### UI and operations

- The onboarding wizard guides first-time setup for Radarr and Sonarr.
- Dashboard widgets support drag-and-drop layouts and live updates over SignalR.
- Job queue and translation history widgets add visibility that upstream does not currently ship.
- The API usage widget tracks usage metrics such as calls, tokens, latency, errors, and success rate.
- Failures show up in a quality-gate audit where you can inline-edit the offending cue, then accept or reject it. Failed batches can be requeued or dismissed in bulk.
- A completed translation compare viewer lets you diff source vs translated text side by side after the fact.
- The dashboard history widget uses infinite scroll instead of paging, which matters once you have a large library.
- The Upload Workspace now lives as a tab inside the Translations page, so you no longer jump between top-level pages to manage uploads. Custom Sources remains a top-level settings entry.
- Configurable job scheduling lives on the redesigned Tasks page (formerly Schedule) with per-job enable toggles, cron expressions, shared CardComponent cards, a responsive grid, and explicit loading/empty states.
- The client includes 11 built-in themes, not just a light/dark toggle.
- The UI is translated into English, Dutch, German, French, Spanish, Polish, and Simplified Chinese.

### Reliability

- Orphan subtitle cleanup detects renamed media that left translated subtitle files behind.
- Bulk integrity checks can validate translated subtitles across the library.
- Ghost-job protection avoids overwriting terminal request states and cleans up interrupted work after restarts.
- Exponential backoff and delayed requeue logic reduce pressure on unstable providers.
- Paused translation recovery holds worker slots for translations that hit rate limits (for example, Gemini 429) and resumes them automatically once the limit clears, instead of failing the request.
- Silent token streaming for AI providers reduces first-token latency on long translations.
- Own translation queues honour media priority and avoid head-of-line blocking when a low-priority translation stalls.
- Chutes, NanoGPT, and CrofAI integrations each ship with quota-aware usage handling, provider-specific controls, and a UI in this fork.

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
- [CrofAI](https://crof.ai/) (credits-only usage tracking; auto-pauses translations when the credits balance hits zero)
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

# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Ondertitelvertaling voor echte Radarr/Sonarr-bibliotheken.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Snapshot vergeleken met `lingarr-translate/lingarr` op 27 maart 2026. Upstream kan daarna verder veranderen.
>
> Upgrade vanaf v1.x? Versie 2.0.0 bevat breaking changes. MySQL/MariaDB wordt niet meer ondersteund, instellingen worden niet automatisch gemigreerd en een schone start is vereist.

---

## Wat is dit?

Lingarr on Steroids is een fork van [Lingarr](https://github.com/lingarr-translate/lingarr). De basisworkflow blijft hetzelfde: media indexeren via Radarr en Sonarr, ondertitels vinden, vertalen via ondersteunde providers en alles beheren vanuit een webinterface.

Deze fork richt zich op betrouwbaardere queues, multi-instance bibliotheken, subtitle repair en betere operationele zichtbaarheid voor grotere installaties.

---

## Geverifieerde verschillen van deze fork

### Backend en queueing

| Onderdeel | Geverifieerd verschil in deze fork |
|-----------|------------------------------------|
| Aangepaste translation worker | Vertaaljobs draaien via een eigen `BackgroundService` met instelbare parallelle workers, niet alleen via Hangfire-queues. |
| PostgreSQL als standaard | PostgreSQL is de standaarddatabase. SQLite blijft ondersteund voor kleinere installaties. |
| Media-statusmodel | Media gebruiken 9 statussen: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `AwaitingSource`, `NoSuitableSubtitles`, `Failed`. |
| Multi-instance ondersteuning | Films en series bewaren `SourceInstanceId`, zodat meerdere Radarr- en Sonarr-instanties aan een installatie gekoppeld kunnen worden. |
| Deferred repair | Mislukte regels kunnen opnieuw geprobeerd worden met omliggende context, wat reparatierondes robuuster maakt. |

### Ondertitelverwerking

- FFmpeg kan tekstgebaseerde ondertitels uit ingebedde MKV- en MP4-tracks halen.
- ASS/SSA-opschoning verwijdert tekencommando's, muziekmarkeringen, placeholder-effecten en URL's voor de vertaling.
- Schaarse tracks met minder dan 50 dialoogregels worden overgeslagen.
- Externe ondertitelontdekking pikt handmatig toegevoegde ondertitelbestanden op en blijft ze volgen.

### UI en operatie

- De onboardingwizard begeleidt de eerste Radarr- en Sonarr-configuratie.
- Dashboardwidgets ondersteunen drag-and-drop layouts en live updates via SignalR.
- Job queue- en vertaalgeschiedenis-widgets geven zichtbaarheid die upstream momenteel niet heeft.
- De API-gebruikswidget toont gebruiksmetrics zoals calls, tokens, latency, errors en success rate.
- De client bevat 11 ingebouwde thema's, niet alleen een licht/donker-schakelaar.
- De UI is vertaald naar Engels, Nederlands, Duits, Frans, Spaans, Pools en Vereenvoudigd Chinees.

### Betrouwbaarheid

- Opruimen van verweesde ondertitels detecteert hernoemde mediabestanden die vertaalde ondertitels hebben achtergelaten.
- Bulk integrity checks kunnen vertaalde ondertitels in de hele bibliotheek valideren.
- Ghost-job bescherming voorkomt het overschrijven van terminale statussen en ruimt onderbroken werk na een restart op.
- Exponential backoff en vertraagde requeue-logica verminderen druk op instabiele providers.
- De Chutes-integratie bevat quota-aware gebruikslogica en providerspecifieke regels in deze fork.

---

## Ondersteunde diensten

Dit is de compatibiliteitslijst van deze fork op de snapshotdatum. Sommige diensten worden intussen ook door upstream ondersteund, dus dit is geen exclusieve fork-claim.

**AI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (met quota-tracking en automatische pauze)
- LocalAI / Ollama (zelf gehost)

**Cloud APIs:**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## Aan de slag

### Docker image tags

| Tag | Beschrijving | Architecturen |
|-----|--------------|---------------|
| `latest` | Nieuwste stabiele release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Specifieke versie | `linux/amd64`, `linux/arm64` |
| `main` | Ontwikkelingsbuild | `linux/amd64`, `linux/arm64` |

PostgreSQL wordt aanbevolen. SQLite is geschikt voor kleine setups (een gebruiker, <1000 media-items).

> Let op: alle images ondersteunen zowel AMD64 (Intel/AMD) als ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (aanbevolen)

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

### SQLite (snel starten)

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

## Configuratie

| Variabele | Beschrijving | Standaard |
|-----------|--------------|-----------|
| `TZ` | Tijdzone van de container | - |
| `ASPNETCORE_URLS` | HTTP-bindadres | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` of `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | SQLite-bestandsnaam in `/app/config` | `local.db` |
| `DB_HOST` | PostgreSQL host | - |
| `DB_PORT` | PostgreSQL poort | `5432` |
| `DB_DATABASE` | Databasenaam | - |
| `DB_USERNAME` | DB-gebruikersnaam | - |
| `DB_PASSWORD` | DB-wachtwoord | - |
| `MAX_PARALLEL_TRANSLATIONS` | Opstartwaarde voor custom translation workers | `1` |
| `MAX_CONCURRENT_JOBS` | Hangfire-worker aantal voor sync- en systeemqueues | `5` |
| `RADARR_URL` | Je Radarr URL | - |
| `RADARR_API_KEY` | Radarr API-sleutel | - |
| `SONARR_URL` | Je Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API-sleutel | - |

Volledige omgevingsvariabelenlijst staat in [Settings.MD](Settings.MD).

---

## Credits

Originele Lingarr door [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Iconen: [Lucide](https://lucide.dev/icons).  
Ondertitelverwerking: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Vertaling: LibreTranslate, GTranslate-bibliotheek.

---

## Dank aan

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

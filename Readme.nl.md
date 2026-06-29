# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Ondertitelvertaling voor echte Radarr/Sonarr-bibliotheken.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Deze README beschrijft onze fork en de stand van upstream Lingarr op 27 maart 2026. Als upstream daarna verder verandert, kunnen sommige details hier later wat achterlopen.
>
> Deze README beschrijft onze fork en de stand van upstream Lingarr op 29 juni 2026. Als upstream daarna verder verandert, kunnen sommige details hier later wat achterlopen.
>
> Upgrade vanaf v1.x? Versie 2.0.0 bevat breaking changes. MySQL/MariaDB wordt niet meer ondersteund, instellingen worden niet automatisch gemigreerd en een schone start is vereist.
>
> Upgrade vanaf v2.x naar v3.0.0? Zie [CHANGELOG](CHANGELOG.md) voor de migratie. De Schedule-pagina heet nu Tasks. De onboarding-wizard, configureerbare job scheduling en de post-translation quality gate zijn veranderd. CrofAI is toegevoegd als AI-provider. De versiebadge linksonder toont nu de echte dev-build-versie in plaats van "Dev Build".

---

## Wat is er nieuw in v3.0.0

De v3-release is een flinke verandering ten opzichte van v2.5.0. Als je maar een sectie leest, lees dan deze.

- **Git-bewuste release-versionering.** De assembly-versie wordt nu bij build-tijd uit `git describe` gehaald en de Docker-build stuurt een `VERSION`-build-arg door. Een release uitbrengen is nu alleen nog `v3.0.0` taggen en pushen. `Lingarr.Core.csproj` hoeft niet meer handmatig te worden aangepast.
- **Dev-build-badge toont de echte versie.** De badge linksonder in de zijbalk toont nu `Dev <version>` (bijvoorbeeld `Dev 3.0.0-216-g39ae09b2`) in plaats van de generieke `Dev Build`-tekst.
- **CrofAI is nu een ondersteunde AI-provider** met alleen-credits gebruiksregistratie. Vertalingen pauzeren automatisch wanneer je CrofAI-tegoed nul bereikt. Zie de nieuwe `CROFAI_*`-omgevingsvariabelen in [Settings.MD](Settings.MD).
- **OCR voor bitmap-ondertitels.** DVD/VobSub, PGS en andere beeldgebaseerde ondertiteltracks worden via OCR naar tekst omgezet en daarna zoals elke andere bron vertaald. Twee nieuwe mediastatussen (`OcrPending`, `OcrBlocked`) dekken de OCR-levenscyclus af.
- **Per-provider circuit breaker.** Gooit een provider opeens 5xx-fouten, dan opent het circuit en worden verzoeken korte tijd gepauzeerd in plaats van je API-quotum te verbranden tijdens een storing.
- **Hervatting van gepauzeerde vertalingen.** Provider-429's (bijvoorbeeld Gemini-rate-limits) beeindigen een vertaling niet meer. De worker houdt de slot vast en hervat zodra de limiet opgeheven is.
- **Post-translation quality gate.** Na afloop van een batch worden de overgebleven alinea's gescoord. De UI laat je buiten de tolerantie vallende items beoordelen, bewerken, accepteren of afwijzen, inclusief Requeue All / Dismiss All als bulkactie.
- **Automatische brontaalmodus.** De brontaal kan per cue automatisch worden gedetecteerd met NLLB (FLORES-200 spBLEU), LLM-tiervergelijking en taalfamilie-heuristiek. Schakelaar in de onboarding en in de brontaalinstellingen.
- **Configureerbare job scheduling op de nieuwe Tasks-pagina.** Elke Hangfire- en vertaaljob heeft een eigen aan/uit-schakelaar en een cron-expressie. De Tasks-pagina is de hernoemde en opnieuw ontworpen Schedule-pagina, met gedeelde CardComponent-kaarten, een responsive 1/2/3-kolomsraster, laad- en leeg-statussen, en gecorrigeerde SignalR-cleanup. Het oude automation-blok op de limits-kaart is weg.
- **Configureerbare embedding en taaldetectie, met nieuwe UI.** Frontend-instellingen voor MKV-embedded-ondertitelgedrag, taaldetectie op niet-getagde streams en een limiet op vertaal-herkansingen.
- **MKV-embed fallback voor lange uitvoer-paden.** Als het pad van de vertaalde ondertitel de gangbare bestandssysteemlimieten zou overschrijden (lange anime-bestandsnamen zijn de bekendste boosdoener), wordt de vertaling terug in de oorspronkelijke MKV ingebed.
- **Upload workspace verplaatst onder translations.** De Upload Workspace leeft nu als tabblad in de Translations-pagina, zodat je niet meer tussen topniveau-pagina's hoeft te springen.
- **Oneindig scrollen in het dashboard, completed translation compare viewer, uitbreidingen op de API-usage-widget.** Quality-of-life-verbeteringen die lonen bij grote bibliotheken.
- **GitHub-issue-sjablonen** voor bug, feature en setup-vragen staan onder `.github/ISSUE_TEMPLATE/`. Gebruik ze als je een issue opent.

De volledige migratiehandleiding staat in [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300).

---
---

## Wat is dit?

Lingarr on Steroids is een fork van [Lingarr](https://github.com/lingarr-translate/lingarr). De basisworkflow blijft hetzelfde: media indexeren via Radarr en Sonarr, ondertitels vinden, vertalen via ondersteunde providers en alles beheren vanuit een webinterface.

Deze fork richt zich op betrouwbaardere queues, multi-instance bibliotheken, subtitle repair en betere operationele zichtbaarheid voor grotere installaties.

---

## Wat we hebben veranderd

### Backend en queueing

| Onderdeel | Wat er in onze fork anders is |
|-----------|-------------------------------|
| Aangepaste translation worker | Vertaaljobs draaien via een eigen `BackgroundService` met instelbare parallelle workers, niet alleen via Hangfire-queues. |
| PostgreSQL als standaard | PostgreSQL is de standaarddatabase. SQLite blijft ondersteund voor kleinere installaties. |
| Mediastatusmodel met 11 statussen | Media volgen de vertaalstatus over 11 statussen inclusief de OCR-levenscyclus: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `NoSuitableSubtitles`, `Failed`, `AwaitingSource`, `OcrPending`, `OcrBlocked`. De beslislogica zit in `MediaStateService`. |
| Multi-instance ondersteuning | Films en series bewaren `SourceInstanceId`, zodat meerdere Radarr- en Sonarr-instanties aan een installatie gekoppeld kunnen worden. |
| Deferred repair | Mislukte regels kunnen opnieuw geprobeerd worden met omliggende context, wat reparatierondes robuuster maakt. |
| Per-provider circuit breaker | Een singleton circuit breaker volgt fouten per vertaalprovider en past automatisch backoff toe boven de drempel. |
| Hervatting van gepauzeerde vertalingen | Vertaalverzoeken die op een 429 stuiten (bijvoorbeeld Gemini) pauzeren met behoud van de workerslot en hervatten automatisch. |
| Post-translation quality gate | Na de batch worden overgebleven alinea's gescoord met configureerbare tolerantie. De UI laat je bewerken of afwijzen, met schakelaar in instellingen. Standaard aan. |

### Ondertitelverwerking

- FFmpeg kan tekstgebaseerde ondertitels uit ingebedde MKV- en MP4-tracks halen.
- ASS/SSA-opschoning verwijdert tekencommando's, muziekmarkeringen, placeholder-effecten en URL's voor de vertaling.
- Schaarse tracks met minder dan 50 dialoogregels worden overgeslagen.
- Externe ondertitelontdekking pikt handmatig toegevoegde ondertitelbestanden op en blijft ze volgen.
- Bitmap-ondertiteltracks (DVD/VobSub, PGS, etc.) worden via OCR naar tekst omgezet en daarna als gewone bron vertaald.
- ASS-integriteitschecks vangen lekkende tag-fragmenten op zodat vertaalprompts geen tekencommando's als dialoog behandelen.
- Lange uitvoer-paden die de bestandssysteemlimiet zouden overschrijden, worden terug in de oorspronkelijke MKV ingebed in plaats van naast het mediabestand geschreven.

### UI en operatie

- De onboardingwizard begeleidt de eerste Radarr- en Sonarr-configuratie.
- Dashboardwidgets ondersteunen drag-and-drop layouts en live updates via SignalR.
- Job queue- en vertaalgeschiedenis-widgets geven zichtbaarheid die upstream momenteel niet heeft.
- De API-gebruikswidget toont gebruiksmetrics zoals calls, tokens, latency, errors en success rate.
- Mislukkingen verschijnen in een quality-gate-audit waar je de problematische cue inline kunt bewerken en dan accepteren of afwijzen. Mislukte batches zijn in bulk opnieuw in de wachtrij te zetten of te verwerpen.
- Een completed translation compare viewer laat je na afloop de bron en de vertaling naast elkaar vergelijken.
- De dashboardgeschiedenis-widget gebruikt oneindig scrollen in plaats van paginering, wat uitmaakt bij grote bibliotheken.
- De Upload Workspace is nu als tabblad in de Translations-pagina opgenomen om minder tussen pagina's te hoeven springen. Custom Sources blijft een eigen item in de instellingen.
- Configureerbare job scheduling leeft op de opnieuw ontworpen Tasks-pagina (voorheen Schedule), met per-job aan/uit-schakelaars, cron-expressies, gedeelde CardComponent-kaarten, responsive raster en expliciete laad-/leeg-statussen.
- De client bevat 11 ingebouwde thema's, niet alleen een licht/donker-schakelaar.
- De UI is vertaald naar Engels, Nederlands, Duits, Frans, Spaans, Pools en Vereenvoudigd Chinees.

### Betrouwbaarheid

- Opruimen van verweesde ondertitels detecteert hernoemde mediabestanden die vertaalde ondertitels hebben achtergelaten.
- Bulk integrity checks kunnen vertaalde ondertitels in de hele bibliotheek valideren.
- Ghost-job bescherming voorkomt het overschrijven van terminale statussen en ruimt onderbroken werk na een restart op.
- Exponential backoff en vertraagde requeue-logica verminderen druk op instabiele providers.
- Hervatting van gepauzeerde vertalingen houdt workerslots vast bij rate-limits en hervat automatisch wanneer de limiet opgeheven is.
- Silent token streaming voor AI-providers verlaagt de first-token-latentie bij lange vertalingen.
- Eigen vertaalqueues respecteren mediaprioriteit en vermijden head-of-line blocking wanneer een laagprioritaire vertaling vastloopt.
- De Chutes-, NanoGPT- en CrofAI-integraties bevatten elk quota-bewust gebruiksbeheer, providerspecifieke regels en een UI in deze fork.

---

## Ondersteunde diensten

Dit is wat er vandaag in onze fork werkt. Een deel hiervan wordt inmiddels ook door upstream ondersteund, dus dit is geen exclusieve fork-claim.

**AI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (met quota-tracking en automatische pauze)
- [NanoGPT](https://nano-gpt.com/) (met abonnement-gebruik, reserves en automatische pauze)
- [CrofAI](https://crof.ai/) (alleen-credits gebruiksregistratie; pauzeert vertalingen automatisch wanneer het creditsaldo nul bereikt)
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

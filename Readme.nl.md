# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Ondertiteling die echter werkt** - voor mensen die mediabibliotheken op schaal draaien.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Upgraden van v1.x?** Versie 2.0.0 heeft brekende wijzigingen - MySQL/MariaDB verwijderd, instellingen worden NIET gemigreerd, nieuwe start nodig. Zie hieronder voor details.

---

## Wat is dit?

Lingarr on Steroids is een fork van [Lingarr](https://github.com/lingarr-translate/lingarr). We hebben het basisidee behouden (ondertiteling vertalen via Radarr/Sonarr) maar het grootste deel van de backend opnieuw gebouwd en veel UI-verbeteringen toegevoegd.

Het begon omdat de originele Lingarr betrouwbaarheidsproblemen had onder belasting. We hadden iets nodig dat niet zou vallen wanneer je duizenden series hebt.

---

## Wat we veranderden

### Backend

| Wat | Waarom |
|------|-----|
| Custom vertaal-worker | Hangfire verstopte op grote wachtrijen. We hebben onze eigen BackgroundService geschreven die 1-20 parallelle workers, prioriteitswachtrijen en auto-herstel na crashes afhandelt. |
| PostgreSQL standaard | SQLite vergrendelt met gelijktijdige workers. MVCC in PostgreSQL werkt daadwerkelijk. We hebben SQLite als optie gehouden voor kleine setups. |
| 9-status vertaling-tracking | Het origineel had geen goede manier om te beantwoorden "wat moet vertaald worden?". We voegden statussen toe (Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted) zodat query's snel zijn. |
| Multi-instance ondersteuning | Eén Radarr/Sonarr instantie is niet genoeg voor sommige mensen. Je kunt nu meerdere *arr-servers op één Lingarr aansluiten. |
| Uitgestelde reparatie | Mislukte regels worden opnieuw geprobeerd met omliggende.context (10 regels standaard). LLM-vertalingskwaliteit stijgt aanzienlijk wanneer de AI kan zien wat ervoor/erna gebeurt. |

### Ondertiteling verwerken

- **FFmpeg extractie** - haalt ondertiteling uit MKV/MP4-containers wanneer ze ingesloten is
- **ASS/SSA opschoning** - verwijdert tekenopdrachten, muzieksymbolen, geluidseffect-placeholders, URL's
- **Dunne track-filter** - slaat tracks met <100 entries over (tekens, liedjes)
- **Externe ondertitel-ontdekking** - vindt ondertitelbestanden die je handmatig toevoegt en houdt ze bij

### UI/UX

- **Dashboard widgets** - drag-and-drop indeling, real-time updates via SignalR
- **Wachtrij-widget** - toont wat draait, wat gepland is, wat mislukt is
- **Vertalingsgeschiedenis** - chart + lijst die toont wat wanneer vertaald werd
- **API-gebruik-tracker** - sparkline-diagrammen die uitgave per dienst tonen
- **Onboarding-wizard** - eerste keer setup gidst je door Radarr/Sonarr-config
- **Thema-ondersteuning** - donker/licht met CSS-variabelen zodat je kunt matchen met je setup
- **7 talen** - EN, NL, DE, FR, ES, PL, ZH
- **Offline-detectie** - toont wanneer de app onbereikbaar is

### Betrouwbaarheid

- **Weesopruiming** - detecteert wanneer upgrade de bestandsnaam verandert en je AI-vertalingen nu wezen zijn
- **Bulk integriteitscontrole** - valideert elke vertaling in je bibliotheek
- **Spookjob-opruiming** - verwijdert vastgelopen jobs die nooit eindigden
- **Exponentiële backoff** - herhaalt met jitter zodat je geen mislukte API's blijft bestoken

---

## Ondersteunde diensten

**AI:**
- OpenAI (GPT)
- Anthropic (Claude)
- Google Gemini
- DeepSeek
- Chutes.ai (met quota-tracking & auto-pauze)
- LocalAI / Ollama (zelfgehost)

**Cloud API's:**
- LibreTranslate
- DeepL
- Google Translate
- Bing Translate
- Yandex Translate
- Azure Translator

---

## Aan de slag

### Docker image tags

| Tag | Beschrijving | Architecturen |
|-----|--------------|---------------|
| `latest` | Nieuwste stabiele release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Specifieke versie | `linux/amd64`, `linux/arm64` |
| `main` | Ontwikkelingsbuild | `linux/amd64`, `linux/arm64` |

> **Let op:** Alle images ondersteunen zowel AMD64 (Intel/AMD) als ARM64 (Raspberry Pi, Apple Silicon).

PostgreSQL wordt aanbevolen. SQLite werkt voor kleine setups (single gebruiker, <1000 media-items).

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

### SQLite (snelstart)

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

## Configuratie

| Variabele | Beschrijving | Standaard |
|-----------|--------------|-----------|
| `ASPNETCORE_URLS` | Poort | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` of `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL host | - |
| `DB_PORT` | PostgreSQL port | `5432` |
| `DB_DATABASE` | Database naam | - |
| `DB_USERNAME` | DB gebruikersnaam | - |
| `DB_PASSWORD` | DB wachtwoord | - |
| `RADARR_URL` | Je Radarr URL | - |
| `RADARR_API_KEY` | Radarr API-sleutel | - |
| `SONARR_URL` | Je Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API-sleutel | - |

Volledige lijst in [Settings.MD](Settings.MD).

---

## Credits

Originele Lingarr door [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Iconen: [Lucide](https://lucide.dev/icons).  
Ondertiteling-parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Vertaling: LibreTranslate, GTranslate bibliotheek.

---

## Bedankt

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)
# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Ondertiteling vertalen die écht werkt** - voor mensen die grote mediabibliotheken beheren.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Upgrade vanaf v1.x?** Versie 2.0.0 bevat breaking changes - MySQL/MariaDB verwijderd, instellingen worden NIET gemigreerd, een schone installatie is vereist. Zie hieronder voor details.

---

## Wat is dit?

Lingarr on Steroids is een fork van [Lingarr](https://github.com/lingarr-translate/lingarr). We hebben het kernidee behouden (ondertitels vertalen via Radarr/Sonarr) maar de meeste backend herbouwd en veel UI-verbeteringen toegevoegd.

Het is gestart omdat het originele Lingarr betrouwbaarheidsproblemen had onder grote belasting. We hadden iets nodig dat niet zou crashen wanneer je duizenden series en films hebt.

---

## Wat we hebben veranderd

### Backend

| Wat | Waarom |
|------|-----|
| Aangepaste vertalingsworker | Hangfire liep vast bij grote wachtrijen. We schreven onze eigen BackgroundService die 1-20 parallelle workers afhandelt, prioriteitswachtrijen en auto-herstel bij crashes. |
| Standaard PostgreSQL | SQLite loopt vast bij gelijktijdige workers. MVCC in PostgreSQL werkt wél. We hebben SQLite behouden als optie voor kleine setups. |
| 9-status vertalings-tracking | Het origineel had geen goede manier om te antwoorden "wat moet er vertaald worden?". We voegden statussen toe (Onbekend, In de wacht, Bezig, Voltooid, Verouderd, Wacht op bron, Geen geschikte ondertitels, Mislukt, Onderbroken) zodat queries snel zijn. |
| Multi-instantie ondersteuning | Eén Radarr/Sonarr instantie is niet genoeg voor sommigen. Je kan nu meerdere *arr servers verbinden met één Lingarr. |
| Uitgestelde reparatie | Mislukte regels worden opnieuw geprobeerd met omliggende context (standaard 10 regels). LLM-vertalingskwaliteit verbetert aanzienlijk wanneer de AI kan zien wat er voor/na gebeurt. |

### Ondertiteling verwerking

- **FFmpeg extractie** - haalt ondertitels uit MKV/MP4-containers wanneer ze zijn ingesloten
- **ASS/SSA opschonen** - verwijdert tekencommando's, muzieksymbolen, geluidseffect tags en URLs
- **Gedeeltelijke track filter** - slaat tracks over met <100 regels (bijv. alleen borden, liedjes)
- **Externe ondertitels ontdekking** - vindt handmatig toegevoegde ondertitelingsbestanden en trackt ze

### UI/UX

- **Dashboard widgets** - sleepbare (drag-and-drop) lay-out, real-time updates via SignalR
- **Wachtrij widget** - laat zien wat er draait, wat is gepland, wat is mislukt
- **Vertalingsgeschiedenis** - grafiek + lijst die laat zien wat en wanneer is vertaald
- **API-gebruik tracker** - minigrafiekjes die de kosten per dienst laten zien
- **Setup-assistent** - begeleidt je bij de eerste instelling met Radarr/Sonarr
- **Thema-ondersteuning** - donker/licht met CSS variabelen om bij je setup te passen
- **7 talen** - EN, NL, DE, FR, ES, PL, ZH
- **Offline detectie** - toont wanneer de app onbereikbaar is

### Betrouwbaarheid

- **Verweesde bestanden opschonen** - detecteert wanneer een upgrade de bestandsnaam wijzigt waardoor je AI-vertalingen ongebruikt raken
- **Massa-integriteitscontrole** - valideert elke vertaling in je bibliotheek
- **Vastgelopen taken opschonen** - verwijdert vastzittende taken die nooit klaar zijn
- **Exponentiële vertraging (Backoff)** - probeert het opnieuw met willekeurige vertraging (jitter) om API's bij storingen niet te overbelasten

---

## Ondersteunde diensten

**AI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (met quota-tracking & automatische pauze)
- LocalAI / Ollama (zelf-gehost)

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
|-----|-------------|---------------|
| `latest` | Nieuwste stabiele release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Specifieke versie | `linux/amd64`, `linux/arm64` |
| `main` | Ontwikkelingsversie | `linux/amd64`, `linux/arm64` |

PostgreSQL is aanbevolen. SQLite werkt voor kleine setups (enkele gebruiker, <1000 media-items).

> **Let op:** Alle images ondersteunen zowel AMD64 (Intel/AMD) als ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (aanbevolen)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Amsterdam
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
      - TZ=Europe/Amsterdam
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
|----------|-------------|---------|
| `ASPNETCORE_URLS` | Poort | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` of `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL host | - |
| `DB_PORT` | PostgreSQL poort | `5432` |
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
Ondertitel verwerking: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Vertaling: LibreTranslate, GTranslate-bibliotheek.

---

## Dank aan

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

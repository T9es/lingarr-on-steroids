# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Untertitel-Ubersetzung fur echte Radarr/Sonarr-Bibliotheken.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Dieses README beschreibt unseren Fork und den Stand von Upstream-Lingarr vom 27. Marz 2026. Wenn sich Upstream danach weiterentwickelt, konnen einzelne Details hier mit der Zeit veralten.
>
> Upgrade von v1.x? Version 2.0.0 bringt Breaking Changes mit. MySQL/MariaDB wird nicht mehr unterstutzt, Einstellungen werden nicht automatisch migriert, und ein sauberer Neustart ist erforderlich.

---

## Was ist das?

Lingarr on Steroids ist ein Fork von [Lingarr](https://github.com/lingarr-translate/lingarr). Die Grundidee bleibt gleich: Medien uber Radarr und Sonarr indizieren, Untertitel finden, mit unterstutzten Diensten ubersetzen und alles uber eine Weboberflache verwalten.

Dieser Fork konzentriert sich auf stabile Warteschlangen, Multi-Instance-Bibliotheken, robustere Reparatur von Untertiteln und bessere Betriebs-Transparenz fur grossere Setups.

---

## Was wir geandert haben

### Backend und Queueing

| Bereich | Was in unserem Fork anders ist |
|---------|-------------------------------|
| Eigener Translation Worker | Ubersetzungsjobs laufen uber einen eigenen `BackgroundService` mit konfigurierbaren parallelen Workern und nicht nur uber Hangfire-Queues. |
| PostgreSQL als Standard | PostgreSQL ist die Standard-Datenbank. SQLite bleibt fur kleinere Installationen verfugbar. |
| Medien-Statusmodell | Medien nutzen 9 Zustande: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `AwaitingSource`, `NoSuitableSubtitles`, `Failed`. |
| Multi-Instance-Support | Filme und Serien speichern `SourceInstanceId`, sodass mehrere Radarr- und Sonarr-Instanzen an eine Installation angebunden werden konnen. |
| Deferred Repair | Fehlgeschlagene Zeilen konnen mit umgebendem Kontext erneut versucht werden, was Reparaturlaufe robuster macht. |

### Untertitel-Verarbeitung

- FFmpeg kann textbasierte Untertitel aus eingebetteten MKV- und MP4-Spuren extrahieren.
- ASS/SSA-Bereinigung entfernt Zeichenbefehle, Musikmarker, Platzhalter-Effekte und URLs vor der Ubersetzung.
- Sparse Tracks mit weniger als 50 Dialogzeilen werden ubersprungen.
- Externe Untertitel-Erkennung erfasst manuell hinzugefugte Untertiteldateien automatisch.

### UI und Betrieb

- Der Onboarding-Assistent begleitet die erste Konfiguration von Radarr und Sonarr.
- Dashboard-Widgets unterstutzen Drag-and-Drop-Layouts und Live-Updates uber SignalR.
- Job-Queue- und Ubersetzungsverlauf-Widgets liefern Einblicke, die Upstream aktuell nicht mitbringt.
- Das API-Usage-Widget zeigt Aufrufe, Token, Latenz, Fehler und Erfolgsquote.
- Der Client bietet 11 integrierte Themes und nicht nur einen Hell/Dunkel-Schalter.
- Die UI ist in Englisch, Niederlandisch, Deutsch, Franzosisch, Spanisch, Polnisch und vereinfachtem Chinesisch verfugbar.

### Zuverlassigkeit

- Orphan-Subtitle-Cleanup erkennt umbenannte Mediendateien, die ubersetzte Untertitel zuruckgelassen haben.
- Bulk-Integrity-Checks konnen ubersetzte Untertitel in der gesamten Bibliothek validieren.
- Ghost-Job-Schutz verhindert das Uberschreiben terminaler Status und bereinigt unterbrochene Arbeit nach Neustarts.
- Exponentielles Backoff und verzogertes Requeueing reduzieren Druck auf instabile Provider.
- Die Chutes-Integration bringt quota-aware Steuerung und providerspezifische Logik in diesem Fork mit.

---

## Unterstutzte Dienste

Das ist die aktuelle Kompatibilitatsliste fur unseren Fork. Ein Teil davon wird inzwischen auch von Upstream unterstutzt, also ist das kein Anspruch auf Exklusivitat.

**KI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (mit Quota-Tracking und Auto-Pause)
- LocalAI / Ollama (selbst gehostet)

**Cloud APIs:**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## Erste Schritte

### Docker-Image-Tags

| Tag | Beschreibung | Architekturen |
|-----|-------------|---------------|
| `latest` | Aktuelles stabiles Release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Spezifische Version | `linux/amd64`, `linux/arm64` |
| `main` | Entwicklungs-Build | `linux/amd64`, `linux/arm64` |

PostgreSQL wird empfohlen. SQLite ist fur kleine Setups geeignet (ein Benutzer, <1000 Medienobjekte).

> Hinweis: Alle Images unterstutzen sowohl AMD64 (Intel/AMD) als auch ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (empfohlen)

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

### SQLite (Schnellstart)

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

## Konfiguration

| Variable | Beschreibung | Standardwert |
|----------|-------------|--------------|
| `TZ` | Container-Zeitzone | - |
| `ASPNETCORE_URLS` | HTTP-Bind-Adresse | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` oder `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | SQLite-Dateiname in `/app/config` | `local.db` |
| `DB_HOST` | PostgreSQL-Host | - |
| `DB_PORT` | PostgreSQL-Port | `5432` |
| `DB_DATABASE` | Datenbankname | - |
| `DB_USERNAME` | DB-Benutzername | - |
| `DB_PASSWORD` | DB-Passwort | - |
| `MAX_PARALLEL_TRANSLATIONS` | Startwert fur den eigenen Translation-Worker-Pool | `1` |
| `MAX_CONCURRENT_JOBS` | Hangfire-Workerzahl fur Sync- und System-Queues | `5` |
| `RADARR_URL` | Deine Radarr-URL | - |
| `RADARR_API_KEY` | Radarr-API-Schlussel | - |
| `SONARR_URL` | Deine Sonarr-URL | - |
| `SONARR_API_KEY` | Sonarr-API-Schlussel | - |

Die vollstandige Umgebungsvariablen-Referenz findest du in [Settings.MD](Settings.MD).

---

## Credits

Originales Lingarr von [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Icons: [Lucide](https://lucide.dev/icons).  
Untertitel-Parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Ubersetzung: LibreTranslate, GTranslate-Bibliothek.

---

## Danke

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

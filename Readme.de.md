# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Untertitel-Übersetzung, die wirklich funktioniert** - für Nutzer mit großen Medienbibliotheken.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Upgrade von v1.x?** Version 2.0.0 enthält Breaking Changes - MySQL/MariaDB wurde entfernt, Einstellungen werden NICHT migriert, ein Neuanfang ist erforderlich. Details siehe unten.

---

## Was ist das?

Lingarr on Steroids ist ein Fork von [Lingarr](https://github.com/lingarr-translate/lingarr). Wir haben die Kernidee beibehalten (Untertitel via Radarr/Sonarr übersetzen), aber den Großteil des Backends neu geschrieben und zahlreiche UI-Verbesserungen hinzugefügt.

Entstanden ist das Projekt, da das ursprüngliche Lingarr unter Last Zuverlässigkeitsprobleme aufwies. Wir brauchten eine Lösung, die nicht abstürzt, wenn man Tausende von Serien verwaltet.

---

## Was wir geändert haben

### Backend

| Was | Warum |
|------|-----|
| Eigener Übersetzungs-Worker | Hangfire war mit großen Warteschlangen überfordert. Wir haben einen eigenen BackgroundService geschrieben, der 1-20 parallele Worker, Prioritätswarteschlangen und automatische Fehlerbehebung bei Abstürzen unterstützt. |
| PostgreSQL als Standard | SQLite blockiert bei gleichzeitigen Workern. MVCC in PostgreSQL funktioniert zuverlässig. SQLite bleibt als Option für kleine Setups erhalten. |
| 9-Status-Übersetzungsverfolgung | Das Original hatte keine gute Möglichkeit zu beantworten, "Was muss übersetzt werden?". Wir haben Status hinzugefügt (Unbekannt, Ausstehend, In Bearbeitung, Abgeschlossen, Veraltet, Warten auf Quelle, Keine passenden Untertitel, Fehlgeschlagen, Unterbrochen), sodass Abfragen schnell sind. |
| Multi-Instanz-Support | Eine Radarr/Sonarr-Instanz reicht für manche nicht aus. Du kannst nun mehrere *arr-Server mit einem Lingarr verbinden. |
| Verzögerte Reparatur | Fehlgeschlagene Zeilen werden mit umgebendem Kontext (standardmäßig 10 Zeilen) erneut versucht. Die Qualität der LLM-Übersetzung steigt deutlich, wenn die KI sehen kann, was davor/danach passiert. |

### Untertitel-Verarbeitung

- **FFmpeg-Extraktion** - extrahiert Untertitel aus MKV/MP4-Containern, wenn diese eingebettet sind
- **ASS/SSA-Bereinigung** - entfernt Zeichenbefehle, Musiksymbole, Platzhalter für Soundeffekte und URLs
- **Sparse-Track-Filter** - überspringt Tracks mit <100 Einträgen (z. B. nur Schilder oder Lieder)
- **Erkennung externer Untertitel** - findet manuell hinzugefügte Untertiteldateien und verfolgt diese

### UI/UX

- **Dashboard-Widgets** - Drag-and-Drop-Layout, Echtzeit-Updates via SignalR
- **Job-Warteschlangen-Widget** - zeigt, was läuft, was geplant ist und was fehlgeschlagen ist
- **Übersetzungsverlauf** - Diagramm + Liste, die zeigt, was wann übersetzt wurde
- **API-Nutzungs-Tracker** - Sparkline-Diagramme zeigen die Ausgaben pro Dienst
- **Einrichtungsassistent** - führt dich beim ersten Start durch die Radarr/Sonarr-Konfiguration
- **Theme-Unterstützung** - Dunkel/Hell mit CSS-Variablen, damit es zu deinem Setup passt
- **7 Sprachen** - EN, NL, DE, FR, ES, PL, ZH
- **Offline-Erkennung** - zeigt an, wenn die App nicht erreichbar ist

### Zuverlässigkeit

- **Bereinigung von verwaisten Dateien** - erkennt, wenn ein Upgrade die Datei umbenennt und deine KI-Übersetzungen nun verwaist sind
- **Massen-Integritätsprüfung** - validiert jede Übersetzung in deiner Bibliothek
- **Bereinigung von Geister-Jobs** - entfernt hängengebliebene Jobs, die nie abgeschlossen wurden
- **Exponentielles Backoff** - wiederholt fehlgeschlagene API-Aufrufe mit Verzögerung (Jitter), um APIs nicht zu überlasten

---

## Unterstützte Dienste

**KI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (mit Quota-Tracking & Auto-Pause)
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

PostgreSQL wird empfohlen. SQLite funktioniert für kleine Setups (Einzelbenutzer, <1000 Medieneinträge).

> **Hinweis:** Alle Images unterstützen sowohl AMD64 (Intel/AMD) als auch ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (empfohlen)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Berlin
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
      - TZ=Europe/Berlin
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

## Konfiguration

| Variable | Beschreibung | Standardwert |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | Port | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` oder `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL Host | - |
| `DB_PORT` | PostgreSQL Port | `5432` |
| `DB_DATABASE` | Datenbankname | - |
| `DB_USERNAME` | DB Benutzername | - |
| `DB_PASSWORD` | DB Passwort | - |
| `RADARR_URL` | Deine Radarr URL | - |
| `RADARR_API_KEY` | Radarr API-Schlüssel | - |
| `SONARR_URL` | Deine Sonarr URL | - |
| `SONARR_API_KEY` | Sonarr API-Schlüssel | - |

Die vollständige Liste findest du unter [Settings.MD](Settings.MD).

---

## Credits

Ursprüngliches Lingarr von [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Icons: [Lucide](https://lucide.dev/icons).  
Untertitel-Parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Übersetzung: LibreTranslate, GTranslate Bibliothek.

---

## Danke

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

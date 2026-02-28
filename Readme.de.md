# Lingarr on Steroids

<div align="center">

[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker Pulls](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Erweiterte Untertitelubersetzung fur Power-User**  
*Gebaut fur Zuverlassigkeit, Leistung und kosteneffiziente KI-Workflows*

[Erste Schritte](#-erste-schritte) • [Warum dieser Fork?](#-warum-dieser-fork) • [Funktionen](#-funktionen) • [Ubersetzungsdienste](#-ubersetzungsdienste) • [Konfiguration](#-konfiguration) • [Multi-Language](Readme.MD) (English, Polski, Francais, Espanol, Nederlands, Chinese)

</div>

---

## Uberblick

**Lingarr on Steroids** ist ein spezialisierter Fork von [Lingarr](https://github.com/lingarr-translate/lingarr) - von Grund auf neu entwickelt fur Zuverlassigkeit, Leistung und kosteneffiziente KI-Nutzung in Untertitel-Ubersetzungs-Workflows.

Gebaut fur Benutzer, die grosse Medienbibliotheken verwalten und Enterprise-Level-Stabilitat von ihren Automatisierungstools erwarten.

### Was Sie bekommen

- Vollstandige Radarr & Sonarr Integration
- 12 Ubersetzungsdienste (KI + Cloud-APIs)
- Multi-Instance-Unterstutzung fur mehrere *arr-Server
- Echtzeit-Dashboard mit Drag-and-Drop-Widgets
- Tiefe Untertitel-Extraktion aus MKV/MP4-Containern
- Produktionsreife Architektur (PostgreSQL, gleichzeitige Worker, korrekte Statusverfolgung)

---

## Warum dieser Fork

Dieser Fork weicht erheblich vom Original-Lingarr ab. Wir haben nicht nur Funktionen hinzugefugt - wir haben die Kernarchitektur fur Produktionsumgebungen neu gebaut, in denen Zuverlassigkeit wichtig ist.

### Architektur

| Original Lingarr | Lingarr on Steroids |
|------------------|---------------------|
| Hangfire fur alle Jobs | Custom BackgroundService fur Ubersetzungen |
| SQLite Standard | PostgreSQL Standard mit MVCC |
| Einfache Job-Verfolgung | 9-Status TranslationState-System |
| Nur Single-Instance | Multi-Instance (mehrere *arr-Server) |
| Begrenzte Untertitel-Extraktion | FFmpeg-basierte tiefe Extraktion |
| Keine Batch-Retry-Logik | Deferred Contextual Repair |

### Was wir gebaut haben

**Custom Translation Worker**  
Hangfire durch einen dedicados Datenbank-getriebenen Service ersetzt, der 1-20 gleichzeitige Worker mit Prioritatswarteschlange verarbeitet. Jobs uberleben Neustarts. Worker erholen sich automatisch nach Absturzen.

```csharp
// Verarbeitet 1-20 gleichzeitige Ubersetzungen pro Instanz
// Prioritatswarteschlange: Medien sofort nach vorne bringen
// Kooperative Cancellation: Jobs sauber mittendrin stoppen
```

**Datenbankgetriebenes Status-System**  
Neun Ubersetzungszustande verfolgen jedes Medien-Element: Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted. Abfragen wie "was muss ubersetzt werden" laufen effizient. Stale-Erkennung lost Neuscans aus, wenn Einstellungen geandert werden.

**PostgreSQL First**  
SQLite funktioniert fur einfache Setups. PostgreSQL ist der Standard - MVCC eliminiert Lock-Contention wahrend schwerer paralleler Verarbeitung. MySQL/MariaDB-Unterstutzung wurde entfernt (sie verursachte zu viele Probleme).

### Untertitel-Verarbeitung

**Tiefe Embedded-Extraktion**  
FFmpeg pruft MKV/MP4-Container und extrahiert SRT, ASS und MOV_TEXT-Untertitel. Schicht-Merging kombiniert Forced/CC/SDH-Tracks intelligent. Dateien bleiben auf der Platte - Extraktion geschieht bei Bedarf.

**ASS/SSA Sanitation**  
Vektorgrafiken (Drawing-Blocks), Musiksymbole, Soundeffekt-Platzhalter, URLs und Credit-Zeilen werden herausgefiltert. Nur ubersetzbarer Dialog erreicht Ihre KI. Das Ergebnis: sauberere Ubersetzungen, weniger Fehler.

```json
// Filterung entfernt:
// Drawing-Befehle: {\p1}...{\p0}
// Musiksymbole: ♪ ♫ ♬
// Soundeffekte: [Tur knallt]
// URLs und Credit-Zeilen
```

**Sparse-Track-Erkennung**  
Tracks mit weniger als 100 Eintra gen (nur Signs, nur Songs) werden automatisch ubersprungen. Kein Verschwenden von Kontingent mehr fur Nicht-Dialog-Tracks.

### Ubersetzungs-Intelligenz

**Deferred Contextual Repair**  
Fehlgeschlagene Zeilen sammeln sich wahrend eines Batchs. Am Ende werden sie mit 10 Zeilen Kontext (konfigurierbar) wiederholt. LLMs ubersetzen besser mit Kontext - Recovery-Raten verbessern sich dramatisch.

**Batch-Ubersetzung**  
OpenAI, Anthropic, DeepSeek, Gemini, Chutes.ai und LocalAI unterstutzen Batch-Aufrufe. Senden Sie 50-100 Zeilen pro API-Anfrage. Context Wrapper fugt bei Bedarf umgebende Zeilen hinzu. Sie sparen Geld. Ubersetzungen sind genauer.

**Volle Chutes.ai-Integration**  
Echtzeit-Nutzungsverfolgung. Kontingent-Durchsetzung mit Puffern. Automatisches Pausieren wenn Limits erreicht werden. 402 PaymentRequired-Handling. Alles automatisiert - Sie musst keine Kontingent-Dashboards uberwachen.

### Workflow-Kontrolle

- **Prioritatswarteschlange**: Medien uber Flag oder UI nach vorne bringen
- **Runtime-Reordering**: Show-Prioritat andern -> Episoden sofort neu sortieren
- **Kooperative Cancellation**: Jobs mittendrin sauber abbrechen
- **Live-Test-Panel**: Dry-Run-Ubersetzungen mit Echtzeit-SSE-Logs
- **Cron-Selector**: Dropdown fur 15min / 30min / stundlich / taglich
- **Deduplikation**: Datenbank-Constraints verhindern doppelte Anfragen

### Zuverlassigkeitsfunktionen

**Orphaned Subtitle Cleanup**  
Wenn Radarr/Sonarr Medien upgraded (Dateinamen andern sich), werden AI-ubersetzte Dateien zu Orphans. Wir erkennen dies und raumen auf. Audit-Logs zeigen, was entfernt wurde.

**Bulk Integrity Check**  
Validiere jede Ubersetzung in deiner Bibliothek. Echtzeit-Fortschritt via SignalR. Erkenne korrupte oder unvollstandige Ubersetzungen, bevor es Wiedergabeprobleme gibt.

**Retry-Tracking**  
Exponential Backoff mit Jitter. Automatische Job-Wiederbelegung beim Start. Ghost-Job-Erkennung loscht festgefahrene Eintrager.

---

## Funktionen

### Dashboard & Uberwachung
- **Echtzeit-Dashboard**: TrueNAS-Style Widget-System mit Drag-and-Drop-Layout
- **SignalR-Updates**: Live-Fortschritt, aktive Ubersetzungen, Job-Warteschlange - kein Seiten-Refresh notig
- **Media Overview Widget**: Episodenweise Ubersetzungsstatus pro Show
- **Translation History Widget**: Chart + Liste mit Erfolg/Fehler-Aufschlusselung
- **Job Queue Widget**: Laufende Jobs, geplante Jobs, fehlgeschlagene Jobs - alles in einem
- **API Usage Widget**: Ausgaben pro Dienst mit Sparkline-Charts
- **Error Log Viewer**: Filterbare, durchsuchbare Logs mit XSS-Schutz

### Multi-Instance-Unterstutzung
- Verbinde mehrere Radarr/Sonarr-Instanzen
- Jede Instanz hat eigene Warteschlange und Einstellungen
- Duplikaterkennung uber Instanzen
- Einheitliche UI mit Instanz-Wechsel
- Migrations-Tool fur bestehende Single-Instance-Setups

### Lebensqualitat
- **Onboarding-Assistent**: Ersteinrichtung mitgefuhrter Radarr/Sonarr-Konfiguration
- **Theme-Unterstutzung**: Dunkel/Hell mit CSS-Variablen - integriere dich in dein Setup
- **Sprachauswahl**: 7 Sprachen (EN, NL, DE, FR, ES, PL, ZH)
- **Offline-Erkennung**: UI-Indikator wenn Anwendung nicht erreichbar
- **Bulk-Operationen**: Alle fehlgeschlagenen erneut einreihen, alle abgeschlossenen entfernen, alle integrity-checken

---

## Ubersetzungsdienste

Lingarr unterstutzt mehrere Ubersetzungsdienste, um deinen Anforderungen, Budget und Datenschutzanforderungen zu entsprechen:

**KI-gestutzte Ubersetzung**
- **[OpenAI](https://openai.com/)** - GPT-Modelle mit Batch-Ubersetzungsunterstutzung
- **[Anthropic](https://www.anthropic.com/)** - Claude-Modelle mit Batch-Ubersetzungsunterstutzung
- **[Google Gemini](https://gemini.google.com/)** - Google KI-Modelle mit Batch-Unterstutzung
- **[DeepSeek](https://deepseek.com)** - Kostengunstige KI mit Batch-Ubersetzungsunterstutzung
- **[Chutes.ai](https://chutes.ai)** - Open-Source-Modelle mit Nutzungsverfolgung und Kontingentverwaltung
- **LocalAI / Ollama** - Selbstgehostete Modelle (Ollama-kompatibel) mit Batch-Unterstutzung

**Cloud-Ubersetzungs-APIs**
- **[LibreTranslate](https://libretranslate.com)** - Selbstgehostete oder Cloud-Ubersetzung
- **[DeepL](https://www.deepl.com/)** - Professionelle Ubersetzungs-API
- **[Google Translate](https://translate.google.com/)** - Uber GTranslate-Bibliothek
- **[Bing Translate](https://www.bing.com/translator)** - Uber GTranslate-Bibliothek
- **[Yandex Translate](https://translate.yandex.com/)** - Uber GTranslate-Bibliothek
- **[Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)** - Uber GTranslate-Bibliothek

---

## Erste Schritte

### Docker-Image-Tags

Lingarr bietet Multi-Architektur-Docker-Images:

| Tag | Beschreibung | Architekturen |
|-----|--------------|---------------|
| `latest` | Neuestes stabiles Release | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Spezifische Version | `linux/amd64`, `linux/arm64` |
| `main` | Entwicklungs-Build | `linux/amd64`, `linux/arm64` |

> **Hinweis:** Alle Images unterstutzen sowohl AMD64 (Intel/AMD) als auch ARM64 (Raspberry Pi, Apple Silicon) Architekturen.

### Schnellstart

> [!WARNING]
> **Upgrade von v1.x?** Version 2.0.0 bringt breaking Changes:
> - **MySQL/MariaDB-Unterstutzung wurde entfernt.** Migriere zu PostgreSQL (empfohlen) oder SQLite.
> - **Einstellungen werden NICHT migriert.** Nach dem Upgrade neu konfigurieren (~5 Minuten).
> - **Medienbibliothek wird automatisch** von Radarr/Sonarr neu synchronisiert - keine Aktion notig.
> - **Vorherige Datenbanken pueden nicht migriert werden**; das ist ein Neustart.

**Empfohlen:** PostgreSQL ist die empfohlene Datenbank fur diesen Fork. Es verwendet MVCC (Multi-Version Concurrency Control), was Lock-Contention-Probleme wahrend schwerer paralleler Verarbeitung eliminiert.

<details>
<summary><b>PostgreSQL-Setup (Empfohlen)</b></summary>

```yaml
version: "3.8"

networks:
  lingarr:

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Berlin # Ersetze mit deiner Zeitzone
      - DB_CONNECTION=postgresql
      - DB_HOST=lingarr-postgres
      - DB_PORT=5432
      - DB_DATABASE=lingarr
      - DB_USERNAME=lingarr
      - DB_PASSWORD=CHANGE_ME_SECURE_PASSWORD # ANDERE DAS
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
      POSTGRES_USER: langarr
      POSTGRES_PASSWORD: CHANGE_ME_SECURE_PASSWORD # ANDERE DAS (Muss ubereinstimmen)
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

</details>

<details>
<summary><b>SQLite-Setup (Einfach)</b></summary>

Fur einfache Setups oder Tests, verwende SQLite, das keine zusatzlichen Container benotigt:

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Berlin # Ersetze mit deiner Zeitzone
      - DB_CONNECTION=sqlite
    volumes:
      - /path/to/media/movies:/movies
      - /path/to/media/tv:/tv
      - /path/to/config:/app/config
    ports:
      - "9876:9876"
    restart: unless-stopped
```

</details>

<details>
<summary><b>Docker CLI Setup</b></summary>

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

</details>

---

## Konfiguration

### Umgebungsvariablen

| Variable | Beschreibung | Standard |
|----------|--------------|----------|
| `ASPNETCORE_URLS` | Interner Port auf dem Lingarr lauscht | `http://+:9876` |
| `MAX_CONCURRENT_JOBS` | Hangfire Worker-Pool-Grosse fur Sync-Jobs | `20` |
| `DB_CONNECTION` | Datenbanktyp: `postgresql` oder `sqlite` | `postgresql` |
| `DB_HOST` | PostgreSQL Hostname (erforderlich fur PostgreSQL) | - |
| `DB_PORT` | PostgreSQL Port (erforderlich fur PostgreSQL) | `5432` |
| `DB_DATABASE` | Datenbankname (erforderlich fur PostgreSQL) | - |
| `DB_USERNAME` | Datenbank-Benutzername (erforderlich fur PostgreSQL) | - |
| `DB_PASSWORD` | Datenbank-Passwort (erforderlich fur PostgreSQL) | - |
| `DB_HANGFIRE_SQLITE_PATH` | SQLite-Pfad fur Hangfire (nur SQLite) | `/app/config/Hangfire.db` |
| `HANGFIRE_USERNAME` | Hangfire Dashboard Benutzername | `admin` |
| `HANGFIRE_PASSWORD` | Hangfire Dashboard Passwort | Zufallig (beim Start gedruckt) |

Zusatzliche Einstellungen können als Umgebungsvariablen konfiguriert werden, um uber Neuinstallationen hinweg zu bestehen. Siehe [Settings.MD](Settings.MD) fur die vollstandige Liste.

### LibreTranslate-Setup

Optional wenn ein anderer Ubersetzungsdienst verwendet wird.

<details>
<summary><b>Docker Compose</b></summary>

```yaml
  libretranslate:
    container_name: libretranslate
    image: libretranslate/libretranslate:latest
    restart: unless-stopped
    environment:
      - LT_LOAD_ONLY=en,de  # Ersetze mit deinen bevorzugten Sprachen
    ports:
      - 5000:5000
    volumes:
      - /path/to/config:/home/libretranslate/.local/share/argos-translate
    networks:
      - lingarr
    healthcheck:
      test: ["CMD-SHELL", "./venv/bin/python scripts/healthcheck.py"]
```

</details>

<details>
<summary><b>Docker CLI</b></summary>

```bash
mkdir -p /apps/libretranslate/{local,db}
chmod -R 777 /apps/libretranslate

docker run -d \
  --name libretranslate \
  -p 5000:5000 \
  -v /path/to/libretranslate/db:/app/db \
  -v /path/to/libretranslate/local:/home/libretranslate/.local \
  libretranslate/libretranslate \
  --disable-web-ui \
  --load-only=en,de     # Ersetze mit deinen bevorzugten Sprachen
```

</details>

**LibreTranslate Umgebungsvariablen:**

| Variable | Beschreibung |
|----------|--------------|
| `LT_LOAD_ONLY` | Quellsprachen nach [ISO-Code](https://libretranslate.com/languages) |
| `LT_DISABLE_WEB_UI` | Deaktiviert die Web-UI (auf beliebigen Wert setzen) |

---

##API-Integration

Lingarr bietet eine RESTful API fur die Integration von Untertitel-Ubersetzungsfhigkeiten in Ihre Anwendungen. Vollstandige API-Dokumentation mit Swagger-Definitionen ist verfugbar unter:

[Lingarr API-Dokumentation](https://lingarr.com/docs/api/)

---

## Mitwirken

Wir begrussen Beitrage! Ob Fehlerberichte, Feature-Anfragen oder Code-Beitrage, bitte zögern Sie nicht zu helfen.

Besuchen Sie das [Lingarr on Steroids](https://github.com/T9es/lingarr-on-steroids) GitHub-Repository, um zu beginnen.

---

## Credits

Dieses Projekt baut auf dem Fundament des originalen [Lingarr](https://github.com/lingarr-translate/lingarr) Projekts von rowanfuchs auf.

- Icons: [Lucide](https://lucide.dev/icons)
- Untertitel-Parsing: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser)
- Ubersetzungsdienste: [LibreTranslate](https://libretranslate.com)
- GTranslate: [GTranslate](https://github.com/d4n3436/GTranslate)

---

## Besonderer Dank

Fur die Unterstutzung von Open Source:
- [selfh.st by Ethan](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)
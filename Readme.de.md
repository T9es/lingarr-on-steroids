# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Untertitel-Ubersetzung fur echte Radarr/Sonarr-Bibliotheken.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Dieses README beschreibt unseren Fork und den Stand von Upstream-Lingarr vom 29. Juni 2026. Wenn sich Upstream danach weiterentwickelt, konnen einzelne Details hier mit der Zeit veralten.
>
> Upgrade von v1.x? Version 2.0.0 bringt Breaking Changes mit. MySQL/MariaDB wird nicht mehr unterstutzt, Einstellungen werden nicht automatisch migriert, und ein sauberer Neustart ist erforderlich.
>
> Upgrade von v2.x auf v3.0.0? Siehe [CHANGELOG](CHANGELOG.md) fur die Migration. Die Schedule-Seite heisst jetzt Tasks. Onboarding-Assistent, konfigurierbares Job-Scheduling und das Post-Translation Quality Gate wurden geandert. CrofAI ist als KI-Provider dazugekommen. Die Versionsanzeige unten links zeigt jetzt die echte Dev-Build-Version statt "Dev Build".

---

## Was ist neu in v3.0.0

Die v3-Version bringt umfangreiche Anderungen gegenuber v2.5.0. Wenn du nur einen Abschnitt liest, dann diesen.

- **Versionsverwaltung aus Git.** Die Assembly-Version wird jetzt zur Build-Zeit aus `git describe` aufgelost, und der Docker-Build gibt einen `VERSION`-Parameter weiter. Ein Release wird jetzt einfach durch Taggen von `v3.0.0` und Pushen des Tags ausgespielt. `Lingarr.Core.csproj` muss fur ein Release nicht mehr manuell bearbeitet werden.
- **Dev-Build-Badge zeigt die echte Version.** Das Badge unten links in der Seitenleiste zeigt jetzt `Dev <version>` (zum Beispiel `Dev 3.0.0-216-g39ae09b2`) statt des bisherigen generischen `Dev Build`-Texts.
- **CrofAI ist jetzt ein unterstutzter KI-Provider** mit reiner Credit-Nutzungsverfolgung. Ubersetzungen pausieren automatisch, wenn dein CrofAI-Credit-Guthaben null erreicht. Siehe die neuen `CROFAI_*`-Umgebungsvariablen in [Settings.MD](Settings.MD).
- **OCR fur Bitmap-Untertitel.** DVD/VobSub, PGS und andere bildbasierte Untertitel werden jetzt per OCR in Text umgewandelt und dann wie jede andere Quelle ubersetzt. Zwei neue Medienstatus (`OcrPending`, `OcrBlocked`) decken den OCR-Lebenszyklus ab.
- **Pro-Provider Circuit Breaker.** Wirft ein Provider vermehrt 5xx-Fehler, offnet sich der Circuit und Anfragen werden kurzzeitig pausiert, statt dein API-Kontingent wahrend einer Provider-Storung zu verbrauchen.
- **Wiederaufnahme pausierter Ubersetzungen.** Provider-429er (zum Beispiel Gemini-Rate-Limits) beenden eine Ubersetzung nicht mehr. Der Worker halt den Slot und setzt die Ubersetzung fort, sobald das Limit zuruckgesetzt ist.
- **Post-Translation Quality Gate.** Nach Abschluss eines Batches werden die verbliebenen Absatze bewertet. Die UI erlaubt es, Items ausserhalb der Toleranz zu prufen, zu bearbeiten, zu akzeptieren oder abzulehnen, inklusive Requeue All / Dismiss All als Sammelaktion.
- **Auto-Quellsprachmodus.** Die Quellsprache kann pro Cue automatisch erkannt werden, mit NLLB (FLORES-200 spBLEU), LLM-Tier-Vergleichen und Sprachfamilien-Heuristiken. Schalter im Onboarding und in den Quellsprach-Einstellungen.
- **Konfigurierbares Job-Scheduling auf der neuen Tasks-Seite.** Jeder Hangfire- und Ubersetzungsjob hat einen eigenen Enable-Schalter und einen Cron-Ausdruck. Die Tasks-Seite ist die umbenannte und neu gestaltete Schedule-Seite, mit geteilten CardComponent-Karten, einem responsiven 1/2/3-Spalten-Raster, Lade- und Leerzustanden und korrigiertem SignalR-Cleanup. Der alte Automationsblock in der Limits-Karte ist weg.
- **Konfigurierbares Embedding und Spracherkennung mit neuer UI.** Frontend-Einstellungen fur MKV-Embedded-Subtitle-Verhalten, untagged-Stream-Spracherkennung und ein Request-Retry-Cap.
- **MKV-Embed-Fallback fur lange Ausgabepfade.** Wenn der Pfad der ubersetzten Untertiteldatei gangige Dateisystemlimits uberschreitet (lange Anime-Dateinamen sind der typische Ausloser), wird die Ubersetzung direkt in das Original-MKV eingebettet.
- **Upload-Workspace unter Ubersetzungen verschoben.** Der Upload Workspace ist jetzt als Tab in der Ubersetzungs-Seite erreichbar, sodass man nicht mehr zwischen Top-Level-Seiten wechseln muss.
- **Dashboard Infinite Scroll, Completed Translation Compare Viewer, API-Usage-Widget-Erweiterungen.** Quality-of-Life-Updates, die sich bei grossen Bibliotheken bezahlt machen.
- **GitHub-Issue-Vorlagen** fur Bug, Feature und Setup-Fragen liegen unter `.github/ISSUE_TEMPLATE/`. Bitte nutze sie beim Offnen von Issues.

Die vollstandige Migrationsanleitung steht in [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300).

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
| 11-Zustande-Medienmodell | Medien erfassen den Ubersetzungsstatus in 11 Zustanden inklusive OCR-Lebenszyklus: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `NoSuitableSubtitles`, `Failed`, `AwaitingSource`, `OcrPending`, `OcrBlocked`. Die Entscheidungslogik liegt in `MediaStateService`. |
| Multi-Instance-Support | Filme und Serien speichern `SourceInstanceId`, sodass mehrere Radarr- und Sonarr-Instanzen an eine Installation angebunden werden konnen. |
| Deferred Repair | Fehlgeschlagene Zeilen konnen mit umgebendem Kontext erneut versucht werden, was Reparaturlaufe robuster macht. |
| Pro-Provider Circuit Breaker | Ein Singleton-Circuit-Breaker verfolgt Fehler pro Ubersetzungs-Provider und pausiert automatisch, wenn Fehlerschwellen uberschritten werden. |
| Wiederaufnahme pausierter Ubersetzungen | Ubersetzungsanfragen, die auf Provider-Rate-Limits (zum Beispiel Gemini 429) stossen, pausieren mit gehaltenem Worker-Slot und werden automatisch fortgesetzt, sobald das Limit zuruckgesetzt ist. |
| Post-Translation Quality Gate | Nach Abschluss eines Batches werden verbliebene Absatze mit konfigurierbarer Toleranz bewertet. Die UI erlaubt Bearbeiten oder Ablehnen, mit Schalter in den Einstellungen. Standard ist aktiv. |

### Untertitel-Verarbeitung

- FFmpeg kann textbasierte Untertitel aus eingebetteten MKV- und MP4-Spuren extrahieren.
- ASS/SSA-Bereinigung entfernt Zeichenbefehle, Musikmarker, Platzhalter-Effekte und URLs vor der Ubersetzung.
- Sparse Tracks mit weniger als 50 Dialogzeilen werden ubersprungen.
- Externe Untertitel-Erkennung erfasst manuell hinzugefugte Untertiteldateien automatisch.
- Bitmap-Untertitel-Spuren (DVD/VobSub, PGS usw.) werden per OCR in Text umgewandelt und dann wie jede andere Quelle ubersetzt.
- ASS-Integritatsprufungen fangen durchsickernde Tag-Fragmente ab, sodass Ubersetzungs-Prompts keine Zeichenbefehle als Dialog aufnehmen.
- Lange Ubersetzungs-Ausgabepfade, die gangige Dateisystemlimits uberschreiten wurden, werden statt neben der Mediendatei direkt in das Original-MKV eingebettet.

### UI und Betrieb

- Der Onboarding-Assistent begleitet die erste Konfiguration von Radarr und Sonarr.
- Dashboard-Widgets unterstutzen Drag-and-Drop-Layouts und Live-Updates uber SignalR.
- Job-Queue- und Ubersetzungsverlauf-Widgets liefern Einblicke, die Upstream aktuell nicht mitbringt.
- Das API-Usage-Widget zeigt Aufrufe, Token, Latenz, Fehler und Erfolgsquote.
- Fehlschlage erscheinen in einem Quality-Gate-Audit, in dem du die problematische Zeile inline bearbeiten und dann akzeptieren oder ablehnen kannst. Fehlgeschlagene Batches konnen gesammelt neu eingereiht oder verworfen werden.
- Ein Completed Translation Compare Viewer erlaubt es, Quell- und Zieltext nach Abschluss direkt nebeneinander zu vergleichen.
- Das Dashboard-Verlaufs-Widget nutzt Infinite Scroll statt Paginierung, was bei grossen Bibliotheken wichtig wird.
- Der Upload Workspace ist jetzt als Tab in der Ubersetzungs-Seite erreichbar, um Seitenwechsel zu reduzieren. Custom Sources bleibt ein eigener Eintrag in den Einstellungen.
- Konfigurierbares Job-Scheduling liegt auf der neu gestalteten Tasks-Seite (vormals Schedule) mit Per-Job-Enable-Schaltern, Cron-Ausdrucken, geteilten CardComponent-Karten, responsivem Raster und expliziten Lade-/Leerzustanden.
- Der Client bietet 11 integrierte Themes und nicht nur einen Hell/Dunkel-Schalter.
- Die UI ist in Englisch, Niederlandisch, Deutsch, Franzosisch, Spanisch, Polnisch und vereinfachtem Chinesisch verfugbar.

### Zuverlassigkeit

- Orphan-Subtitle-Cleanup erkennt umbenannte Mediendateien, die ubersetzte Untertitel zuruckgelassen haben.
- Bulk-Integrity-Checks konnen ubersetzte Untertitel in der gesamten Bibliothek validieren.
- Ghost-Job-Schutz verhindert das Uberschreiben terminaler Status und bereinigt unterbrochene Arbeit nach Neustarts.
- Exponentielles Backoff und verzogertes Requeueing reduzieren Druck auf instabile Provider.
- Die Wiederaufnahme pausierter Ubersetzungen halt Worker-Slots fur Ubersetzungen, die auf Rate-Limits stossen (zum Beispiel Gemini 429), und setzt sie automatisch fort, statt die Anfrage fehlschlagen zu lassen.
- Silent Token Streaming fur KI-Provider reduziert die First-Token-Latenz bei langen Ubersetzungen.
- Eigene Ubersetzungs-Queues respektieren die Medienprioritat und vermeiden Head-of-Line-Blocking, wenn eine niedrigpriorisierte Ubersetzung stockt.
- Die Chutes-, NanoGPT- und CrofAI-Integrationen bringen jeweils quota-aware Nutzungslogik, providerspezifische Steuerung und eine UI in diesem Fork mit.

---

## Unterstutzte Dienste

Das ist die aktuelle Kompatibilitatsliste fur unseren Fork. Ein Teil davon wird inzwischen auch von Upstream unterstutzt, also ist das kein Anspruch auf Exklusivitat.

**KI:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (mit Quota-Tracking und Auto-Pause)
- [NanoGPT](https://nano-gpt.com/) (mit Subscription-Nutzung, Reserves und Auto-Pause)
- [CrofAI](https://crof.ai/) (reine Credit-Nutzung; pausiert Ubersetzungen automatisch, wenn das Credit-Guthaben null erreicht)
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

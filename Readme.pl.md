# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Tłumaczenie napisów, które naprawdę działa** - dla osób zarządzających dużymi bibliotekami multimediów.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Aktualizacja z v1.x?** Wersja 2.0.0 wprowadza zmiany powodujące niezgodność wsteczną - usunięto MySQL/MariaDB, ustawienia NIE są migrowane, wymagana jest czysta instalacja. Zobacz poniżej po więcej szczegółów.

---

## Czym to jest?

Lingarr on Steroids to rozwidlenie (fork) projektu [Lingarr](https://github.com/lingarr-translate/lingarr). Zachowaliśmy główną ideę (tłumaczenie napisów poprzez Radarr/Sonarr), ale przebudowaliśmy większość backendu i dodaliśmy mnóstwo ulepszeń interfejsu (UI).

Projekt powstał, ponieważ oryginalny Lingarr miał problemy ze stabilnością pod obciążeniem. Potrzebowaliśmy czegoś, co nie ulegnie awarii, gdy masz tysiące seriali.

---

## Co zmieniliśmy

### Backend

| Co | Dlaczego |
|------|-----|
| Niestandardowy system zadań w tle | Hangfire dławił się przy dużych kolejkach. Napisaliśmy własny BackgroundService, który obsługuje 1-20 równoległych procesów, kolejkowanie priorytetowe i automatyczne wznawianie po awariach. |
| Domyślnie PostgreSQL | SQLite blokuje się przy wielu jednoczesnych procesach. MVCC w PostgreSQL działa znakomicie. Zostawiliśmy SQLite jako opcję dla bardzo małych instalacji. |
| 9-stanowe śledzenie tłumaczeń | Oryginał nie potrafił łatwo odpowiedzieć na pytanie "co wymaga przetłumaczenia?". Dodaliśmy stany (Nieznany, Oczekujący, W toku, Zakończony, Przestarzały, Oczekujący na źródło, Brak odpowiednich napisów, Zakończony niepowodzeniem, Przerwany), aby zapytania działały błyskawicznie. |
| Obsługa wielu instancji | Jedna instancja Radarr/Sonarr to dla niektórych za mało. Teraz możesz połączyć wiele serwerów *arr z jednym Lingarr. |
| Opóźniona naprawa błędów | Błędne linie są ponawiane z użyciem otaczającego je kontekstu (domyślnie 10 linii). Jakość tłumaczenia przez modele LLM znacznie wzrasta, gdy AI "widzi", co dzieje się wcześniej/później. |

### Przetwarzanie napisów

- **Ekstrakcja FFmpeg** - wyciąga napisy z kontenerów MKV/MP4, gdy są w nich osadzone
- **Czyszczenie ASS/SSA** - usuwa polecenia rysowania, symbole muzyczne, tagi efektów dźwiękowych i adresy URL
- **Filtr ścieżek "rzadkich" (sparse)** - pomija ścieżki zawierające <100 wpisów (tylko znaki, piosenki itp.)
- **Wykrywanie zewnętrznych napisów** - odnajduje ręcznie dodane pliki z napisami i śledzi je w systemie

### Interfejs użytkownika (UI/UX)

- **Widżety pulpitu** - układ "przeciągnij i upuść", aktualizacje w czasie rzeczywistym przez SignalR
- **Widżet kolejki zadań** - pokazuje, co jest w toku, co zaplanowano, a co się nie powiodło
- **Historia tłumaczeń** - wykres i lista pokazujące co i kiedy zostało przetłumaczone
- **Śledzenie użycia API** - miniaturowe wykresy pokazujące wydatki na daną usługę
- **Kreator pierwszej konfiguracji** - przewodnik pomagający w konfiguracji z Radarr/Sonarr
- **Obsługa motywów** - ciemny/jasny z użyciem zmiennych CSS, by dopasować wygląd do Twojego panelu
- **7 języków** - EN, NL, DE, FR, ES, PL, ZH
- **Wykrywanie offline** - pokazuje, kiedy aplikacja jest niedostępna (rozłączona)

### Niezawodność

- **Usuwanie osieroconych wpisów** - wykrywa sytuacje, gdy po aktualizacji odcinek zmienił nazwę i Twoje tłumaczenia AI stały się "osierocone"
- **Masowe sprawdzanie integralności** - weryfikuje każde tłumaczenie w Twojej bibliotece
- **Usuwanie zablokowanych zadań** - oczyszcza system z procesów, które nigdy się nie zakończyły
- **Wykładnicze opóźnienie (Exponential backoff)** - ponawia próby z opóźnieniem (jitter), by nie bombardować serwerów API po błędach

---

## Obsługiwane usługi

**Sztuczna inteligencja (AI):**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (ze śledzeniem limitów i autowstrzymaniem)
- LocalAI / Ollama (self-hosted)

**Interfejsy API chmury:**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## Rozpoczęcie pracy

### Tagi obrazów Docker

| Tag | Opis | Architektury |
|-----|-------------|---------------|
| `latest` | Najnowsza stabilna wersja | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Konkretna wersja | `linux/amd64`, `linux/arm64` |
| `main` | Wersja rozwojowa | `linux/amd64`, `linux/arm64` |

PostgreSQL jest zalecany. SQLite nadaje się do małych instalacji (jeden użytkownik, <1000 pozycji medialnych).

> **Uwaga:** Wszystkie obrazy obsługują zarówno AMD64 (Intel/AMD), jak i ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (zalecany)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Warsaw
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

### SQLite (szybki start)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    environment:
      - TZ=Europe/Warsaw
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

## Konfiguracja

| Zmienna | Opis | Domyślnie |
|----------|-------------|---------|
| `ASPNETCORE_URLS` | Port | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` lub `sqlite` | `postgresql` |
| `DB_HOST` | Host PostgreSQL | - |
| `DB_PORT` | Port PostgreSQL | `5432` |
| `DB_DATABASE` | Nazwa bazy danych | - |
| `DB_USERNAME` | Nazwa użytkownika bazy danych | - |
| `DB_PASSWORD` | Hasło bazy danych | - |
| `RADARR_URL` | Adres URL Radarr | - |
| `RADARR_API_KEY` | Klucz API Radarr | - |
| `SONARR_URL` | Adres URL Sonarr | - |
| `SONARR_API_KEY` | Klucz API Sonarr | - |

Pełna lista znajduje się w pliku [Settings.MD](Settings.MD).

---

## Autorzy i prawa autorskie

Oryginalny projekt Lingarr autorstwa [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Ikony: [Lucide](https://lucide.dev/icons).  
Analizator napisów: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Tłumaczenia: LibreTranslate, biblioteka GTranslate.

---

## Podziękowania

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

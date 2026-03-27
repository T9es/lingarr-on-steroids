# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Tlumaczenie napisow dla prawdziwych bibliotek Radarr/Sonarr.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Snapshot porownany z `lingarr-translate/lingarr` z dnia 27 marca 2026. Upstream moze sie po tej dacie dalej zmieniac.
>
> Aktualizacja z v1.x? Wersja 2.0.0 wprowadza breaking changes. MySQL/MariaDB nie jest juz wspierany, ustawienia nie sa automatycznie migrowane i wymagany jest czysty start.

---

## Czym to jest?

Lingarr on Steroids to fork projektu [Lingarr](https://github.com/lingarr-translate/lingarr). Zachowuje ten sam podstawowy workflow: indeksowanie mediow z Radarr i Sonarr, wykrywanie napisow, tlumaczenie przez wspierane providery i zarzadzanie wszystkim z poziomu web UI.

Ten fork skupia sie na stabilnosci kolejek, obsludze wielu instancji, naprawie napisow i lepszej widocznosci operacyjnej dla wiekszych instalacji.

---

## Zweryfikowane roznice tego forka

### Backend i kolejkowanie

| Obszar | Zweryfikowana roznica w tym forku |
|--------|-----------------------------------|
| Wlasny translation worker | Zadania tlumaczen dzialaja przez wlasny `BackgroundService` z konfigurowalna liczba workerow, a nie tylko przez kolejki Hangfire. |
| PostgreSQL jako domyslny wybor | PostgreSQL jest domyslna baza danych. SQLite nadal jest wspierany dla mniejszych instalacji. |
| Model stanow mediow | Media uzywaja 9 stanow: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `AwaitingSource`, `NoSuitableSubtitles`, `Failed`. |
| Wsparcie wielu instancji | Filmy i seriale zapisują `SourceInstanceId`, dzieki czemu jedna instalacja moze obslugiwac wiele instancji Radarr i Sonarr. |
| Deferred repair | Nieudane linie moga byc ponawiane z otaczajacym kontekstem, co poprawia skutecznosc naprawy. |

### Przetwarzanie napisow

- FFmpeg potrafi wyciagnac tekstowe napisy z osadzonych sciezek MKV i MP4.
- Czyszczenie ASS/SSA usuwa komendy rysowania, znaczniki muzyczne, placeholdery efektow i URL-e przed tlumaczeniem.
- Rzadkie sciezki z mniej niz 50 liniami dialogow sa pomijane.
- Wykrywanie zewnetrznych napisow znajduje recznie dodane pliki i dalej je sledzi.

### UI i operacje

- Kreator onboardingu prowadzi przez pierwsza konfiguracje Radarr i Sonarr.
- Widgety dashboardu obsluguja drag and drop i live update przez SignalR.
- Widget kolejki zadan i historia tlumaczen daja widocznosc, ktorej upstream obecnie nie dostarcza.
- Widget zuzycia API pokazuje metryki uzycia: liczbe wywolan, tokeny, opoznienia, bledy i success rate.
- Klient ma 11 wbudowanych motywow, a nie tylko przelacznik jasny/ciemny.
- UI jest dostepne po angielsku, holendersku, niemiecku, francusku, hiszpansku, polsku i po uproszczonemu chinsku.

### Niezawodnosc

- Czyszczenie osieroconych napisow wykrywa przemianowane media, po ktorych zostaly przetlumaczone pliki.
- Zbiorcze sprawdzanie integralnosci potrafi zweryfikowac napisy w calej bibliotece.
- Ochrona przed ghost jobami zapobiega nadpisywaniu stanow koncowych i sprzata przerwane zadania po restarcie.
- Exponential backoff i opoznione ponowne kolejkowanie zmniejszaja nacisk na niestabilne providery.
- Integracja z Chutes zawiera quota-aware obsluge limitow i logike specyficzna dla tego providera.

---

## Obslugiwane uslugi

To lista kompatybilnosci tego forka na dzien snapshotu. Czesc z tych uslug jest juz rowniez wspierana przez upstream, wiec nie traktuj tej listy jako claimu "only in fork".

**Sztuczna inteligencja (AI):**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (ze sledzeniem limitow i auto-pauza)
- LocalAI / Ollama (self-hosted)

**Interfejsy API chmury:**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## Start

### Tagi obrazow Docker

| Tag | Opis | Architektury |
|-----|------|---------------|
| `latest` | Najnowsza stabilna wersja | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Konkretna wersja | `linux/amd64`, `linux/arm64` |
| `main` | Wersja rozwojowa | `linux/amd64`, `linux/arm64` |

Polecamy PostgreSQL. SQLite nadaje sie do malych instalacji (jeden uzytkownik, <1000 pozycji mediow).

> Uwaga: Wszystkie obrazy wspieraja AMD64 (Intel/AMD) i ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (zalecany)

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

### SQLite (szybki start)

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

## Konfiguracja

| Zmienna | Opis | Domyslnie |
|---------|------|-----------|
| `TZ` | Strefa czasowa kontenera | - |
| `ASPNETCORE_URLS` | Adres HTTP do nasluchiwania | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` lub `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | Nazwa pliku SQLite w `/app/config` | `local.db` |
| `DB_HOST` | Host PostgreSQL | - |
| `DB_PORT` | Port PostgreSQL | `5432` |
| `DB_DATABASE` | Nazwa bazy danych | - |
| `DB_USERNAME` | Uzytkownik bazy danych | - |
| `DB_PASSWORD` | Haslo bazy danych | - |
| `MAX_PARALLEL_TRANSLATIONS` | Startowa liczba workerow tlumaczen | `1` |
| `MAX_CONCURRENT_JOBS` | Liczba workerow Hangfire dla kolejek sync i systemowych | `5` |
| `RADARR_URL` | Adres URL Radarr | - |
| `RADARR_API_KEY` | Klucz API Radarr | - |
| `SONARR_URL` | Adres URL Sonarr | - |
| `SONARR_API_KEY` | Klucz API Sonarr | - |

Pelna referencja zmiennych srodowiskowych jest w [Settings.MD](Settings.MD).

---

## Autorzy i prawa autorskie

Oryginalny projekt Lingarr autorstwa [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Ikony: [Lucide](https://lucide.dev/icons).  
Parser napisow: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Tlumaczenia: LibreTranslate, biblioteka GTranslate.

---

## Podziekowania

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Tlumaczenie napisow dla prawdziwych bibliotek Radarr/Sonarr.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> To README opisuje nasz fork i stan upstreamowego Lingarra z 27 marca 2026. Jesli upstream zmieni sie pozniej, czesc szczegolow tutaj moze z czasem przestac byc idealnie aktualna.
>
> To README opisuje nasz fork i stan upstreamowego Lingarra z 29 czerwca 2026. Jesli upstream zmieni sie pozniej, czesc szczegolow tutaj moze z czasem przestac byc idealnie aktualna.
>
> Aktualizacja z v1.x? Wersja 2.0.0 wprowadza breaking changes. MySQL/MariaDB nie jest juz wspierany, ustawienia nie sa automatycznie migrowane i wymagany jest czysty start.
>
> Aktualizacja z v2.x do v3.0.0? Sprawdz [CHANGELOG](CHANGELOG.md) z notatkami migracyjnymi. Strona Schedule zmienila nazwe na Tasks. Kreator onboardingu, konfigurowalne planowanie zadan oraz post-translation quality gate zostaly zmienione. CrofAI dolaczyl do wspieranych providerow AI. Znaczek wersji w lewym dolnym rogu pokazuje teraz prawdziwa wersje dev-build zamiast "Dev Build".

---

## Co nowego w v3.0.0

Wersja v3 to powazna zmiana w stosunku do v2.5.0. Jesli przeczytasz tylko jedna sekcje, to przeczytaj te.

- **Wersjonowanie swiadome Git.** Wersja assembly jest teraz rozwiazywana z `git describe` w czasie budowania, a build Dockera przekazuje argument `VERSION`. Wydanie v3.0.0 to juz tylko otagowanie `v3.0.0` i push taga. `Lingarr.Core.csproj` nie trzeba juz recznie edytowac.
- **Znaczek Dev Build pokazuje prawdziwa wersje.** Znaczek w lewym dolnym rogu pokazuje teraz `Dev <version>` (na przyklad `Dev 3.0.0-216-g39ae09b2`) zamiast ogolnego tekstu.
- **CrofAI jest teraz wspieranym providerem AI** z sledzeniem uzycia tylko na kredyty. Tlumaczenia pauzuja sie automatycznie, gdy saldo kredytow CrofAI spadnie do zera. Zobacz nowe zmienne `CROFAI_*` w [Settings.MD](Settings.MD).
- **OCR dla napisow bitmapowych.** Sciezki DVD/VobSub, PGS i inne oparte na obrazie sa przetwarzane przez OCR, a nastepnie tlumaczone jak kazde inne zrodlo. Dwa nowe stany (`OcrPending`, `OcrBlocked`) obejmuja cykl zycia OCR.
- **Circuit breaker per provider.** Gdy provider zaczyna zwracac bledy 5xx, obwod sie otwiera i zapytania sa pauzowane, zamiast zuzywas twojego limitu API podczas awarii.
- **Wznawianie wstrzymanych tlumaczen.** 429 od providera (na przyklad limity Gemini) nie koncza juz tlumaczenia. Worker trzyma slot i wznawia, gdy limit zostanie zniesiony.
- **Post-translation quality gate.** Po zakonczeniu batcha pozostale akapity sa oceniane. UI pozwala przegladac, edytowac, akceptowac lub odrzucac pozycje poza tolerancja, lacznie z akcjami masowymi Requeue All i Dismiss All.
- **Tryb automatycznego jezyka zrodlowego.** Jezyk zrodlowy moze byc automatycznie wykrywany per cue, z NLLB (FLORES-200 spBLEU), porownaniem tierow LLM i heurystykami rodzin jezykowymi. Przelacznik w onboardingu i w ustawieniach jezyka zrodlowego.
- **Konfigurowalne planowanie zadan na nowej stronie Tasks.** Kazde zadanie Hangfire i tlumaczenia ma wlasny przelacznik i wyrazenie cron. Strona Tasks to przemianowana i przeprojektowana strona Schedule, z kartami CardComponent, responsywna siatka 1/2/3, stanami ladowania i pustymi oraz poprawionym sprzataniem SignalR. Stary blok automatyzacji na karcie limits zniknal.
- **Konfigurowalne embedding i wykrywanie jezyka, z nowym UI.** Ustawienia frontendu dla zachowania MKV-embed, wykrywania jezyka nieoznaczonych strumieni oraz limitu powtorzen.
- **Fallback MKV-embed dla dlugich sciezek wyjsciowych.** Jesli sciezka przetlumaczonego napisu przekroczylaby typowe limity systemu plikow (dlugie nazwy plikow anime to klasyczny przypadek), tlumaczenie jest osadzane z powrotem w oryginalnym MKV.
- **Upload workspace przeniesiony pod translations.** Upload Workspace jest teraz dostepny jako zakladka w zakladce Translations, co zmniejsza przeskakiwanie miedzy stronami.
- **Nieskonczone przewijanie na dashboardzie, completed translation compare viewer, rozszerzenia widgetu API-usage.** Ulepszenia jakosci zycia, ktore procentuja przy duzych bibliotekach.
- **Szablony issue na GitHubie** dla bug, feature i setup sa w `.github/ISSUE_TEMPLATE/`. Prosze, uzywaj ich przy otwieraniu issue.

Pelny przewodnik migracji jest w [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300).

---
---

## Czym to jest?

Lingarr on Steroids to fork projektu [Lingarr](https://github.com/lingarr-translate/lingarr). Zachowuje ten sam podstawowy workflow: indeksowanie mediow z Radarr i Sonarr, wykrywanie napisow, tlumaczenie przez wspierane providery i zarzadzanie wszystkim z poziomu web UI.

Ten fork skupia sie na stabilnosci kolejek, obsludze wielu instancji, naprawie napisow i lepszej widocznosci operacyjnej dla wiekszych instalacji.

---

## Co zmienilismy

### Backend i kolejkowanie

| Obszar | Co jest inne w naszym forku |
|--------|------------------------------|
| Wlasny translation worker | Zadania tlumaczen dzialaja przez wlasny `BackgroundService` z konfigurowalna liczba workerow, a nie tylko przez kolejki Hangfire. |
| PostgreSQL jako domyslny wybor | PostgreSQL jest domyslna baza danych. SQLite nadal jest wspierany dla mniejszych instalacji. |
| Model stanow mediow z 11 stanami | Media sledza status tlumaczenia w 11 stanach obejmujacych cykl zycia OCR: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `NoSuitableSubtitles`, `Failed`, `AwaitingSource`, `OcrPending`, `OcrBlocked`. Logika decyzji jest w `MediaStateService`. |
| Wsparcie wielu instancji | Filmy i seriale zapisuja `SourceInstanceId`, dzieki czemu jedna instalacja moze obslugiwac wiele instancji Radarr i Sonarr. |
| Deferred repair | Nieudane linie moga byc ponawiane z otaczajacym kontekstem, co poprawia skutecznosc naprawy. |
| Circuit breaker per provider | Singleton circuit breaker sledzi blady per provider tlumaczen i automatycznie stosuje backoff po przekroczeniu progu. |
| Wznawianie wstrzymanych tlumaczen | Zapytania, ktore trafiaja na 429 (np. limity Gemini), pauzuja sie z zachowanym slotem workera i wznawiaja automatycznie. |
| Post-translation quality gate | Po batchu pozostale akapity sa oceniane z konfigurowalna tolerancja. UI pozwala edytowac lub odrzucac. Domyslnie wlaczone, z przelacznikiem. |

### Przetwarzanie napisow

- FFmpeg potrafi wyciagnac tekstowe napisy z osadzonych sciezek MKV i MP4.
- Czyszczenie ASS/SSA usuwa komendy rysowania, znaczniki muzyczne, placeholdery efektow i URL-e przed tlumaczeniem.
- Rzadkie sciezki z mniej niz 50 liniami dialogow sa pomijane.
- Wykrywanie zewnetrznych napisow znajduje recznie dodane pliki i dalej je sledzi.
- Bitmapowe sciezki napisow (DVD/VobSub, PGS itd.) sa przetwarzane przez OCR, a nastepnie tlumaczone jak kazde inne zrodlo.
- Sprawdzenia integralnosci ASS lapiacy wyciekajace fragmenty tagow, dzieki czemu prompty nie traktuja komend rysowania jak dialogu.
- Dlugie sciezki wyjsciowe, ktore przekroczylyby typowe limity systemu plikow, sa osadzane w oryginalnym MKV zamiast byc zapisywane obok pliku.

### UI i operacje

- Kreator onboardingu prowadzi przez pierwsza konfiguracje Radarr i Sonarr.
- Widgety dashboardu obsluguja drag and drop i live update przez SignalR.
- Widget kolejki zadan i historia tlumaczen daja widocznosc, ktorej upstream obecnie nie dostarcza.
- Widget zuzycia API pokazuje metryki uzycia: liczbe wywolan, tokeny, opoznienia, bledy i success rate.
- Bledy pojawiaja sie w audycie quality gate, gdzie mozna edytowac problematyczny cue inline, a nastepnie zaakceptowac lub odrzucic. Nieudane batche mozna ponownie ustawiac w kolejce lub odrzucac masowo.
- Completed translation compare viewer pozwala porownac tekst zrodlowy i przetlumaczony obok siebie po zakonczeniu.
- Widget historii dashboardu uzywa nieskonczonego przewijania zamiast stronicowania, co ma znaczenie przy duzych bibliotekach.
- Upload Workspace jest teraz zakladka w przeplywie Translations, aby zmniejszyc przeskakiwanie miedzy stronami. Custom Sources pozostaje osobnym elementem w ustawieniach.
- Konfigurowalne planowanie zadan mieszka na nowej stronie Tasks (dawniej Schedule) z przelacznikami per zadanie, wyrazeniami cron, kartami CardComponent, responsywna siatka i jawnymi stanami ladowania i pustymi.
- Klient ma 11 wbudowanych motywow, a nie tylko przelacznik jasny/ciemny.
- UI jest dostepne po angielsku, holendersku, niemiecku, francusku, hiszpansku, polsku i po uproszczonemu chinsku.

### Niezawodnosc

- Czyszczenie osieroconych napisow wykrywa przemianowane media, po ktorych zostaly przetlumaczone pliki.
- Zbiorcze sprawdzanie integralnosci potrafi zweryfikowac napisy w calej bibliotece.
- Ochrona przed ghost jobami zapobiega nadpisywaniu stanow koncowych i sprzata przerwane zadania po restarcie.
- Exponential backoff i opoznione ponowne kolejkowanie zmniejszaja nacisk na niestabilne providery.
- Wznawianie wstrzymanych tlumaczen trzyma sloty workerow przy limitach i wznawia automatycznie.
- Silent token streaming dla providerow AI zmniejsza opoznienie pierwszego tokena przy dlugich tlumaczeniach.
- Wlasne kolejki tlumaczen respektuja priorytet mediow i unikaja head-of-line blockingu, gdy niskopriorytetowe tlumaczenie sie zatrzyma.
- Integracje Chutes, NanoGPT i CrofAI kazda zawiera quota-aware obsluge limitow, logike specyficzna dla providera i UI w tym forku.

---

## Obslugiwane uslugi

To jest lista tego, co dziala w naszym forku dzisiaj. Czesc z tych uslug jest juz tez wspierana przez upstream, wiec nie traktuj tego jako listy rzeczy "tylko u nas".

**Sztuczna inteligencja (AI):**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (ze sledzeniem limitow i auto-pauza)
- [NanoGPT](https://nano-gpt.com/) (z uzyciem subskrypcji, rezerwami i auto-pauza)
- [CrofAI](https://crof.ai/) (tylko kredyty; pauzuje tlumaczenia automatycznie, gdy saldo kredytow spadnie do zera)
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

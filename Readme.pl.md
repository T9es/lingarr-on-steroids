# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Tłumaczenie napisów dla prawdziwych bibliotek Radarr/Sonarr.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [中文](Readme.zh.md)

---

>
> To README opisuje nasz fork i stan upstreamowego Lingarra z 29 czerwca 2026. Jeśli upstream zmieni się później, część szczegółów tutaj może z czasem przestac byc idealnie aktualna.
>
> Aktualizacja z v1.x? Wersja 2.0.0 wprowadza breaking changes. MySQL/MariaDB nie jest już wspierany, ustawienia nie są automatycznie migrowane i wymagany jest czysty start.
>
> Aktualizacja z v2.x do v3.0.0? Sprawdz [CHANGELOG](CHANGELOG.md) z notatkami migracyjnymi. Strona Schedule zmieniła nazwę na Tasks. Kreator onboardingu, konfigurowalne planowanie zadań oraz post-translation quality gate zostały zmienione. CrofAI dołączył do wspieranych providerów AI. Znaczek wersji w lewym dolnym rogu pokazuje teraz prawdziwą wersję dev-build zamiast "Dev Build".

---

## Co nowego w v3.0.0

Wersja v3 to poważna zmiana w stosunku do v2.5.0. Jeśli przeczytasz tylko jedną sekcję, to przeczytaj tę.

- **Wersjonowanie świadome Git.** Wersja assembly jest teraz rozwiązywana z `git describe` w czasie budowania, a build Dockera przekazuje argument `VERSION`. Wydanie v3.0.0 to już tylko otagowanie `v3.0.0` i push taga. `Lingarr.Core.csproj` nie trzeba już ręcznie edytować.
- **Znaczek Dev Build pokazuje prawdziwą wersję.** Znaczek w lewym dolnym rogu pokazuje teraz `Dev <version>` (na przykład `Dev 3.0.0-216-g39ae09b2`) zamiast ogólnego tekstu.
- **CrofAI jest teraz wspieranym providerem AI** z śledzeniem użycia tylko na kredyty. Tłumaczenia pauzuja się automatycznie, gdy saldo kredytów CrofAI spadnie do zera. Zobacz nowe zmienne `CROFAI_*` w [Settings.MD](Settings.MD).
- **OCR dla napisów bitmapowych.** Ścieżki DVD/VobSub, PGS i inne oparte na obrazie są przetwarzane przez OCR, a nastepnie tłumaczone jak kazde inne zrodlo. Dwa nowe stany (`OcrPending`, `OcrBlocked`) obejmuja cykl życia OCR.
- **Circuit breaker per provider.** Gdy provider zaczyna zwracac bledy 5xx, obwod się otwiera i zapytania są pauzowane, zamiast zuzywas twojego limitu API podczas awarii.
- **Wznawianie wstrzymanych tłumaczeń.** 429 od providera (na przykład limity Gemini) nie kończą już tłumaczenia. Worker trzyma slot i wznawia, gdy limit zostanie zniesiony.
- **Post-translation quality gate.** Po zakończeniu batcha pozostale akapity są oceniane. UI pozwala przegladac, edytować, akceptowac lub odrzucac pozycje poza tolerancja, lacznie z akcjami masowymi Requeue All i Dismiss All.
- **Tryb automatycznego języka źródłowego.** Język źródłowy może byc automatycznie wykrywany per cue, z NLLB (FLORES-200 spBLEU), porownaniem tierow LLM i heurystykami rodzin językowymi. Przełącznik w onboardingu i w ustawieniach języka źródłowego.
- **Konfigurowalne planowanie zadań na nowej stronie Tasks.** Kazde zadanie Hangfire i tłumaczenia ma własny przełącznik i wyrażenie cron. Strona Tasks to przemianowana i przeprojektowana strona Schedule, z kartami CardComponent, responsywna siatka 1/2/3, stanami ladowania i pustymi oraz poprawionym sprzataniem SignalR. Stary blok automatyzacji na karcie limits zniknal.
- **Konfigurowalne embedding i wykrywanie języka, z nowym UI.** Ustawienia frontendu dla zachowania MKV-embed, wykrywania języka nieoznaczonych strumieni oraz limitu powtórzeń.
- **Fallback MKV-embed dla dlugich ścieżek wyjsciowych.** Jeśli ścieżka przetłumaczonego napisu przekroczylaby typowe limity systemu plików (dlugie nazwy plików anime to klasyczny przypadek), tłumaczenie jest osadzane z powrotem w oryginalnym MKV.
- **Upload workspace przeniesiony pod translations.** Upload Workspace jest teraz dostepny jako zakladka w zakladce Translations, co zmniejsza przeskakiwanie miedzy stronami.
- **Nieskończone przewijanie na dashboardzie, completed translation compare viewer, rozszerzenia widgetu API-usage.** Ulepszenia jakosci życia, ktore procentuja przy duzych bibliotekach.
- **Szablony issue na GitHubie** dla bug, feature i setup są w `.github/ISSUE_TEMPLATE/`. Prosze, uzywaj ich przy otwieraniu issue.

Pełny przewodnik migracji jest w [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300).

---
---

## Czym to jest?

Lingarr on Steroids to fork projektu [Lingarr](https://github.com/lingarr-translate/lingarr). Zachowuje ten sam podstawowy workflow: indeksowanie mediow z Radarr i Sonarr, wykrywanie napisów, tłumaczenie przez wspierane providery i zarządzanie wszystkim z poziomu web UI.

Ten fork skupia się na stabilności kolejek, obsludze wielu instancji, naprawie napisów i lepszej widoczności operacyjnej dla wiekszych instalacji.

---

## Co zmieniliśmy

### Backend i kolejkowanie

| Obszar | Co jest inne w naszym forku |
|--------|------------------------------|
| Własny translation worker | Zadania tłumaczeń działaja przez własny `BackgroundService` z konfigurowalna liczba workerow, a nie tylko przez kolejki Hangfire. |
| PostgreSQL jako domyślny wybor | PostgreSQL jest domyślna baza danych. SQLite nadal jest wspierany dla mniejszych instalacji. |
| Model stanow mediow z 11 stanami | Media śledza status tłumaczenia w 11 stanach obejmujacych cykl życia OCR: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `NoSuitableSubtitles`, `Failed`, `AwaitingSource`, `OcrPending`, `OcrBlocked`. Logika decyzji jest w `MediaStateService`. |
| Wsparcie wielu instancji | Filmy i seriale zapisują `SourceInstanceId`, dzieki czemu jedna instalacja może obsługiwac wiele instancji Radarr i Sonarr. |
| Deferred repair | Nieudane linie moga byc ponawiane z otaczajacym kontekstem, co poprawia skuteczność naprawy. |
| Circuit breaker per provider | Singleton circuit breaker śledzi blady per provider tłumaczeń i automatycznie stosuje backoff po przekroczeniu progu. |
| Wznawianie wstrzymanych tłumaczeń | Zapytania, ktore trafiaja na 429 (np. limity Gemini), pauzuja się z zachowanym slotem workera i wznawiaja automatycznie. |
| Post-translation quality gate | Po batchu pozostale akapity są oceniane z konfigurowalna tolerancja. UI pozwala edytować lub odrzucac. Domyślnie wlaczone, z przełącznikiem. |

### Przetwarzanie napisów

- FFmpeg potrafi wyciągnac tekstowe napisy z osadzonych ścieżek MKV i MP4.
- Czyszczenie ASS/SSA usuwa komendy rysowania, znaczniki muzyczne, placeholdery efektow i URL-e przed tłumaczeniem.
- Rzadkie ścieżki z mniej niz 50 liniami dialogów są pomijane.
- Wykrywanie zewnetrznych napisów znajduje ręcznie dodane pliki i dalej je śledzi.
- Bitmapowe ścieżki napisów (DVD/VobSub, PGS itd.) są przetwarzane przez OCR, a nastepnie tłumaczone jak kazde inne zrodlo.
- Sprawdzenia integralnosci ASS lapiacy wyciekajace fragmenty tagow, dzieki czemu prompty nie traktuja komend rysowania jak dialogu.
- Dlugie ścieżki wyjsciowe, ktore przekroczylyby typowe limity systemu plikow, są osadzane w oryginalnym MKV zamiast byc zapisywane obok pliku.

### UI i operacje

- Kreator onboardingu prowadzi przez pierwsza konfigurację Radarr i Sonarr.
- Widgety dashboardu obsługuja drag and drop i live update przez SignalR.
- Widget kolejki zadań i historia tłumaczeń daja widoczność, ktorej upstream obecnie nie dostarcza.
- Widget zużycia API pokazuje metryki użycia: liczbe wywolan, tokeny, opóźnienia, bledy i success rate.
- Bledy pojawiaja się w audycie quality gate, gdzie mozna edytować problematyczny cue inline, a nastepnie zaakceptowac lub odrzucic. Nieudane batche mozna ponownie ustawiac w kolejce lub odrzucac masowo.
- Completed translation compare viewer pozwala porownac tekst źródłowy i przetłumaczony obok siebie po zakończeniu.
- Widget historii dashboardu uzywa nieskończonego przewijania zamiast stronicowania, co ma znaczenie przy duzych bibliotekach.
- Upload Workspace jest teraz zakladka w przeplywie Translations, aby zmniejszyc przeskakiwanie miedzy stronami. Custom Sources pozostaje osobnym elementem w ustawieniach.
- Konfigurowalne planowanie zadań mieszka na nowej stronie Tasks (dawniej Schedule) z przełącznikami per zadanie, wyrażeniami cron, kartami CardComponent, responsywna siatka i jawnymi stanami ladowania i pustymi.
- Klient ma 11 wbudowanych motywow, a nie tylko przełącznik jasny/ciemny.
- UI jest dostepne po angielsku, holendersku, niemiecku, francusku, hiszpansku, polsku i po uproszczonemu chinsku.

### Niezawodność

- Czyszczenie osieroconych napisów wykrywa przemianowane media, po ktorych zostały przetłumaczone pliki.
- Zbiorcze sprawdzanie integralnosci potrafi zweryfikować napisy w calej bibliotece.
- Ochrona przed ghost jobami zapobiega nadpisywaniu stanow koncowych i sprzata przerwane zadania po restarcie.
- Exponential backoff i opóźnione ponowne kolejkowanie zmniejszaja nacisk na niestabilne providery.
- Wznawianie wstrzymanych tłumaczeń trzyma sloty workerów przy limitach i wznawia automatycznie.
- Silent token streaming dla providerów AI zmniejsza opóźnienie pierwszego tokena przy dlugich tłumaczeniach.
- Własne kolejki tłumaczeń respektuja priorytet mediow i unikaja head-of-line blockingu, gdy niskopriorytetowe tłumaczenie się zatrzyma.
- Integracje Chutes, NanoGPT i CrofAI kazda zawiera quota-aware obsługe limitow, logike specyficzna dla providera i UI w tym forku.

---

## Obsługiwane usługi

To jest lista tego, co działa w naszym forku dzisiaj. Część z tych usług jest już też wspierana przez upstream, wiec nie traktuj tego jako listy rzeczy "tylko u nas".

**Sztuczna inteligencja (AI):**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (ze śledzeniem limitów i auto-pauza)
- [NanoGPT](https://nano-gpt.com/) (z użyciem subskrypcji, rezerwami i auto-pauza)
- [CrofAI](https://crof.ai/) (tylko kredyty; pauzuje tłumaczenia automatycznie, gdy saldo kredytów spadnie do zera)
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

### Tagi obrazów Docker

| Tag | Opis | Architektury |
|-----|------|---------------|
| `latest` | Najnowsza stabilna wersja | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Konkretna wersja | `linux/amd64`, `linux/arm64` |
| `main` | Wersja rozwojowa | `linux/amd64`, `linux/arm64` |

Polecamy PostgreSQL. SQLite nadaje się do malych instalacji (jeden użytkownik, <1000 pozycji mediow).

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

| Zmienna | Opis | Domyślnie |
|---------|------|-----------|
| `TZ` | Strefa czasowa kontenera | - |
| `ASPNETCORE_URLS` | Adres HTTP do nasluchiwania | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` lub `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | Nazwa pliku SQLite w `/app/config` | `local.db` |
| `DB_HOST` | Host PostgreSQL | - |
| `DB_PORT` | Port PostgreSQL | `5432` |
| `DB_DATABASE` | Nazwa bazy danych | - |
| `DB_USERNAME` | Użytkownik bazy danych | - |
| `DB_PASSWORD` | Haslo bazy danych | - |
| `MAX_PARALLEL_TRANSLATIONS` | Startowa liczba workerów tłumaczeń | `1` |
| `MAX_CONCURRENT_JOBS` | Liczba workerów Hangfire dla kolejek sync i systemowych | `5` |
| `RADARR_URL` | Adres URL Radarr | - |
| `RADARR_API_KEY` | Klucz API Radarr | - |
| `SONARR_URL` | Adres URL Sonarr | - |
| `SONARR_API_KEY` | Klucz API Sonarr | - |

Pełna referencja zmiennych środowiskowych jest w [Settings.MD](Settings.MD).

---

## Autorzy i prawa autorskie

Oryginalny projekt Lingarr autorstwa [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Ikony: [Lucide](https://lucide.dev/icons).  
Parser napisow: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Tłumaczenia: LibreTranslate, biblioteka GTranslate.

---

## Podziękowania

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

# Lingarr on Steroids

<div align="center">

[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker Pulls](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Zaawansowane tlumaczenie napisow dla power-userow**  
*Zbudowane dla niezawodnosci, wydajnosci i kosztowo-efektywnych przeplywow KI*

[Pierwsze kroki](#-pierwsze-kroki) • [Dlaczego ten fork?](#-dlaczego-ten-fork) • [Funkcje](#-funkcje) • [Uslugi tlumaczenia](#-uslugi-tlumaczenia) • [Konfiguracja](#-konfiguracja) • [Multi-Language](Readme.MD) (English, Deutsch, Polski, Francais, Espanol, Nederlands, Chinese)

</div>

---

## Przeglad

**Lingarr on Steroids** to wyspecjalizowany fork [Lingarr](https://github.com/lingarr-translate/lingarr) - przebudowany od podstaw dla niezawodnosci, wydajnosci i kosztowo-efektywnego wykorzystania KI w przeplywach tlumaczenia napisow.

Zbudowany dla uzytkownikow zarzadzajacych duzymi bibliotekami mediiw i oczekujacych stability poziomu korporacyjnego od swoich narzedzi automatyzacji.

### Co otrzymujesz

- Pelna integracja z Radarr & Sonarr
- 12 uslug tlumaczenia (AI + API chmurowe)
- Obsluga wielu instancji dla wielu serwerow *arr
- Dashboard w czasie rzeczywistym z widgetami przeciagnij-i-upusc
- Gleboka ekstrakcja napisow z kontenerow MKV/MP4
- Architektura gotowa do produkcji (PostgreSQL, wspolbiezni pracownicy, wlasciwe sledzenie stanu)

---

## Dlaczego ten fork

Ten fork znacznie odbiega od oryginalnego Lingarr. Nie tylko dodawalismy funkcje - odbudowalismy podstawowa architekture dla srodowisk produkcyjnych, gdzie niezawodnosc jest wazna.

### Architektura

| Oryginalny Lingarr | Lingarr on Steroids |
|--------------------|---------------------|
| Hangfire dla wszystkich zadan | Niestandardowy BackgroundService dla tlumaczen |
| Domyslnie SQLite | Domyslnie PostgreSQL z MVCC |
| Podstawowe sledzenie zadan | 9-stanowy system TranslationState |
| Tylko jedna instancja | Multi-instance (wiele serwerow *arr) |
| Ograniczona ekstrakcja napisow | gleboka ekstrakcja oparta na FFmpeg |
| Brak logiki ponawiania partii | Deferred Contextual Repair |

### Co zbudowalismy

**Niestandardowy worker tlumaczenia**  
Zastapilismy Hangfire dedykowanym usluga opartym na bazie danych, ktora obsluguje 1-20 wspolbieznych pracownikow z kolejkowanie priorytetowym. Zadania przetrwaja restarty. Pracownicy automatycznie regeneruja sie po awariach.

```csharp
// Obsluguje 1-20 wspolbieznych tlumaczen na instancje
// Kolejka priorytetowa: przesun media na sam poczatek
// Wspolpracujace anulowanie: zatrzymuj zadania posrodku czysto
```

**System stanow oparty na bazie danych**  
Dziewiec stanow tlumaczenia sledzi kazdy element mediow: Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted. Zapytania jak "co wymaga tlumaczenia" dzialaja wydajnie. Wykrywanie stalych wywoluje ponowne skany gdy ustawienia sie zmieniaja.

**PostgreSQL First**  
SQLite dziala dla prostych konfiguracji. PostgreSQL jest domyslny - MVCC eliminuje blokade podczas ciezkiej pracy wspolbieznej. Obsluga MySQL/MariaDB zostala usuneta (powodowala zbyt wiele problemow).

### Przetwarzanie napisow

**Gleboka ekstrakcja osadzonych**  
FFmpeg przeszukuje kontenery MKV/MP4 i wyodrzbnia napisy SRT, ASS i MOV_TEXT. Laczenie warstw inteligentnie laczy sciezki Forced/CC/SDH. Pliki pozostaja na dysku - ekstrakcja odbywa sie na zadanie.

**Czyszczenie ASS/SSA**  
Grafika wektorowa (bloki rysunkowe), symbole muzyczne, efekty dzwiekowe, adresy URL i linie creditow sa filtrowane. Tylko tlumaczalny dialog trafia do twojej AI. Rezultat: czystsze tlumaczenia, mniej niepowodzen.

```json
// Filtrowanie usuwa:
// Polecenia rysunkowe: {\p1}...{\p0}
// Symbole muzyczne: ♪ ♫ ♬
// Efekty dzwiekowe: [Drzwi trzaskaja]
// URL i linie creditow
```

**Wykrywanie rzadkich sciozek**  
Sciezki z mniej niz 100 wpisami (tylko znaki, tylko piosenki) sa automatycznie pomijane. Koniec z marnowaniem limitow na nierozmowne sciezki.

### Inteligentne tlumaczenie

**Opozniona naprawa kontekstowa**  
Nieudane linie gromadza sie podczas partii. Na koncu sa ponawiane z 10 liniami otaczajacego kontekstu (konfigurowalne). LLM tlumacza lepiej z kontekstem - wspolczynniki odzysku dramatycznie sie poprawiaja.

**Tlumaczenie partiowe**  
OpenAI, Anthropic, DeepSeek, Gemini, Chutes.ai i LocalAI obsuguja wywolania partiowe. Wysylaj 50-100 linii na request API. Wrapper kontekstowy dodaje otaczajace linie w razie potrzeby. Oszczedzasz pieniadze. Tlumaczenia sa dokladniejsze.

**Pelna integracja z Chutes.ai**  
Sledzenie uzycia w czasie rzeczywistym. Egzekwowanie limitow z buforami. Automatyczne pauzowanie gdy limity zostana osiagniete. Obsluga 402 PaymentRequired. Wszystko zautomatyzowane - nie musisz sprawdzac dashboardow limitow.

### Kontrola przeplywu pracy

- **Kolejka priorytetowa**: przesun media na poczatek przez flage lub UI
- **Przetasowanie w czasie rzeczywistym**: zmiana priorytetu show -> odcinki natychmiast sie przesuwaja
- **Wspolpracujace anulowanie**: czyste anulowanie zadan w trakcie
- **Panel testowy na zywo**: suche uruchomienia tlumaczen z logami SSE w czasie rzeczywistym
- **Wybor Cron**: rozwijane menu dla wzorcow 15min / 30min / godzinne / dzienne
- **Deduplikacja**: ograniczenia bazy danych zapobiegaja duplikatom zadan

### Funkcje niezawodnosci

**Czyszczenie osieroconych napisow**  
Gdy Radarr/Sonarr aktualizuje media (zmieniaja sie nazwy plikow), pliki tlumaczone przez AI staja sie osierocone. Wykrywamy to i sprzatamy. Logi audytu pokazuja co zostalo usnieto.

**Buletowe sprawdzanie integralnosci**  
Sprawdz kazde tlumaczenie w swojej bibliotece. Postep w czasie rzeczywistym przez SignalR. Wykryj uszkodzone lub niekompletne tlumaczenia zanim wystapi problem z odtwarzaniem.

**Sledzenie ponawiania**  
Exponential backoff z jitter. Automatyczne zalamanie zadan przy starcie. Wykrywanie zadan-duchow usuwa zawieszone wpisy.

---

## Funkcje

### Dashboard i monitorowanie
- **Dashboard w czasie rzeczywistym**: System widgetow w stylu TrueNAS z ukladem przeciagnij-i-upusc
- **Aktualizacje SignalR**: zyc postep, aktywne tlumaczenia, kolejka zadan - bez odswiezania strony
- **Widget przegladu medii**: Status tlumaczenia odcinek po odcinku dla kazdego serialu
- **Widget historii tlumaczen**: Wykres + lista z podzialem sukces/niepowodzenie
- **Widget kolejki zadan**: Uruchomione zadania, zaplanowane zadania, nieudane zadania - wszystko w jednym
- **Widget uzycia API**: Wydatki na kazda usluge z wykresami sparkline
- **Przegladaj logi bledow**: Filtrowalne, przeszukiwalne logi z ochrona XSS

### Obsluga wielu instancji
- Podlacz wiele instancji Radarr/Sonarr
- Kazda instancja ma wlasna kolejke i ustawienia
- Wykrywanie duplikatow pomiedzy instancjami
- Jednolity interfejs z przejsciem pomiedzy instancjami
- Narzedzie migracji dla istniejacych konfiguracji z jedna instancja

### Jakosc zycia
- **Kreator pierwszej konfiguracji**: Konfiguracja z przewodnikiem Radarr/Sonarr
- **Obsluga motywow**: Ciemny/Jasny z zmiennymi CSS - zintegruj ze swoim ustawieniem
- **Wybor jezyka**: 7 jezykow (EN, NL, DE, FR, ES, PL, ZH)
- **Wykrywanie offline**: Wskaznik gdy aplikacja jest nieosiagalna
- **Operacje masowe**: Ponow dodaj wszystkie nieudane, usun wszystkie ukonczone, sprawdz integralnosc wszystkich

---

## Uslugi tlumaczenia

Lingarr obsluguje wiele uslug tlumaczenia, aby odpowiadac Twoim potrzebom, budzetowi i wymaganiom dotyczacym prywatnosci:

**Tlumaczenie AI**
- **[OpenAI](https://openai.com/)** - Modele GPT z obsluga tlumaczenia partiowego
- **[Anthropic](https://www.anthropic.com/)** - Modele Claude z obsluga tlumaczenia partiowego
- **[Google Gemini](https://gemini.google.com/)** - Modele Google AI z obsluga partiowa
- **[DeepSeek](https://deepseek.com)** - Ekonomiczne AI z obsluga tlumaczenia partiowego
- **[Chutes.ai](https://chutes.ai)** - Modele open-source ze sledzeniem uzycia i zarzadzaniem limitami
- **LocalAI / Ollama** - Modele samodzielnie hostowane (kompatybilne z Ollama) z obsluga partiowa

**API tlumaczenia chmurowego**
- **[LibreTranslate](https://libretranslate.com)** - Samodzielnie hostowane lub chmurowe tlumaczenie
- **[DeepL](https://www.deepl.com/)** - Profesjonalne API tlumaczenia
- **[Google Translate](https://translate.google.com/)** - Poprzez biblioteke GTranslate
- **[Bing Translate](https://www.bing.com/translator)** - Poprzez biblioteke GTranslate
- **[Yandex Translate](https://translate.yandex.com/)** - Poprzez biblioteke GTranslate
- **[Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)** - Poprzez biblioteke GTranslate

---

## Pierwsze kroki

### Tagi obrazow Docker

Lingarr udostepnia obrazy Docker dla wielu architektur:

| Tag | Opis | Architektury |
|-----|------|---------------|
| `latest` | Najnowsze stabilne wydanie | `linux/amd64`, `linux/arm64` |
| `1.2.3` | okreslona wersja | `linux/amd64`, `linux/arm64` |
| `main` | Build rozwojowy | `linux/amd64`, `linux/arm64` |

> **Uwaga:** Wszystkie obrazy obsluguja zarowno AMD64 (Intel/AMD) jak i ARM64 (Raspberry Pi, Apple Silicon).

### Szybki start

> [!WARNING]
> **Aktualizacja z v1.x?** Wersja 2.0.0 wprowadza zmiany laczace:
> - **Obsluga MySQL/MariaDB zostala usunieta.** Migruj do PostgreSQL (zalecane) lub SQLite.
> - **Ustawienia NIE sa migrowane.** Przekonfiguruj po aktualizacji (~5 minut).
> - **Biblioteka medii automatycznie** synchronizuje sie z Radarr/Sonarr - zadna akcja nie wymagana.
> - **Poprzednie bazy danych nie moga byc migrowane**; to swiezy start.

**Zalecane:** PostgreSQL to zalecana baza danych dla tego forka. Uzywa MVCC (Multi-Version Concurrency Control), ktora eliminuje problemy z blokowaniem podczas ciezkiej pracy wspolbieznej.

<details>
<summary><b>Konfiguracja PostgreSQL (zalecana)</b></summary>

```yaml
version: "3.8"

networks:
  lingarr:

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Warsaw # Zastap swoja strefa czasowa
      - DB_CONNECTION=postgresql
      - DB_HOST=lingarr-postgres
      - DB_PORT=5432
      - DB_DATABASE=lingarr
      - DB_USERNAME=lingarr
      - DB_PASSWORD=CHANGE_ME_SECURE_PASSWORD # ZMIEN TO
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
      POSTGRES_PASSWORD: CHANGE_ME_SECURE_PASSWORD # ZMIEN TO (Musi sie zgadzac)
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
<summary><b>Konfiguracja SQLite (prosta)</b></summary>

Dla prostych konfiguracji lub testow, uzyj SQLite, ktory nie wymaga dodatkowych kontenerow:

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Warsaw # Zastap swoja strefa czasowa
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
<summary><b>Konfiguracja Docker CLI</b></summary>

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

## Konfiguracja

### Zmienne srodowiskowe

| Zmienna | Opis | Domyslna |
|---------|------|----------|
| `ASPNETCORE_URLS` | Wewnetrzny port na ktorym nasluchuje Lingarr | `http://+:9876` |
| `MAX_CONCURRENT_JOBS` | Rozmiar puli workerow Hangfire dla zadan sync | `20` |
| `DB_CONNECTION` | Typ bazy danych: `postgresql` lub `sqlite` | `postgresql` |
| `DB_HOST` | Nazwa hosta PostgreSQL (wymagane dla PostgreSQL) | - |
| `DB_PORT` | Port PostgreSQL (wymagane dla PostgreSQL) | `5432` |
| `DB_DATABASE` | Nazwa bazy danych (wymagane dla PostgreSQL) | - |
| `DB_USERNAME` | Nazwa uzytkownika bazy danych (wymagane dla PostgreSQL) | - |
| `DB_PASSWORD` | Haslo bazy danych (wymagane dla PostgreSQL) | - |
| `DB_HANGFIRE_SQLITE_PATH` | Ścieżka SQLite dla Hangfire (tylko SQLite) | `/app/config/Hangfire.db` |
| `HANGFIRE_USERNAME` | Nazwa uzytkownika panelu Hangfire | `admin` |
| `HANGFIRE_PASSWORD` | Haslo panelu Hangfire | Losowe (drukowane przy starcie) |

Dodatkowe ustawienia mozna skonfigurowac jako zmienne srodowiskowe, aby przetrwaly reinstalacje. Zobacz [Settings.MD](Settings.MD) dla pelnej listy.

### Konfiguracja LibreTranslate

Opcjonalnie jesli uzywasz innej uslugi tlumaczenia.

<details>
<summary><b>Docker Compose</b></summary>

```yaml
  libretranslate:
    container_name: libretranslate
    image: libretranslate/libretranslate:latest
    restart: unless-stopped
    environment:
      - LT_LOAD_ONLY=en,pl  # Zastap swoimi preferowanymi jezykami
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
  --load-only=en,pl     # Zastap swoimi preferowanymi jezykami
```

</details>

**Zmienne srodowiskowe LibreTranslate:**

| Zmienna | Opis |
|---------|------|
| `LT_LOAD_ONLY` | Jezyki zrodlowe wedlug [kodu ISO](https://libretranslate.com/languages) |
| `LT_DISABLE_WEB_UI` | Wylacza interfejs webowy (ustaw na dowolna wartosc) |

---

## Integracja API

Lingarr udostepnia RESTful API do integrowania mozliwosci tlumaczenia napisow w Twoich aplikacjach. Pelna dokumentacja API z definicjami Swagger jest dostepna pod:

[Dokumentacja API Lingarr](https://lingarr.com/docs/api/)

---

## Wnoszenie wkadu

Milescimy wkady! Czy to zgloszenia bledow, prośby o funkcje lub wkady kodu, zachecamy do pomocy.

Odwiedz repozytorium [Lingarr on Steroids](https://github.com/T9es/lingarr-on-steroids) na GitHubie, aby zaczac.

---

## Zaslugi

Ten projekt opiera sie na fundamentach oryginalnego projektu [Lingarr](https://github.com/lingarr-translate/lingarr) autorstwa rowanfuchs.

- Ikony: [Lucide](https://lucide.dev/icons)
- Parsowanie napisow: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser)
- Uslugi tlumaczenia: [LibreTranslate](https://libretranslate.com)
- GTranslate: [GTranslate](https://github.com/d4n3436/GTranslate)

---

## Specjalne podziekowania

Za wsparcie dla open source:
- [selfh.st by Ethan](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)
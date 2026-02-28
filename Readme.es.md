# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Subtitulado que realmente funciona** - para gente que gestiona bibliotecas de medios a gran escala.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Actualizando desde v1.x?** La version 2.0.0 tiene cambios importantes - MySQL/MariaDB eliminado, la configuracion NO se migra, se requiere inicio limpio. Ver abajo para detalles.

---

## Que es esto?

Lingarr on Steroids es un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Mantuvimos la idea central (traducir subtitulos via Radarr/Sonarr) pero reconstruimos la mayoria del backend y añadimos muchas mejoras de UI.

Empezo porque el Lingarr original tuvo problemas de fiabilidad bajo carga. Necesitabamos algo que no se cayera cuando tienes miles de programas.

---

## Que cambiamos

### Backend

| Que | Por qué |
|------|-----|
| Worker de traduccion personalizado | Hangfire se ahogaba con colas grandes. Escribimos nuestro propio BackgroundService que maneja 1-20 workers paralelos, colas de prioridad, y auto-recuperacion de crashes. |
| PostgreSQL por defecto | SQLite se bloquea con workers concurrentes. El MVCC en PostgreSQL realmente funciona. Mantuvimos SQLite como opcion para setups pequenos. |
| Seguimiento de traduccion de 9 estados | El original no teniabuena forma de responder "que necesita traduccir?". Añadimos estados (Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted) para que las consultas sean rapidas. |
| Soporte multi-instancia | Una instancia de Radarr/Sonarr no es suficiente para algunos. Ahora puedes conectar multiples servidores *arr a un Lingarr. |
| Reparacion diferida | Las lineas fallidas se reintentan con contexto circundante (10 lineas por defecto). La calidad de traduccion LLM mejora significativamente cuando la IA puede ver que pasa antes/despues. |

### Procesamiento de subtitulos

- **Extraccion FFmpeg** - extrae subtitulos de contenedores MKV/MP4 cuando estan embedidos
- **Limpieza ASS/SSA** - elimina comandos de dibujo, simbolos musicales, placeholders de efectos de sonido, URLs
- **Filtro de tracks escasos** - salta tracks con <100 entradas (simbolos, canciones)
- **Descubrimiento de subtitulos externos** - encuentra archivos de subtitulos que anyades manualmente y los rastrea

### UI/UX

- **Widgets de dashboard** - layout drag-and-drop, actualizaciones en tiempo real via SignalR
- **Widget de cola** - muestra que esta corriendo, que esta programado, que fallo
- **Historial de traduccion** - grafico + lista mostrando que se tradujo cuando
- **Rastreador de uso API** - graficos sparkline mostrando gasto por servicio
- **Wizard de onboarding** - primer ajuste te guia a traves de la config de Radarr/Sonarr
- **Soporte de temas** - oscuro/claro con variables CSS para que coincida con tu setup
- **7 idiomas** - EN, NL, DE, FR, ES, PL, ZH
- **Deteccion offline** - muestra cuando la app es inalcanzable

### Fiabilidad

- **Limpieza de huerfanos** - detecta cuando una actualizacion renombra el archivo y tus traducciones AI ahora son huerfanas
- **Chequeo de integridad bulk** - valida cada traduccion en tu biblioteca
- **Limpieza de trabajos fantasma** - elimina trabajos atascados que nunca terminaron
- **Backoff exponencial** - reintenta con jitter para no golpear APIs fallidas

---

## Servicios soportados

**IA:**
- OpenAI (GPT)
- Anthropic (Claude)
- Google Gemini
- DeepSeek
- Chutes.ai (con seguimiento de cuota y pausa auto)
- LocalAI / Ollama (autoalojado)

**APIs en la nube:**
- LibreTranslate
- DeepL
- Google Translate
- Bing Translate
- Yandex Translate
- Azure Translator

---

## Empezando

### Tags de imagen Docker

| Tag | Descripcion | Arquitecturas |
|-----|-------------|---------------|
| `latest` | Ultimo lanzamiento estable | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Version especifica | `linux/amd64`, `linux/arm64` |
| `main` | Build de desarrollo | `linux/amd64`, `linux/arm64` |

> **Nota:** Todas las imagenes soportan tanto AMD64 (Intel/AMD) como ARM64 (Raspberry Pi, Apple Silicon).

PostgreSQL es recomendado. SQLite funciona para setups pequenos (usuario unico, <1000 items de medios).

### PostgreSQL (recomendado)

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

### SQLite (inicio rapido)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    environment:
      - TZ=Your/Timezone
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

## Configuracion

| Variable | Descripcion | Por defecto |
|----------|-------------|------------|
| `ASPNETCORE_URLS` | Puerto | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` o `sqlite` | `postgresql` |
| `DB_HOST` | Host de PostgreSQL | - |
| `DB_PORT` | Puerto de PostgreSQL | `5432` |
| `DB_DATABASE` | Nombre de la base de datos | - |
| `DB_USERNAME` | Usuario de la BD | - |
| `DB_PASSWORD` | Contrasena de la BD | - |
| `RADARR_URL` | Tu URL de Radarr | - |
| `RADARR_API_KEY` | Clave API de Radarr | - |
| `SONARR_URL` | Tu URL de Sonarr | - |
| `SONARR_API_KEY` | Clave API de Sonarr | - |

Lista completa en [Settings.MD](Settings.MD).

---

## Creditos

Lingarr original por [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Iconos: [Lucide](https://lucide.dev/icons).  
Parsing de subtitulos: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Traduccion: LibreTranslate, biblioteca GTranslate.

---

## Agradecimientos

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)
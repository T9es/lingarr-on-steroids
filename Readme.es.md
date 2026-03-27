# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Traduccion de subtitulos para bibliotecas Radarr/Sonarr reales.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Snapshot verificado contra `lingarr-translate/lingarr` el 27 de marzo de 2026. Upstream puede cambiar despues de esa fecha.
>
> Actualizando desde v1.x? La version 2.0.0 introduce breaking changes. MySQL/MariaDB ya no esta soportado, la configuracion no se migra automaticamente y hace falta un inicio limpio.

---

## Que es esto?

Lingarr on Steroids es un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Mantiene el flujo principal: indexar medios desde Radarr y Sonarr, encontrar pistas de subtitulos, traducirlas con proveedores soportados y gestionarlo todo desde una interfaz web.

Este fork se centra en colas mas fiables, bibliotecas con varias instancias, reparacion de subtitulos y mejor visibilidad operativa para instalaciones grandes.

---

## Diferencias verificadas de este fork

### Backend y colas

| Area | Diferencia verificada en este fork |
|------|-----------------------------------|
| Translation worker propio | Los trabajos de traduccion pasan por un `BackgroundService` propio con workers paralelos configurables y no solo por colas Hangfire. |
| PostgreSQL por defecto | PostgreSQL es la base de datos por defecto. SQLite sigue disponible para instalaciones pequenas. |
| Modelo de estados de medios | Los medios usan 9 estados: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `AwaitingSource`, `NoSuitableSubtitles`, `Failed`. |
| Soporte multi-instancia | Peliculas y series guardan `SourceInstanceId`, lo que permite conectar varias instancias de Radarr y Sonarr a una sola instalacion. |
| Deferred repair | Las lineas fallidas pueden reintentarse con contexto alrededor, lo que hace mas robustas las pasadas de reparacion. |

### Procesamiento de subtitulos

- FFmpeg puede extraer subtitulos de texto desde pistas incrustadas en contenedores MKV y MP4.
- La limpieza ASS/SSA elimina comandos de dibujo, marcadores musicales, efectos placeholder y URLs antes de traducir.
- Las pistas escasas con menos de 50 lineas de dialogo se omiten.
- El descubrimiento de subtitulos externos detecta y sigue los archivos que anades manualmente.

### UI y operacion

- El asistente de onboarding guia la configuracion inicial de Radarr y Sonarr.
- Los widgets del dashboard soportan drag and drop y actualizaciones en tiempo real via SignalR.
- Los widgets de cola de trabajos e historial de traduccion aportan visibilidad que upstream todavia no incluye.
- El widget de uso de API muestra llamadas, tokens, latencia, errores y tasa de exito.
- El cliente incluye 11 temas integrados, no solo un interruptor claro/oscuro.
- La UI esta traducida a ingles, neerlandes, aleman, frances, espanol, polaco y chino simplificado.

### Fiabilidad

- La limpieza de subtitulos huerfanos detecta medios renombrados que dejaron detras archivos traducidos.
- Los bulk integrity checks pueden validar subtitulos traducidos en toda la biblioteca.
- La proteccion contra ghost jobs evita sobrescribir estados terminales y limpia trabajo interrumpido tras reinicios.
- El exponential backoff y el requeue diferido reducen la presion sobre proveedores inestables.
- La integracion con Chutes incluye gestion de cuotas y logica especifica del proveedor en este fork.

---

## Servicios soportados

Esta es la lista de compatibilidad de este fork en la fecha del snapshot. Parte de estos servicios tambien estan presentes en upstream, asi que no es una reclamacion exclusiva del fork.

**IA:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (con seguimiento de cuota y pausa automatica)
- LocalAI / Ollama (autoalojado)

**APIs en la nube:**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## Empezando

### Etiquetas de imagen Docker

| Etiqueta | Descripcion | Arquitecturas |
|----------|-------------|---------------|
| `latest` | Ultima version estable | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Version especifica | `linux/amd64`, `linux/arm64` |
| `main` | Build de desarrollo | `linux/amd64`, `linux/arm64` |

Se recomienda PostgreSQL. SQLite sirve para instalaciones pequenas (un usuario, <1000 elementos multimedia).

> Nota: todas las imagenes soportan AMD64 (Intel/AMD) y ARM64 (Raspberry Pi, Apple Silicon).

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

## Configuracion

| Variable | Descripcion | Por defecto |
|----------|-------------|-------------|
| `TZ` | Zona horaria del contenedor | - |
| `ASPNETCORE_URLS` | Direccion HTTP de escucha | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` o `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | Nombre del archivo SQLite dentro de `/app/config` | `local.db` |
| `DB_HOST` | Host de PostgreSQL | - |
| `DB_PORT` | Puerto de PostgreSQL | `5432` |
| `DB_DATABASE` | Nombre de la base de datos | - |
| `DB_USERNAME` | Usuario de la BD | - |
| `DB_PASSWORD` | Contrasena de la BD | - |
| `MAX_PARALLEL_TRANSLATIONS` | Valor inicial para los workers de traduccion personalizados | `1` |
| `MAX_CONCURRENT_JOBS` | Numero de workers Hangfire para colas de sync y sistema | `5` |
| `RADARR_URL` | Tu URL de Radarr | - |
| `RADARR_API_KEY` | Clave API de Radarr | - |
| `SONARR_URL` | Tu URL de Sonarr | - |
| `SONARR_API_KEY` | Clave API de Sonarr | - |

La referencia completa de variables de entorno esta en [Settings.MD](Settings.MD).

---

## Creditos

Lingarr original por [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Iconos: [Lucide](https://lucide.dev/icons).  
Analisis de subtitulos: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Traduccion: LibreTranslate, biblioteca GTranslate.

---

## Agradecimientos

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

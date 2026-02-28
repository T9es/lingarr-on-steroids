# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Traducción de subtítulos que realmente funciona** - para quienes gestionan bibliotecas de medios a gran escala.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **¿Actualizando desde v1.x?** La versión 2.0.0 tiene cambios importantes - se ha eliminado MySQL/MariaDB, la configuración NO se migra, se requiere una instalación limpia. Ver abajo para más detalles.

---

## ¿Qué es esto?

Lingarr on Steroids es un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Mantuvimos la idea central (traducir subtítulos vía Radarr/Sonarr) pero reconstruimos la mayoría del backend y añadimos muchas mejoras a la interfaz de usuario (UI).

Empezó porque el Lingarr original tenía problemas de fiabilidad bajo carga. Necesitábamos algo que no se cayera cuando tienes miles de series o películas.

---

## ¿Qué cambiamos?

### Backend

| Qué | Por qué |
|------|-----|
| Worker de traducción personalizado | Hangfire se ahogaba con colas grandes. Escribimos nuestro propio BackgroundService que maneja de 1 a 20 procesos paralelos, colas de prioridad y recuperación automática ante fallos. |
| PostgreSQL por defecto | SQLite se bloquea con procesos concurrentes. El MVCC en PostgreSQL realmente funciona. Mantuvimos SQLite como opción para configuraciones pequeñas. |
| Seguimiento de traducción de 9 estados | El original no tenía una buena forma de responder "¿Qué necesita traducción?". Añadimos estados (Desconocido, Pendiente, En Progreso, Completado, Obsoleto, Esperando Fuente, Sin subtítulos adecuados, Fallido, Interrumpido) para que las consultas sean rápidas. |
| Soporte multi-instancia | Una instancia de Radarr/Sonarr no es suficiente para algunos. Ahora puedes conectar múltiples servidores *arr a un solo Lingarr. |
| Reparación diferida | Las líneas fallidas se reintentan con el contexto circundante (10 líneas por defecto). La calidad de la traducción LLM mejora significativamente cuando la IA puede ver qué pasa antes y después. |

### Procesamiento de subtítulos

- **Extracción FFmpeg** - extrae subtítulos de contenedores MKV/MP4 cuando están incrustados
- **Limpieza ASS/SSA** - elimina comandos de dibujo, símbolos musicales, etiquetas de efectos de sonido y URLs
- **Filtro de pistas escasas** - omite pistas con <100 líneas (símbolos, canciones)
- **Descubrimiento de subtítulos externos** - encuentra archivos de subtítulos que añades manualmente y los rastrea

### UI/UX

- **Widgets del panel de control** - diseño de arrastrar y soltar, actualizaciones en tiempo real vía SignalR
- **Widget de cola de tareas** - muestra qué está en ejecución, qué está programado y qué falló
- **Historial de traducción** - gráfico + lista mostrando qué se tradujo y cuándo
- **Rastreador de uso de API** - minigráficos (sparklines) mostrando el gasto por servicio
- **Asistente de configuración** - el ajuste inicial te guía a través de la configuración de Radarr/Sonarr
- **Soporte de temas** - oscuro/claro con variables CSS para que coincida con tu configuración
- **7 idiomas** - EN, NL, DE, FR, ES, PL, ZH
- **Detección offline** - muestra cuándo la app está inalcanzable (desconectada)

### Fiabilidad

- **Limpieza de archivos huérfanos** - detecta cuando una actualización renombra el archivo y tus traducciones IA quedan huérfanas
- **Comprobación de integridad masiva** - valida cada traducción en tu biblioteca
- **Limpieza de tareas fantasma** - elimina tareas atascadas que nunca terminaron
- **Retroceso exponencial (Backoff)** - reintenta con retraso (jitter) para no saturar las APIs fallidas

---

## Servicios soportados

**IA:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (con seguimiento de cuota y pausa automática)
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

| Etiqueta | Descripción | Arquitecturas |
|-----|-------------|---------------|
| `latest` | Última versión estable | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Versión específica | `linux/amd64`, `linux/arm64` |
| `main` | Versión de desarrollo | `linux/amd64`, `linux/arm64` |

Se recomienda PostgreSQL. SQLite funciona para configuraciones pequeñas (usuario único, <1000 elementos multimedia).

> **Nota:** Todas las imágenes soportan tanto AMD64 (Intel/AMD) como ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (recomendado)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Madrid
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

### SQLite (inicio rápido)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    environment:
      - TZ=Europe/Madrid
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

## Configuración

| Variable | Descripción | Por defecto |
|----------|-------------|------------|
| `ASPNETCORE_URLS` | Puerto | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` o `sqlite` | `postgresql` |
| `DB_HOST` | Host de PostgreSQL | - |
| `DB_PORT` | Puerto de PostgreSQL | `5432` |
| `DB_DATABASE` | Nombre de la base de datos | - |
| `DB_USERNAME` | Usuario de la BD | - |
| `DB_PASSWORD` | Contraseña de la BD | - |
| `RADARR_URL` | Tu URL de Radarr | - |
| `RADARR_API_KEY` | Clave API de Radarr | - |
| `SONARR_URL` | Tu URL de Sonarr | - |
| `SONARR_API_KEY` | Clave API de Sonarr | - |

Lista completa de configuraciones en [Settings.MD](Settings.MD).

---

## Créditos

Lingarr original por [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Iconos: [Lucide](https://lucide.dev/icons).  
Análisis de subtítulos: [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Traducción: LibreTranslate, biblioteca GTranslate.

---

## Agradecimientos

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

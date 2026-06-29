# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Traduccion de subtitulos para bibliotecas Radarr/Sonarr reales.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Este README describe nuestro fork y el estado de Lingarr upstream a fecha del 27 de marzo de 2026. Si upstream cambia despues, algunos detalles aqui pueden quedarse un poco desactualizados con el tiempo.
>
> Este README describe nuestro fork y el estado de Lingarr upstream a fecha del 29 de junio de 2026. Si upstream cambia despues, algunos detalles aqui pueden quedarse un poco desactualizados con el tiempo.
>
> Actualizando desde v1.x? La version 2.0.0 introduce breaking changes. MySQL/MariaDB ya no esta soportado, la configuracion no se migra automaticamente y hace falta un inicio limpio.
>
> Actualizando desde v2.x a v3.0.0? Consulta el [CHANGELOG](CHANGELOG.md) para las notas de migracion. La pagina de Schedule pasa a llamarse Tasks. El asistente de onboarding, el programador de trabajos configurable y el post-translation quality gate han cambiado. CrofAI se ha anadido como proveedor de IA. La insignia de version de la esquina inferior izquierda ahora muestra la version real del dev-build en lugar de "Dev Build".

---

## Que hay de nuevo en v3.0.0

La version v3 trae cambios importantes respecto a v2.5.0. Si solo vas a leer una seccion, lee esta.

- **Versionado consciente de Git.** La version del assembly se resuelve desde `git describe` en tiempo de compilacion y el build de Docker reenvia un argumento `VERSION`. Para sacar un release basta con etiquetar `v3.0.0` y hacer push de la etiqueta. Ya no hace falta editar `Lingarr.Core.csproj` a mano.
- **La insignia Dev Build muestra la version real.** La insignia de la esquina inferior izquierda ahora lee `Dev <version>` (por ejemplo, `Dev 3.0.0-216-g39ae09b2`) en lugar del texto generico anterior.
- **CrofAI ya es un proveedor de IA soportado** con seguimiento de uso solo por creditos. Las traducciones se pausan automaticamente al agotar el saldo. Mira las nuevas variables `CROFAI_*` en [Settings.MD](Settings.MD).
- **OCR para subtitulos bitmap.** DVD/VobSub, PGS y otras pistas de imagen se pasan por OCR y luego se traducen como cualquier otra fuente. Dos nuevos estados (`OcrPending`, `OcrBlocked`) cubren el ciclo de vida OCR.
- **Circuit breaker por proveedor.** Si un proveedor empieza a fallar con 5xx, el circuito se abre y las peticiones se pausan, en vez de gastar tu cuota de API durante la caida.
- **Reanudacion de traducciones pausadas.** Los 429 del proveedor (por ejemplo, limites de Gemini) ya no matan la traduccion. El worker retiene el slot y continua cuando se levanta el limite.
- **Post-translation quality gate.** Tras cerrar un lote, los parrafos supervivientes se puntuan. La UI permite revisar, editar, aceptar o rechazar los que queden fuera de tolerancia, con acciones masivas de Requeue All y Dismiss All.
- **Modo de idioma origen automatico.** El idioma origen se detecta por cue usando NLLB (FLORES-200 spBLEU), comparativa de LLM tier y heuristicas de familia linguistica. Interruptor en onboarding y en los ajustes de idioma origen.
- **Programador de trabajos configurable en la nueva pagina Tasks.** Cada trabajo de Hangfire y de traduccion tiene su propio interruptor y una expresion cron. La pagina Tasks sustituye a la antigua Schedule, con componentes CardComponent compartidos, una rejilla 1/2/3 responsive, estados de carga y vacio, y limpieza de SignalR corregida. El bloque antiguo de automatizacion de la tarjeta limits se ha eliminado.
- **Embedding y deteccion de idioma configurables, con nueva UI.** Ajustes de frontend para comportamiento de embedding MKV, deteccion de idioma en streams sin etiqueta y limite de reintentos por peticion.
- **Fallback de embedding MKV para rutas largas.** Si la ruta del subtitulo traducido excede limites habituales del sistema de archivos (los nombres de archivo de anime son los casos tipicos), la traduccion se embebe en el MKV original.
- **Upload workspace movido bajo translations.** Upload Workspace vive ahora como pestana dentro de la pagina Translations, reduciendo saltos entre paginas.
- **Scroll infinito en el dashboard, completed translation compare viewer, mejoras del widget de uso de API.** Mejoras de calidad de vida para bibliotecas grandes.
- **Plantillas de issues de GitHub** para bug, feature y setup viven en `.github/ISSUE_TEMPLATE/`. Usalas al abrir issues.

La guia completa de migracion esta en [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300).

---
---

## Que es esto?

Lingarr on Steroids es un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Mantiene el flujo principal: indexar medios desde Radarr y Sonarr, encontrar pistas de subtitulos, traducirlas con proveedores soportados y gestionarlo todo desde una interfaz web.

Este fork se centra en colas mas fiables, bibliotecas con varias instancias, reparacion de subtitulos y mejor visibilidad operativa para instalaciones grandes.

---

## Lo que cambiamos

### Backend y colas

| Area | Que es distinto en nuestro fork |
|------|----------------------------------|
| Custom translation worker | Los trabajos de traduccion pasan por un `BackgroundService` propio con workers paralelos configurables y no solo por colas Hangfire. |
| PostgreSQL por defecto | PostgreSQL es la base de datos por defecto. SQLite sigue disponible para instalaciones pequenas. |
| Modelo de medios de 11 estados | Los medios reflejan el estado de traduccion en 11 estados incluyendo el ciclo de vida OCR: `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `NoSuitableSubtitles`, `Failed`, `AwaitingSource`, `OcrPending`, `OcrBlocked`. La logica de decision vive en `MediaStateService`. |
| Soporte multi-instancia | Peliculas y series guardan `SourceInstanceId`, lo que permite conectar varias instancias de Radarr y Sonarr a una sola instalacion. |
| Deferred repair | Las lineas fallidas pueden reintentarse con contexto alrededor, lo que hace mas robustas las pasadas de reparacion. |
| Circuit breaker por proveedor | Un circuit breaker singleton sigue los fallos por proveedor y aplica backoff automatico cuando se cruza el umbral. |
| Reanudacion de traducciones pausadas | Las peticiones que encuentran 429 (por ejemplo, Gemini) se pausan con el slot retenido y se reanudan cuando se levanta el limite. |
| Post-translation quality gate | Tras el lote, los parrafos supervivientes se puntuan con tolerancia configurable. La UI permite editar o rechazar. Activado por defecto, con interruptor en ajustes. |

### Procesamiento de subtitulos

- FFmpeg puede extraer subtitulos de texto desde pistas incrustadas en contenedores MKV y MP4.
- La limpieza ASS/SSA elimina comandos de dibujo, marcadores musicales, efectos placeholder y URLs antes de traducir.
- Las pistas escasas con menos de 50 lineas de dialogo se omiten.
- El descubrimiento de subtitulos externos detecta y sigue los archivos que anades manualmente.
- Las pistas bitmap (DVD/VobSub, PGS, etc.) se pasan por OCR y luego se traducen como cualquier otra fuente.
- Las comprobaciones de integridad ASS cazan fragmentos de etiquetas que se cuelan, para que los prompts no traten comandos de dibujo como dialogo.
- Las rutas de salida largas que excederian limites del sistema de archivos se embeben en el MKV original en lugar de escribirse junto al archivo multimedia.

### UI y operacion

- El asistente de onboarding guia la configuracion inicial de Radarr y Sonarr.
- Los widgets del dashboard soportan drag and drop y actualizaciones en tiempo real via SignalR.
- Los widgets de cola de trabajos e historial de traduccion aportan visibilidad que upstream todavia no incluye.
- El widget de uso de API muestra llamadas, tokens, latencia, errores y tasa de exito.
- Los fallos aparecen en un audit de quality gate donde puedes editar el cue problematico y aceptarlo o rechazarlo. Los lotes fallidos se pueden reencolar o descartar en bloque.
- Un completed translation compare viewer permite diff entre origen y traduccion tras finalizar.
- El widget de historial del dashboard usa scroll infinito en vez de paginacion, lo que importa en bibliotecas grandes.
- Upload Workspace es ahora una pestana dentro de la pagina Translations para reducir saltos entre paginas. Custom Sources sigue siendo una entrada propia dentro de los ajustes.
- El programador de trabajos configurable vive en la nueva pagina Tasks (antes Schedule), con interruptores por trabajo, expresiones cron, CardComponent compartidos, rejilla responsive y estados de carga y vacio.
- El cliente incluye 11 temas integrados, no solo un interruptor claro/oscuro.
- La UI esta traducida a ingles, neerlandes, aleman, frances, espanol, polaco y chino simplificado.

### Fiabilidad

- La limpieza de subtitulos huerfanos detecta medios renombrados que dejaron detras archivos traducidos.
- Los bulk integrity checks pueden validar subtitulos traducidos en toda la biblioteca.
- La proteccion contra ghost jobs evita sobrescribir estados terminales y limpia trabajo interrumpido tras reinicios.
- El exponential backoff y el requeue diferido reducen la presion sobre proveedores inestables.
- La reanudacion de traducciones pausadas retiene el slot del worker cuando se llega a un rate limit y reanuda automaticamente cuando se levanta.
- El silent token streaming para proveedores de IA reduce la latencia del primer token en traducciones largas.
- Las colas de traduccion propias respetan la prioridad del medio y evitan head-of-line blocking cuando una traduccion de baja prioridad se atasca.
- Las integraciones de Chutes, NanoGPT y CrofAI incluyen gestion de cuotas, controles especificos del proveedor y UI en este fork.

---

## Servicios soportados

Esto es lo que funciona hoy en nuestro fork. Parte de estos servicios tambien estan soportados por upstream, asi que esta seccion habla de compatibilidad, no de exclusividad.

**IA:**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (con seguimiento de cuota y pausa automatica)
- [NanoGPT](https://nano-gpt.com/) (con uso de suscripcion, reservas y pausa automatica)
- [CrofAI](https://crof.ai/) (seguimiento solo por creditos; pausa las traducciones automaticamente al agotar el saldo)
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

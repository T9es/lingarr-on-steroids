# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Traduction de sous-titres pour de vraies bibliotheques Radarr/Sonarr.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Snapshot verifie par rapport a `lingarr-translate/lingarr` le 27 mars 2026. L'upstream peut encore evoluer apres cette date.
>
> Migration depuis la v1.x ? La version 2.0.0 introduit des breaking changes. MySQL/MariaDB n'est plus pris en charge, les parametres ne sont pas migres automatiquement et un redemarrage propre est necessaire.

---

## Qu'est-ce que c'est ?

Lingarr on Steroids est un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Le flux principal reste le meme : indexer les medias via Radarr et Sonarr, trouver les pistes de sous-titres, les traduire avec des fournisseurs pris en charge, puis tout gerer depuis une interface web.

Ce fork met surtout l'accent sur la fiabilite des files d'attente, les bibliotheques multi-instances, la reparation des sous-titres et une meilleure visibilite operationnelle pour les grosses installations.

---

## Differences verifiees de ce fork

### Backend et files d'attente

| Domaine | Difference verifiee dans ce fork |
|---------|----------------------------------|
| Translation worker personnalise | Les jobs de traduction passent par un `BackgroundService` maison avec workers paralleles configurables, et pas uniquement par les files Hangfire. |
| PostgreSQL par defaut | PostgreSQL est la base par defaut. SQLite reste disponible pour les petites installations. |
| Modele d'etat des medias | Les medias utilisent 9 etats : `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `AwaitingSource`, `NoSuitableSubtitles`, `Failed`. |
| Support multi-instances | Les films et series stockent `SourceInstanceId`, ce qui permet de connecter plusieurs instances Radarr et Sonarr a une seule installation. |
| Deferred repair | Les lignes en echec peuvent etre retentees avec le contexte autour d'elles, ce qui rend les passes de reparation plus robustes. |

### Traitement des sous-titres

- FFmpeg peut extraire les sous-titres texte depuis des pistes integrees dans des conteneurs MKV et MP4.
- Le nettoyage ASS/SSA supprime les commandes de dessin, marqueurs musicaux, effets placeholder et URLs avant la traduction.
- Les pistes clairsemees avec moins de 50 lignes de dialogue sont ignorees.
- La decouverte de sous-titres externes detecte et suit les fichiers ajoutes manuellement.

### UI et exploitation

- L'assistant d'onboarding guide la premiere configuration de Radarr et Sonarr.
- Les widgets du tableau de bord gerent le drag-and-drop et les mises a jour temps reel via SignalR.
- Les widgets de file d'attente et d'historique de traduction apportent une visibilite absente de l'upstream actuel.
- Le widget d'usage API affiche appels, tokens, latence, erreurs et taux de succes.
- Le client propose 11 themes integres, pas seulement un basculement clair/sombre.
- L'interface est traduite en anglais, neerlandais, allemand, francais, espagnol, polonais et chinois simplifie.

### Fiabilite

- Le nettoyage des sous-titres orphelins detecte les medias renommes qui ont laisse des sous-titres traduits derriere eux.
- Les bulk integrity checks peuvent valider les sous-titres traduits dans toute la bibliotheque.
- La protection contre les ghost jobs evite d'ecraser des etats terminaux et nettoie le travail interrompu apres un redemarrage.
- L'exponential backoff et le requeue differe reduisent la pression sur les fournisseurs instables.
- L'integration Chutes ajoute une gestion des quotas et une logique specifique a ce fournisseur dans ce fork.

---

## Services pris en charge

Cette liste decrit la compatibilite de ce fork a la date du snapshot. Certains de ces services sont aussi disponibles upstream, il ne s'agit donc pas d'une revendication exclusive au fork.

**IA :**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (avec suivi des quotas et pause automatique)
- LocalAI / Ollama (auto-heberge)

**API cloud :**
- [LibreTranslate](https://libretranslate.com/)
- [DeepL](https://www.deepl.com/)
- [Google Translate](https://translate.google.com/)
- [Bing Translate](https://www.bing.com/translator)
- [Yandex Translate](https://translate.yandex.com/)
- [Azure Translator](https://www.microsoft.com/en-us/translator/business/translator-api/)

---

## Mise en route

### Tags de l'image Docker

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Derniere version stable | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Version specifique | `linux/amd64`, `linux/arm64` |
| `main` | Build de developpement | `linux/amd64`, `linux/arm64` |

PostgreSQL est recommande. SQLite convient aux petites installations (un utilisateur, <1000 medias).

> Remarque : toutes les images prennent en charge AMD64 (Intel/AMD) et ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (recommande)

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

### SQLite (demarrage rapide)

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

## Configuration

| Variable | Description | Par defaut |
|----------|-------------|------------|
| `TZ` | Fuseau horaire du conteneur | - |
| `ASPNETCORE_URLS` | Adresse HTTP d'ecoute | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` ou `sqlite` | `postgresql` |
| `SQLITE_DB_PATH` | Nom du fichier SQLite dans `/app/config` | `local.db` |
| `DB_HOST` | Hote PostgreSQL | - |
| `DB_PORT` | Port PostgreSQL | `5432` |
| `DB_DATABASE` | Nom de la base de donnees | - |
| `DB_USERNAME` | Utilisateur de la BDD | - |
| `DB_PASSWORD` | Mot de passe de la BDD | - |
| `MAX_PARALLEL_TRANSLATIONS` | Valeur de demarrage du pool de workers de traduction | `1` |
| `MAX_CONCURRENT_JOBS` | Nombre de workers Hangfire pour les files sync et systeme | `5` |
| `RADARR_URL` | Votre URL Radarr | - |
| `RADARR_API_KEY` | Cle API Radarr | - |
| `SONARR_URL` | Votre URL Sonarr | - |
| `SONARR_API_KEY` | Cle API Sonarr | - |

Reference complete des variables d'environnement dans [Settings.MD](Settings.MD).

---

## Credits

Lingarr original par [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Icones : [Lucide](https://lucide.dev/icons).  
Analyse des sous-titres : [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Traduction : LibreTranslate, bibliotheque GTranslate.

---

## Remerciements

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

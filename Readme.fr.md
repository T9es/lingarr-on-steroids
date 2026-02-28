# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Le sous-titrage qui fonctionne vraiment** - pour les gens qui gerent des bibliotheques mediatiques a grande echelle.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Mise a jour depuis v1.x?** La version 2.0.0 apporte des changements cassants - MySQL/MariaDB supprime, les parametres NE SONT PAS migres, nouveau depart requis. Voir ci-dessous pour les details.

---

## Qu'est-ce que c'est ?

Lingarr on Steroids est un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Nous avons gardé l'idée de base (traduire les sous-titres via Radarr/Sonarr) mais avons reconstruit la majeure partie du backend et ajouté beaucoup d'amélioration UI.

Ça a commencé parce que Lingarr original avait des problèmes de fiabilité sous charge. Nous avions besoin de quelque chose qui ne s'effondrerait pas quand vous avez des milliers d'émissions.

---

## Ce que nous avons changé

### Backend

| Quoi | Pourquoi |
|------|-----|
| Worker de traduction personnalisé | Hangfire s'étouffait sur les grandes files. Nous avons écrit notre propre BackgroundService qui gère 1-20 workers parallèles, des files de priorité, et récupération automatique après crash. |
| PostgreSQL par défaut | SQLite se bloque avec les workersconcurrents. Le MVCC dans PostgreSQL fonctionne réellement. Nous avons gardé SQLite comme option pour les petites configurations. |
| Suivi de traduction à 9 états | L'original n'avait pas de bonne façon de répondre "quoi doit être traduit ?". Nous avons ajouté des états (Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted) pour que les requêtes soient rapides. |
| Support multi-instance | Une instance Radarr/Sonarr ne suffit pas pour certains. Vous pouvez maintenant connecter plusieurs serveurs *arr à un seul Lingarr. |
| Réparation différée | Les lignes échouées sont réessayées avec le contexte environnant (10 lignes par défaut). La qualité de traduction LLM augmente considérablement quand l'IA peut voir ce qui se passe avant/après. |

### Traitement des sous-titres

- **Extraction FFmpeg** --extrait les sous-titres des conteneurs MKV/MP4 quand ils sont embarqués
- **Nettoyage ASS/SSA** -supprime les commandes de dessin, symboles musicaux, placeholders d'effets sonores, URLs
- **Filtre de tracks sparses** -saute les tracks avec <100 entrées (signes, chansons)
- **Découverte de sous-titres externes** -trouve les fichiers de sous-titres que vous ajoutez manuellement et les suit

### UI/UX

- **Widgets dashboard** -disposition glisser-déposer, mises à jour en temps réel via SignalR
- **Widget file d'attente** -montre ce qui tourne, ce qui est planifié, ce qui est échoué
- **Historique de traduction** -graphique + liste montrant quoi traduit quand
- **Suivi usage API** -graphiques sparkline montrant les dépenses par service
- **Assistant onboarding** -premier démarrage vous guide à travers la config Radarr/Sonarr
- **Support thème** -sombre/clair avec variables CSS pour correspondre à votre config
- **7 langues** -EN, NL, DE, FR, ES, PL, ZH
- **Détection offline** -affiche quand l'app est inaccessible

### Fiabilité

- **Nettoyage des orphelins** -détecte quand une mise à niveau renomme le fichier et que vos traductions AI sont maintenant orphelines
- **Vérification d'intégrité en masse** -valide chaque traduction dans votre bibliothèque
- **Nettoyage des jobs fantômes** -supprime les jobs bloqués qui n'ont jamais terminé
- **Backoff exponentiel** -réessaie avec du jitter pour ne pas surcharger les APIs_FAILED

---

## Services supportés

**IA :**
- OpenAI (GPT)
- Anthropic (Claude)
- Google Gemini
- DeepSeek
- Chutes.ai (avec suivi de quota et pause auto)
- LocalAI / Ollama (auto-heberge)

**APIs cloud :**
- LibreTranslate
- DeepL
- Google Translate
- Bing Translate
- Yandex Translate
- Azure Translator

---

## Pour commencer

### Tags d'image Docker

| Tag | Description | Architectures |
|-----|-------------|---------------|
| `latest` | Derniere version stable | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Version specifique | `linux/amd64`, `linux/arm64` |
| `main` | Build de developpement | `linux/amd64`, `linux/arm64` |

> **Note:** Toutes les images supportent AMD64 (Intel/AMD) et ARM64 (Raspberry Pi, Apple Silicon).

PostgreSQL est recommande. SQLite fonctionne pour les petites configurations (utilisateur unique, <1000 elements media).

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

### SQLite (démarrage rapide)

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

## Configuration

| Variable | Description | Défaut |
|----------|-------------|--------|
| `ASPNETCORE_URLS` | Port | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` ou `sqlite` | `postgresql` |
| `DB_HOST` | Hôte PostgreSQL | - |
| `DB_PORT` | Port PostgreSQL | `5432` |
| `DB_DATABASE` | Nom de la database | - |
| `DB_USERNAME` | Nom utilisateur DB | - |
| `DB_PASSWORD` | Mot de passe DB | - |
| `RADARR_URL` | Votre URL Radarr | - |
| `RADARR_API_KEY` | Clé API Radarr | - |
| `SONARR_URL` | Votre URL Sonarr | - |
| `SONARR_API_KEY` | Clé API Sonarr | - |

Liste complète dans [Settings.MD](Settings.MD).

---

## Crédits

Lingarr original par [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Icônes : [Lucide](https://lucide.dev/icons).  
Parsing sous-titres : [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Traduction : LibreTranslate, bibliothèque GTranslate.

---

## Remerciements

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)
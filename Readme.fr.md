# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**La traduction de sous-titres qui fonctionne vraiment** - pour les personnes gérant des médiathèques à grande échelle.

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Français](Readme.fr.md) | [Español](Readme.es.md) | [Chinese](Readme.zh.md)

---

> **Mise à niveau depuis la v1.x ?** La version 2.0.0 comporte des changements majeurs - MySQL/MariaDB a été supprimé, les paramètres ne sont PAS migrés, une installation propre est requise. Voir ci-dessous pour plus de détails.

---

## Qu'est-ce que c'est ?

Lingarr on Steroids est un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Nous avons gardé l'idée de base (traduire les sous-titres via Radarr/Sonarr) mais nous avons reconstruit la majeure partie du backend et ajouté de nombreuses améliorations de l'interface utilisateur.

Tout a commencé parce que le Lingarr original avait des problèmes de fiabilité sous charge. Nous avions besoin de quelque chose qui ne plante pas quand on a des milliers de séries ou de films.

---

## Ce que nous avons changé

### Backend

| Quoi | Pourquoi |
|------|-----|
| Worker de traduction personnalisé | Hangfire s'étouffait avec de grandes files d'attente. Nous avons écrit notre propre processus en arrière-plan qui gère 1 à 20 workers simultanés, des files d'attente prioritaires et une récupération automatique en cas de plantage. |
| PostgreSQL par défaut | SQLite se bloque avec des requêtes simultanées. Le MVCC de PostgreSQL fonctionne vraiment. Nous avons conservé SQLite comme option pour les petites configurations. |
| Suivi des traductions à 9 états | L'original n'avait pas de bon moyen de répondre à la question "Qu'est-ce qui doit être traduit ?". Nous avons ajouté des états (Inconnu, En attente, En cours, Terminé, Obsolète, En attente de source, Aucun sous-titre approprié, Échoué, Interrompu) pour des requêtes ultra-rapides. |
| Support multi-instances | Une instance Radarr/Sonarr n'est pas suffisante pour certains. Vous pouvez désormais connecter plusieurs serveurs *arr à un seul Lingarr. |
| Réparation différée | Les lignes échouées sont relancées avec le contexte environnant (10 lignes par défaut). La qualité de la traduction LLM (IA) s'améliore considérablement lorsque l'IA peut voir ce qui se passe avant et après. |

### Traitement des sous-titres

- **Extraction FFmpeg** - extrait les sous-titres des conteneurs MKV/MP4 lorsqu'ils sont intégrés
- **Nettoyage ASS/SSA** - supprime les commandes de dessin, symboles musicaux, balises d'effets sonores et URLs
- **Filtre de pistes clairsemées** - ignore les pistes avec <100 lignes (souvent juste des panneaux ou des chansons)
- **Détection de sous-titres externes** - trouve les fichiers de sous-titres que vous ajoutez manuellement et les suit

### UI/UX

- **Widgets du tableau de bord** - interface en glisser-déposer, mises à jour en temps réel via SignalR
- **Widget de file d'attente** - montre ce qui est en cours d'exécution, programmé ou a échoué
- **Historique de traduction** - graphique + liste montrant ce qui a été traduit et quand
- **Suivi de l'utilisation de l'API** - mini-graphiques montrant les dépenses par service
- **Assistant de configuration** - guide l'utilisateur pas-à-pas lors de la première configuration de Radarr/Sonarr
- **Support de thèmes** - sombre/clair avec des variables CSS pour correspondre à votre setup
- **7 langues** - EN, NL, DE, FR, ES, PL, ZH
- **Détection hors ligne** - indique quand l'application est inaccessible

### Fiabilité

- **Nettoyage des fichiers orphelins** - détecte lorsqu'une mise à niveau renomme le fichier et que vos traductions IA sont désormais orphelines
- **Vérification d'intégrité en masse** - valide chaque traduction dans votre bibliothèque
- **Nettoyage des tâches fantômes** - supprime les tâches bloquées qui ne se sont jamais terminées
- **Temporisation exponentielle (Backoff)** - réessaye avec un délai aléatoire pour ne pas surcharger les API défaillantes

---

## Services pris en charge

**IA :**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (avec suivi des quotas et pause automatique)
- LocalAI / Ollama (auto-hébergé)

**API Cloud :**
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
| `latest` | Dernière version stable | `linux/amd64`, `linux/arm64` |
| `1.2.3` | Version spécifique | `linux/amd64`, `linux/arm64` |
| `main` | Version de développement | `linux/amd64`, `linux/arm64` |

PostgreSQL est recommandé. SQLite fonctionne pour les petites configurations (utilisateur unique, <1000 médias).

> **Remarque :** Toutes les images prennent en charge à la fois l'AMD64 (Intel/AMD) et l'ARM64 (Raspberry Pi, Apple Silicon).

### PostgreSQL (recommandé)

```yaml
version: "3.8"

services:
  lingarr:
    image: ree0/lingarr-on-steroids:latest
    container_name: lingarr
    environment:
      - TZ=Europe/Paris
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
      - TZ=Europe/Paris
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

| Variable | Description | Par défaut |
|----------|-------------|------------|
| `ASPNETCORE_URLS` | Port | `http://+:9876` |
| `DB_CONNECTION` | `postgresql` ou `sqlite` | `postgresql` |
| `DB_HOST` | Hôte PostgreSQL | - |
| `DB_PORT` | Port PostgreSQL | `5432` |
| `DB_DATABASE` | Nom de la base de données | - |
| `DB_USERNAME` | Utilisateur de la BDD | - |
| `DB_PASSWORD` | Mot de passe de la BDD | - |
| `RADARR_URL` | Votre URL Radarr | - |
| `RADARR_API_KEY` | Clé API Radarr | - |
| `SONARR_URL` | Votre URL Sonarr | - |
| `SONARR_API_KEY` | Clé API Sonarr | - |

Liste complète des paramètres dans [Settings.MD](Settings.MD).

---

## Crédits

Lingarr original par [rowanfuchs](https://github.com/lingarr-translate/lingarr).

Icônes : [Lucide](https://lucide.dev/icons).  
Analyse des sous-titres : [AlexPoint](https://github.com/AlexPoint/SubtitlesParser).  
Traduction : LibreTranslate, bibliothèque GTranslate.

---

## Remerciements

- [selfh.st](https://selfh.st/?ref=lingarr)
- [r/selfhosted](https://www.reddit.com/r/selfhosted/)
- [FrankieBBBB](https://github.com/FrankieBBBB)

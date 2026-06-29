# Lingarr on Steroids

<!-- Badge row -->
[![Version](https://img.shields.io/github/v/release/T9es/lingarr-on-steroids?style=for-the-badge&logo=github&color=blue)](https://github.com/T9es/lingarr-on-steroids/releases)
[![Docker](https://img.shields.io/docker/pulls/ree0/lingarr-on-steroids?style=for-the-badge&logo=docker&color=2496ED)](https://hub.docker.com/r/ree0/lingarr-on-steroids)
[![License](https://img.shields.io/badge/license-AGPL--3.0-green.svg?style=for-the-badge)](LICENSE)
[![Discord](https://img.shields.io/discord/1293119073739210885?style=for-the-badge&logo=discord&logoColor=white&label=discord&color=7289DA)](https://discord.gg/HkubmH2rcR)

**Traduction de sous-titres pour de vraies bibliotheques Radarr/Sonarr.**

[English](Readme.MD) | [Deutsch](Readme.de.md) | [Polski](Readme.pl.md) | [Nederlands](Readme.nl.md) | [Francais](Readme.fr.md) | [Espanol](Readme.es.md) | [中文](Readme.zh.md)

---

> Ce README decrit notre fork et l'etat du projet upstream Lingarr au 27 mars 2026. Si l'upstream evolue ensuite, certains details ici pourront devenir un peu moins actuels avec le temps.
>
> Ce README decrit notre fork et l'etat du projet upstream Lingarr au 29 juin 2026. Si l'upstream evolue ensuite, certains details ici pourront devenir un peu moins actuels avec le temps.
>
> Migration depuis la v1.x ? La version 2.0.0 introduit des breaking changes. MySQL/MariaDB n'est plus pris en charge, les parametres ne sont pas migres automatiquement et un redemarrage propre est necessaire.
>
> Migration depuis la v2.x vers v3.0.0 ? Voir le [CHANGELOG](CHANGELOG.md) pour les notes de migration. La page Schedule devient Tasks. L'assistant d'onboarding, le planificateur de jobs configurable et le post-translation quality gate ont change. CrofAI rejoint les fournisseurs IA pris en charge. Le badge de version en bas a gauche affiche maintenant la vraie version de dev-build au lieu de "Dev Build".

---

## Quoi de neuf dans v3.0.0

La v3 est un changement majeur par rapport a v2.5.0. Si vous ne lisez qu'une seule section, lisez celle-ci.

- **Versionnement conscient de Git.** La version de l'assembly est resolue depuis `git describe` au build et le build Docker transmet un build-arg `VERSION`. Pour sortir une release, il suffit de tagger `v3.0.0` et de pousser le tag. Plus besoin d'editer `Lingarr.Core.csproj` a la main.
- **Le badge Dev Build montre la vraie version.** Le badge en bas a gauche affiche maintenant `Dev <version>` (par exemple `Dev 3.0.0-216-g39ae09b2`) au lieu du texte generique precedent.
- **CrofAI devient un fournisseur IA pris en charge**, avec suivi d'usage en credits uniquement. Les traductions se mettent en pause automatiquement quand le solde de credits tombe a zero. Voir les nouvelles variables `CROFAI_*` dans [Settings.MD](Settings.MD).
- **OCR pour les sous-titres bitmap.** Les pistes DVD/VobSub, PGS et autres basees sur image sont passees par OCR puis traduites comme n'importe quelle autre source. Deux nouveaux etats (`OcrPending`, `OcrBlocked`) couvrent le cycle de vie OCR.
- **Circuit breaker par fournisseur.** Si un fournisseur se met a renvoyer des 5xx, le circuit s'ouvre et les requetes sont mises en pause, plutot que de bruler votre quota d'API pendant la panne.
- **Reprise des traductions en pause.** Les 429 du fournisseur (par exemple les limites Gemini) ne tuent plus la traduction. Le worker garde le slot et reprend quand la limite se leve.
- **Post-translation quality gate.** Apres la fin d'un lot, les paragraphes survivants sont scores. L'UI permet de revoir, editer, accepter ou rejeter ceux qui sortent de la tolerance, avec Requeue All et Dismiss All en action de masse.
- **Mode langue source automatique.** La langue source peut etre detectee par cue avec NLLB (FLORES-200 spBLEU), comparaison de tiers LLM et heuristiques de famille linguistique. Interrupteur dans l'onboarding et dans les reglages de langue source.
- **Planificateur de jobs configurable sur la nouvelle page Tasks.** Chaque job Hangfire et de traduction a son propre interrupteur et une expression cron. La page Tasks remplace l'ancienne page Schedule, avec les composants CardComponent partages, une grille 1/2/3 responsive, des etats de chargement et vide, et un nettoyage SignalR corrige. L'ancien bloc d'automation de la carte limits a disparu.
- **Embedding et detection de langue configurables, avec nouvelle UI.** Reglages frontend pour le comportement d'embedding MKV, la detection de langue sur les streams non tagues et un plafond de reessais par requete.
- **Fallback d'embedding MKV pour les chemins longs.** Si le chemin du sous-titre traduit depasse les limites classiques du systeme de fichiers (les noms de fichiers anime en sont le cas type), la traduction est embedee dans le MKV d'origine.
- **Upload workspace deplace sous translations.** L'Upload Workspace est maintenant accessible comme onglet dans la page Translations, ce qui reduit les sauts de pages.
- **Scroll infini du dashboard, completed translation compare viewer, ameliorations du widget d'usage API.** Qualite de vie qui paye sur les grosses bibliotheques.
- **Modeles d'issue GitHub** pour bug, feature et setup dans `.github/ISSUE_TEMPLATE/`. Merci de les utiliser.

La migration complete est dans [CHANGELOG.md](CHANGELOG.md#migration-notes-for-3x-v300).

---
---

## Qu'est-ce que c'est ?

Lingarr on Steroids est un fork de [Lingarr](https://github.com/lingarr-translate/lingarr). Le flux principal reste le meme : indexer les medias via Radarr et Sonarr, trouver les pistes de sous-titres, les traduire avec des fournisseurs pris en charge, puis tout gerer depuis une interface web.

Ce fork met surtout l'accent sur la fiabilite des files d'attente, les bibliotheques multi-instances, la reparation des sous-titres et une meilleure visibilite operationnelle pour les grosses installations.

---

## Ce que nous avons change

### Backend et files d'attente

| Domaine | Ce qui est different dans notre fork |
|---------|-----------------------------------|
| Translation worker personnalise | Les jobs de traduction passent par un `BackgroundService` maison avec workers paralleles configurables, et pas uniquement par les files Hangfire. |
| PostgreSQL par defaut | PostgreSQL est la base par defaut. SQLite reste disponible pour les petites installations. |
| Modele d'etat des medias a 11 etats | Les medias suivent leur statut de traduction sur 11 etats couvrant le cycle OCR : `Unknown`, `NotApplicable`, `Pending`, `InProgress`, `Complete`, `Stale`, `NoSuitableSubtitles`, `Failed`, `AwaitingSource`, `OcrPending`, `OcrBlocked`. La logique vit dans `MediaStateService`. |
| Support multi-instances | Les films et series stockent `SourceInstanceId`, ce qui permet de connecter plusieurs instances Radarr et Sonarr a une seule installation. |
| Deferred repair | Les lignes en echec peuvent etre retentees avec le contexte autour d'elles, ce qui rend les passes de reparation plus robustes. |
| Circuit breaker par fournisseur | Un circuit breaker singleton suit les erreurs par fournisseur de traduction et applique un backoff automatique au-dela du seuil. |
| Reprise des traductions en pause | Les requetes qui frappent un 429 (par exemple Gemini) se mettent en pause avec le slot conserve et reprennent des que la limite se leve. |
| Post-translation quality gate | Apres le lot, les paragraphes survivants sont scores avec une tolerance configurable. L'UI permet d'editer ou rejeter. Active par defaut, avec interrupteur. |

### Traitement des sous-titres

- FFmpeg peut extraire les sous-titres texte depuis des pistes integrees dans des conteneurs MKV et MP4.
- Le nettoyage ASS/SSA supprime les commandes de dessin, marqueurs musicaux, effets placeholder et URLs avant la traduction.
- Les pistes clairsemees avec moins de 50 lignes de dialogue sont ignorees.
- La decouverte de sous-titres externes detecte et suit les fichiers ajoutes manuellement.
- Les pistes bitmap (DVD/VobSub, PGS, etc.) sont passees par OCR puis traduites comme n'importe quelle autre source.
- Les controles d'integrite ASS detectent les fragments de tags qui fuitent, pour eviter que les prompts ne traitent des commandes de dessin comme du dialogue.
- Les chemins de sortie longs qui depasseraient les limites classiques du systeme de fichiers sont embedes dans le MKV d'origine au lieu d'etre ecrits a cote du media.

### UI et exploitation

- L'assistant d'onboarding guide la premiere configuration de Radarr et Sonarr.
- Les widgets du tableau de bord gerent le drag-and-drop et les mises a jour temps reel via SignalR.
- Les widgets de file d'attente et d'historique de traduction apportent une visibilite absente de l'upstream actuel.
- Le widget d'usage API affiche appels, tokens, latence, erreurs et taux de succes.
- Les echecs apparaissent dans un audit quality gate ou vous pouvez editer le cue en cause, puis accepter ou rejeter. Les lots echoues peuvent etre requeues ou rejetes en masse.
- Un completed translation compare viewer permet de comparer source et traduction apres coup.
- Le widget d'historique du tableau de bord utilise le scroll infini au lieu de la pagination, ce qui compte sur les grosses bibliotheques.
- L'Upload Workspace est maintenant un onglet dans la page Translations, ce qui reduit les sauts entre pages. Custom Sources reste une entree dediee dans les reglages.
- Le planificateur de jobs configurable vit sur la nouvelle page Tasks (ex-Schedule) avec interrupteurs par job, expressions cron, composants CardComponent partages, grille responsive et etats de chargement et vide.
- Le client propose 11 themes integres, pas seulement un basculement clair/sombre.
- L'interface est traduite en anglais, neerlandais, allemand, francais, espagnol, polonais et chinois simplifie.

### Fiabilite

- Le nettoyage des sous-titres orphelins detecte les medias renommes qui ont laisse des sous-titres traduits derriere eux.
- Les bulk integrity checks peuvent valider les sous-titres traduits dans toute la bibliotheque.
- La protection contre les ghost jobs evite d'ecraser des etats terminaux et nettoie le travail interrompu apres un redemarrage.
- L'exponential backoff et le requeue differe reduisent la pression sur les fournisseurs instables.
- La reprise des traductions en pause conserve le slot du worker en cas de rate limit et reprend automatiquement.
- Le silent token streaming pour les fournisseurs IA reduit la latence du premier token sur les traductions longues.
- Les files de traduction propres respectent la priorite du media et evitent le head-of-line blocking quand une traduction de basse priorite se bloque.
- Les integrations Chutes, NanoGPT et CrofAI apportent chacune gestion de quotas, controles specifiques au fournisseur et UI dans ce fork.

---

## Services pris en charge

Voici ce qui fonctionne aujourd'hui dans notre fork. Une partie de ces services est aussi disponible upstream, donc cette section parle de compatibilite, pas d'exclusivite.

**IA :**
- [OpenAI](https://openai.com/) (GPT)
- [Anthropic](https://www.anthropic.com/) (Claude)
- [Google Gemini](https://gemini.google.com/)
- [DeepSeek](https://deepseek.com/)
- [Chutes.ai](https://chutes.ai/) (avec suivi des quotas et pause automatique)
- [NanoGPT](https://nano-gpt.com/) (avec usage d'abonnement, reserves et pause automatique)
- [CrofAI](https://crof.ai/) (suivi par credits uniquement ; pause automatiquement les traductions quand le solde tombe a zero)
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

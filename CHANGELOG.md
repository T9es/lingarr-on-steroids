# Changelog

All notable changes to Lingarr on Steroids are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Dates are written in ISO 8601 (`YYYY-MM-DD`).

## [3.0.0] - 2026-07-06

Major release. 33 new features and 102+ fixes since v2.5.0. This is a substantial
change over v2.5.0 and users upgrading from a v2.x install should read the
[Migration notes](#migration-notes-for-3x-v300) before pulling the new image.

### Added

- **CrofAI as a supported AI translation provider.** New `CROFAI_MODEL` and
  `CROFAI_API_KEY` env vars. CrofAI is credits-only (Crof.ai no longer
  offers subscriptions as of v3.0.0); translations auto-pause when the
  credits balance reaches zero. Credits-only usage is exposed in the API
  usage widget.
- **OCR for bitmap subtitles.** DVD/VobSub, PGS, and other image-based subtitle
  tracks are now OCR'd into text and then translated like any other source.
  Two new translation states (`OcrPending`, `OcrBlocked`) cover the OCR
  lifecycle, and OCR detection is split into its own Hangfire job.
- **OCR-aware translation prompts.** Prompts now flag OCR'd source material so
  AI providers can be told about expected noise from OCR.
- **Auto source-language mode.** Source language can be auto-detected per cue
  using a `TranslationQualityScorer` that combines NLLB (FLORES-200 spBLEU)
  scores, LLM tier comparisons and language-family heuristics. Toggle in the
  onboarding language step and the source-language settings.
- **Per-provider circuit breaker.** A singleton `ProviderCircuitBreaker` tracks
  failures per translation provider and backs off automatically when error
  thresholds are crossed. A flaky Gemini or OpenAI endpoint will no longer
  drain your API quota.
- **Paused translation recovery.** Translation requests that hit provider rate
  limits (for example, Gemini 429) are paused with the worker slot held until
  the limit clears, instead of failing the request and losing the slot.
- **Post-translation quality gate.** After a batch finishes, surviving
  paragraphs are scored with a configurable tolerance. New settings toggle
  plus UI surface to review, edit, accept, or reject failing items. Default is
  on.
- **Failed-translation review and inline editing.** The quality-gate audit now
  exposes inline editing and an Accept / Reject flow, with batch Requeue All
  and Dismiss All buttons.
- **Configurable job scheduling.** Each Hangfire and translation job has its
  own enable toggle and per-job cron expression on the Schedule page.
- **MKV subtitle embedding fallback.** When translated subtitle output paths
  exceed filesystem limits (long anime file names are the usual culprit), the
  translation is embedded back into the original MKV instead.
- **Paused MKV and short output paths** are used when extracting translated
  embedded subtitles to avoid `File name too long` errors at extraction time.
- **NanoGPT provider.** Foundation, backend usage, settings UI, and
  subscription-aware reserves / weekly token allowance / auto-pause.
- **Frontend settings UI** for MKV embedding, untagged-stream language
  detection, and a request retry cap.
- **Batch LLM language detection.** Untagged subtitle streams are detected in
  bulk via the LLM.
- **TranslatedSubtitle safe-write.** Fixes that prevent `TranslatedSubtitle`
  from being overwritten with the source path when MKV embedding succeeds.
- **Configurable subtitle retry cap.** Failed translations can be retried up
  to a configurable cap before being permanently marked as failed. Failed
  translation requests are preferred over `AwaitingSource` when the queue is
  ordered.
- **Configured Subtitle translation pipeline hardening.** Many internal fixes
  to avoid corrupting source-sidecar mismatches and source-key drift.
- **ASS integrity tag-leakage checks.** Drawing artifacts and stale tags are
  stripped from ASS dialogues before translation.
- **Dashboard infinite scroll.** Translation history widget uses infinite
  scroll instead of paging.
- **Completed translation compare viewer.** Diff source vs translated text
  side by side after a translation completes.
- **Token streaming.** Translation providers now stream tokens silently to
  reduce first-token latency on long translations.
- **Upload workspace moved under translations workflow.** The Upload Workspace
  now lives as a tab inside the Translations page for fewer cross-page jumps.
  Custom Sources remains a top-level settings entry.
- **GitHub issue templates.** `.github/ISSUE_TEMPLATE/` now ships bug,
  feature, and setup templates.
- **Multiple unwindable dependabot bumps**, including `vue-tsc` 2.2.12 to
  3.2.6, `eslint` 9.x to 10.1.0, `follow-redirects`, `Microsoft.AspNetCore.SpaServices.Extensions`
  9.0.14 to 9.0.15, `DeepL.net` 1.20 to 1.21, `Microsoft.EntityFrameworkCore`
  9.0.11 to 9.0.14, and several Hangfire / EFCore / Npgsql patch bumps.
- **Auto-approve repeated sibling subtitle cues.** Repeated dialogue cues
  from the same subtitle track are now auto-approved during comparison,
  reducing manual review for multi-track sources.
- **Skip translation queueing for embedded target subtitles.** Subtitles
  that are already embedded in the target language are no longer queued
  for re-translation.
- **Git-aware release versioning.** `Directory.Build.props` now resolves
  the assembly version from `git describe` at build time, the Dockerfile
  forwards a `VERSION` build-arg, and `Lingarr.Core.csproj` no longer
  hard-codes the version. To cut a release, just tag `v3.0.0` and push.
  The Docker build workflow exports the tag-derived version as a GHA
  output and passes it through the new build-arg.
- **Dev-build badge shows the real version.** The aside's dev-build badge
  now reads `Dev <version>` (for example, `Dev 3.0.0-216-g39ae09b2`)
  instead of the previous generic `Dev Build` text. Falls back to the old
  text if the version cannot be resolved.
- **`TasksPage` (renamed from `SchedulePage`).** The page file, route,
  nav label, and i18n keys now use the `tasks` namespace. Internally the
  page was redesigned to use the shared `CardComponent`, a 1/2/3-column
  responsive grid, explicit loading and empty states, and a corrected
  SignalR teardown that captures and `.off()`s the same handler reference
  that was bound (the previous implementation `.off()`d with an empty
  no-op closure, which did nothing). 17 new `LingarrVersionTests` cover
  the dev/release detection logic.
- **New i18n keys** for OCR (`common.ocrRun`, `ocrRetry`, `ocrQueued`,
  `ocrProcessing`, `ocrReady`, `ocrBlocked`, `ocrUnsupported`, `ocrPreview`,
  `ocrApprove`, `ocrFailed`, `ocrApproveFailed`, `ocrPreviewFailed`,
  `ocrRequired`, `ocrQuality`, `ocrCueCount`), the dev-build badge
  (`common.devBuildWithVersion`), the navigation entries
  (`navigation.customSources`, `navigation.uploadWorkspace`), and the
  Tasks page (`schedule.noJobs`, `settings.automation.limitsDescription`)
  are now translated across all 7 supported locales.

### Changed

- **Media state machine extended from 9 to 11 states.** OCR lifecycles are
  now first-class. New states: `OcrPending`, `OcrBlocked`.
- **Onboarding wizard redesigned.** Re-running onboarding is the recommended
  way to pick up the v3 defaults for source mode, service selection, and
  subtitle defaults.
- **Settings page reorganised.** Subtitle settings, source-language handling,
  embedding, and quality-gate toggles were reordered. Some settings may have
  moved. Search the page rather than browsing by old position.
- **Schedule page renamed to Tasks.** The page now lives at `/settings/tasks`
  with the navigation label `Tasks`. The old automation block on the
  limits card is gone. If you had automation there, re-enable it on the
  new Tasks page.
- **Webhook validation order changed.** Sonarr webhooks now check event type
  before payload structure, matching the Radarr behaviour and removing false
  rejections.
- **Failed translation priority rule.** When picking the next translation
  request, `AwaitingSource` is preferred over `Failed` so retries take
  precedence over fresh failures.
- **Source-key matching is now strict.** Mismatched source keys fail fast
  instead of falling back to the wrong source.
- **Translation validation guards are more lenient by default.** A new
  batch-level relaxation reduces false positives. A UI toggle re-tightens the
  guard if you preferred the old strict behaviour.
- **Translate compare flow improvements.** Side-by-side compare shows the
  translated, not the source, when reviewing failed translations. Editability
  of "missing lines" is clarified, and Accept preserves translator metadata.
- **`CI` runs on `latest`, `main`, `develop`, `fix/**`, and `feat/**`.**
  Docker images are published from `main` and from `v*.*.*` tags. The
  Docker build now uses `fetch-depth: 0` and `fetch-tags: true` so non-tag
  pushes can still resolve a version from git.
- **Version reporting in the UI.** Dev builds now show the real git-describe
  version (`Dev 3.0.0-216-g39ae09b2`) instead of a generic `Dev Build`
  label. Update-available detection in the badge now triggers correctly
  between consecutive tag releases.
- **Subtitle settings grouped into a single card with row layout.**
  The subtitle settings UI was reorganised for clarity.
- **Tasks page layout redesigned.** The Tasks page (formerly Schedule)
  was visually redesigned with a cleaner layout.

### Fixed

- 102 fixes since v2.5.0, including but not limited to:
  - Stale subtitle source detection and re-translation of stale sources.
  - Bulk audit, accept endpoint, and compare-modal UI bugs.
  - `PathTooLongException` for anime with long filenames (MKV embed fallback).
  - Indexed unique constraints prevent duplicate translation requests.
  - Race conditions in embedded-subtitle extraction cleanup.
  - Frontend-side pagination freezes in translation compare table.
  - Hardening of the post-translation quality gate echo detection.
  - Repeated forced dialogue retranslation loops.
  - Subtitle sidecar integrity verifier reporting.
  - `PathTooLongException` resolved by MKV embed.
  - Untranslated source-sidecar mishandling when source-key mismatches.
  - `U+2019` (right single quote) breaking `mkvmerge` subtitle embedding.
  - OCR cache expiry causing wasteful re-translation cycles.
  - OCR queueing from OCR output after success.
  - OCR fallback when text-based source language not found.
  - VTT parser / writer position handling.
  - NanoGPT batch schema rejection for thinking models.
  - Backend usage field parsing for NanoGPT.
  - Async JSON-object batching for NanoGPT thinking models.
  - Circuit breaker Espresso thresholds once tripped.
  - Empty-string prompt instruction removed.
  - `usable_requests` decimal-vs-int mismatch on CrofAI usage API.
  - Provider retry suppression when circuit is open.
  - ASS compare and auto-complete output alignment fixes.
  - Dev-build badge SHA trimmed to fit in the sidebar.
  - Remaining English strings translated and mojibake in JSON fixed.
  - Diacritics restored in all localized READMEs.
  - Duplicate keys removed from en.json.

### Removed

- **CrofAI daily-quota tracking was removed.** Crof.ai no longer offers
  subscriptions. The previous daily-quota auto-pause hook is no longer
  relevant, and credits-exhausted auto-pause now handles the soft-cap
  behaviour.

### Migration notes for 3.x (v3.0.0)

These are the steps to take before and immediately after upgrading from any v2.x
release to v3.0.0. Skip none of them.

1. **Take a database backup.** There is no automatic settings migration from
   v2.x to v3.0.0. Postgres users should `pg_dump` and verify the backup
   before pulling the new image. SQLite users should copy the `lingarr.db`
   file out of `/app/config`.
2. **Re-run onboarding.** Default values, source-language handling, and
   service selection all changed. The new wizard picks the new defaults for
   you. Existing settings are kept across the upgrade unless you change them
   in onboarding.
3. **Re-enable any automation you had on the old Schedule page's limits
   card.** The automation block on the limits card is gone. The new
   Tasks page now owns all job scheduling, with per-job enable toggles
   and cron expressions. Re-enable any jobs you had configured.
4. **The Schedule page is now Tasks.** The page file was renamed from
   `SchedulePage.vue` to `TasksPage.vue`, the i18n nav label is now `Tasks`,
   and the page is the redesigned replacement for the old per-job automation
   card that used to live on the limits card. The route has been
   `/settings/tasks` since v2.x, so bookmarks should still work.
5. **Decide on the post-translation quality gate.** Default is on with a
   tolerance threshold that may reject some previously accepted translations.
   If you want the v2.x "accept all" behaviour, open Settings and toggle the
   quality gate off.
6. **Docker `:latest` is now v3.0.0.** Any `docker-compose.yml` pulling
   `ree0/lingarr-on-steroids:latest` will auto-upgrade on the next pull. If
   you want to stay on v2.5.0 for now, pin to the `2.5.0` tag explicitly
   before pulling.
7. **MySQL / MariaDB** are still not supported. This has not changed from
   v2.0.0. The supported engines remain PostgreSQL (default) and SQLite.
8. **Settings are not migrated automatically** between v2.x and v3.0.0. This
   has not changed from v2.0.0. A clean start is expected on upgrade.
9. **CrofAI subscriptions were dropped by Crof.ai.** The previous daily-quota
   auto-pause is no longer relevant. Credits-exhausted auto-pause handles
   the soft-cap behaviour. If you want a hard budget cap, configure the
   per-provider circuit breaker via the UI.

## [2.5.0] - 2026-03-27

Maintenance release. Polished the onboarding flow, hardened multi-instance
sync and webhooks, and landed a stack of media-state, dashboard, and
subtitle-reliability fixes including `AwaitingSource` auto-recovery, accurate
dashboard translation counts, dashboard widget-shrink-on-resize fixes, and
proper LLM token usage tracking. Re-introduced the webhook queue, WebVTT
support, network retries, and database indexes.

## [2.3.0] - 2026-02-12

Translation queue performance fixes plus embedded-subtitle extraction,
manual subtitle selection, and the integrity-check system.

[Unreleased]: https://github.com/T9es/lingarr-on-steroids/compare/v2.5.0...HEAD
[3.0.0]: https://github.com/T9es/lingarr-on-steroids/compare/v2.5.0...v3.0.0
[2.5.0]: https://github.com/T9es/lingarr-on-steroids/compare/v2.3.0...v2.5.0
[2.3.0]: https://github.com/T9es/lingarr-on-steroids/releases/tag/v2.3.0

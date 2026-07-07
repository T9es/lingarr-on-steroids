<template>
    <div class="space-y-4 lg:space-y-5">
        <p class="text-secondary-content -mt-1 text-sm leading-6">
            {{ translate('onboarding.settings.smartDefaultsApplied') }}
        </p>

        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <div
                v-for="item in settingsSummaryItems"
                :key="item.label"
                class="bg-primary/40 border-secondary/30 min-w-0 rounded-2xl border p-4">
                <p class="text-secondary-content text-xs font-semibold tracking-[0.16em] uppercase">
                    {{ item.label }}
                </p>
                <p class="text-primary-content mt-2 text-lg font-semibold">
                    {{ item.value }}
                </p>
                <p class="text-secondary-content mt-1 text-xs leading-5">
                    {{ item.description }}
                </p>
            </div>
        </div>

        <!-- Subtitle Tagging -->
        <div class="bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border p-4 sm:p-5">
            <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('settings.subtitle.useSubtitleTagging') }}
                    </span>
                    <p class="text-secondary-content text-xs leading-5">
                        {{ translate('settings.subtitle.useSubtitleTaggingDescription') }}
                    </p>
                </div>
                <ToggleButton v-model="useSubtitleTagging">
                    <span class="text-primary-content text-sm font-medium">
                        {{ translateBoolean(useSubtitleTagging) }}
                    </span>
                </ToggleButton>
            </div>
            <div v-if="useSubtitleTagging === 'true'" class="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                    <span class="text-primary-content font-semibold">
                        {{ translate('settings.subtitle.subtitleTag') }}
                    </span>
                    <InputComponent
                        v-model="subtitleTag"
                        validation-type="string"
                        placeholder="-AI-TRANSLATED-"
                        @update:validation="(val) => (isValid.subtitleTag = val)" />
                </div>
                <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                    <span class="text-primary-content font-semibold">
                        {{ translate('settings.subtitle.subtitleTagShort') }}
                    </span>
                    <p class="text-secondary-content text-xs leading-5">
                        {{ translate('settings.subtitle.subtitleTagShortDescription') }}
                    </p>
                    <InputComponent
                        v-model="subtitleTagShort"
                        validation-type="string"
                        placeholder="-ai-"
                        @update:validation="(val) => (isValid.subtitleTagShort = val)" />
                </div>
            </div>
        </div>

        <!-- OCR -->
        <div class="bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border p-4 sm:p-5">
            <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('settings.subtitle.ocrEnabled') }}
                    </span>
                    <p class="text-secondary-content text-xs leading-5">
                        {{ translate('settings.subtitle.ocrEnabledDescription') }}
                    </p>
                </div>
                <ToggleButton v-model="subtitleOcrEnabled">
                    <span class="text-primary-content text-sm font-medium">
                        {{ translateBoolean(subtitleOcrEnabled) }}
                    </span>
                </ToggleButton>
            </div>
        </div>

        <!-- Embedding Mode -->
        <div class="bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border p-4 sm:p-5">
            <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('settings.subtitle.embedMode') }}
                    </span>
                    <p class="text-secondary-content text-xs leading-5">
                        {{ translate('settings.subtitle.embedModeDescription') }}
                    </p>
                </div>
                <SelectComponent v-model:selected="embedMode" :options="embedOptions" />
            </div>
        </div>

        <!-- Strip ASS Drawing Commands -->
        <div class="bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border p-4 sm:p-5">
            <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('settings.subtitle.stripAssDrawingCommands') }}
                    </span>
                    <p class="text-secondary-content text-xs leading-5">
                        {{ translate('settings.subtitle.stripAssDrawingCommandsDescription') }}
                    </p>
                </div>
                <ToggleButton v-model="stripAssDrawingCommands">
                    <span class="text-primary-content text-sm font-medium">
                        {{ translateBoolean(stripAssDrawingCommands) }}
                    </span>
                </ToggleButton>
            </div>
        </div>

        <!-- Cleanup Orphaned Subtitles -->
        <div class="bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border p-4 sm:p-5">
            <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('settings.subtitle.cleanupOrphanedSubtitles') }}
                    </span>
                    <p class="text-secondary-content text-xs leading-5">
                        {{ translate('settings.subtitle.cleanupOrphanedSubtitlesDescription') }}
                    </p>
                </div>
                <ToggleButton v-model="cleanupOrphanedSubtitles">
                    <span class="text-primary-content text-sm font-medium">
                        {{ translateBoolean(cleanupOrphanedSubtitles) }}
                    </span>
                </ToggleButton>
            </div>
        </div>

        <!-- Advanced note -->
        <p class="text-secondary-content/60 text-xs leading-5">
            {{ translate('onboarding.settings.advancedAvailable') }}
        </p>
    </div>
</template>

<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import InputComponent from '@/components/common/InputComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const settingsStore = useSettingStore()

const isValid = reactive({
    subtitleTag: true,
    subtitleTagShort: true
})

function translateBoolean(value: string): string {
    return value === 'true' ? translate('common.enabled') : translate('common.disabled')
}

// Subtitle Tagging
const useSubtitleTagging = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.USE_SUBTITLE_TAGGING) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_SUBTITLE_TAGGING, newValue, true)
    }
})

const subtitleTag = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SUBTITLE_TAG) as string) ?? '-AI-TRANSLATED-',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG, newValue, isValid.subtitleTag)
    }
})

const subtitleTagShort = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SUBTITLE_TAG_SHORT) as string) ?? '-ai-',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG_SHORT, newValue, isValid.subtitleTagShort)
    }
})

// OCR
const subtitleOcrEnabled = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SUBTITLE_OCR_ENABLED) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_OCR_ENABLED, newValue, true)
    }
})

// Strip ASS Drawing Commands
const stripAssDrawingCommands = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.STRIP_ASS_DRAWING_COMMANDS) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_ASS_DRAWING_COMMANDS, newValue, true)
    }
})

// Cleanup Orphaned Subtitles
const cleanupOrphanedSubtitles = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.CLEANUP_ORPHANED_SUBTITLES) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.CLEANUP_ORPHANED_SUBTITLES, newValue, true)
    }
})

// Embedding Mode
const embedMode = computed({
    get: (): string => {
        const always = settingsStore.getSetting(SETTINGS.EMBED_IN_CONTAINER) as string
        const whenTooLong = settingsStore.getSetting(SETTINGS.EMBED_WHEN_PATH_TOO_LONG) as string
        if (always === 'true') return 'always'
        if (whenTooLong === 'true') return 'when_too_long'
        return 'never'
    },
    set: (newValue: string): void => {
        if (newValue === 'always') {
            settingsStore.updateSetting(SETTINGS.EMBED_IN_CONTAINER, 'true', true)
            settingsStore.updateSetting(SETTINGS.EMBED_WHEN_PATH_TOO_LONG, 'false', true)
        } else if (newValue === 'when_too_long') {
            settingsStore.updateSetting(SETTINGS.EMBED_IN_CONTAINER, 'false', true)
            settingsStore.updateSetting(SETTINGS.EMBED_WHEN_PATH_TOO_LONG, 'true', true)
        } else {
            settingsStore.updateSetting(SETTINGS.EMBED_IN_CONTAINER, 'false', true)
            settingsStore.updateSetting(SETTINGS.EMBED_WHEN_PATH_TOO_LONG, 'false', true)
        }
    }
})

const embedOptions = computed(() => [
    { value: 'always' as const, label: translate('settings.subtitle.embedModeAlways') },
    { value: 'when_too_long' as const, label: translate('settings.subtitle.embedModeWhenTooLong') },
    { value: 'never' as const, label: translate('settings.subtitle.embedModeNever') }
])

// Summary items for top cards
const settingsSummaryItems = computed(() => [
    {
        label: translate('settings.subtitle.useSubtitleTagging'),
        value: translateBoolean(useSubtitleTagging.value),
        description: translate('settings.subtitle.ocrEnabled') + ': ' + translateBoolean(subtitleOcrEnabled.value)
    },
    {
        label: translate('settings.subtitle.embedMode'),
        value: embedMode.value === 'always'
            ? translate('settings.subtitle.embedModeAlways')
            : embedMode.value === 'when_too_long'
              ? translate('settings.subtitle.embedModeWhenTooLong')
              : translate('settings.subtitle.embedModeNever'),
        description: translate('settings.subtitle.embedModeDescription')
    },
    {
        label: translate('settings.subtitle.stripAssDrawingCommands'),
        value: translateBoolean(
            (settingsStore.getSetting(SETTINGS.STRIP_ASS_DRAWING_COMMANDS) as string) ?? 'true'
        ),
        description: translate('settings.subtitle.stripAssDrawingCommandsDescription')
    },
    {
        label: translate('settings.subtitle.cleanupOrphanedSubtitles'),
        value: translateBoolean(
            (settingsStore.getSetting(SETTINGS.CLEANUP_ORPHANED_SUBTITLES) as string) ?? 'true'
        ),
        description: translate('settings.subtitle.cleanupOrphanedSubtitlesDescription')
    }
])
</script>

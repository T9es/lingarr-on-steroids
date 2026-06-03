<template>
    <CardComponent :title="translate('settings.subtitle.title')">
        <template #description>
            {{ translate('settings.subtitle.description') }}
        </template>
        <template #content>
            <div class="flex flex-col space-y-4">
                <SaveNotification ref="saveNotification" />

                <!-- Skip when target embedded -->
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.skipWhenTargetEmbedded') }}
                    </span>
                    {{ translate('settings.subtitle.skipWhenTargetEmbeddedDescription') }}
                </div>
                <ToggleButton v-model="skipWhenTargetEmbedded">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            skipWhenTargetEmbedded == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.ignoreCaptions') }}
                    </span>
                    {{ translate('settings.subtitle.ignoreCaptionsDescription') }}
                </div>
                <ToggleButton v-model="ignoreCaptions">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            ignoreCaptions == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.translateSupplementalSubtitles') }}
                    </span>
                    {{ translate('settings.subtitle.translateSupplementalSubtitlesDescription') }}
                </div>
                <ToggleButton v-model="translateSupplementalSubtitles">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            translateSupplementalSubtitles == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="border-accent/60 mt-2 flex flex-col space-y-3 border-t pt-4">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.ocrEnabled') }}
                        </span>
                        {{ translate('settings.subtitle.ocrEnabledDescription') }}
                    </div>
                    <ToggleButton v-model="subtitleOcrEnabled">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                subtitleOcrEnabled == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>

                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.ocrAutoQueue') }}
                        </span>
                        {{ translate('settings.subtitle.ocrAutoQueueDescription') }}
                    </div>
                    <ToggleButton v-model="subtitleOcrAutoQueue">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                subtitleOcrAutoQueue == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>

                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.ocrTranslationPrompt') }}
                        </span>
                        {{ translate('settings.subtitle.ocrTranslationPromptDescription') }}
                    </div>
                    <ToggleButton v-model="subtitleOcrTranslationPromptEnabled">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                subtitleOcrTranslationPromptEnabled == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>

                    <InputComponent
                        v-model="subtitleOcrMinQualityScore"
                        validation-type="number"
                        :label="translate('settings.subtitle.ocrMinQualityScore')"
                        :description="translate('settings.subtitle.ocrMinQualityScoreDescription')"
                        @update:validation="(val) => (isValid.subtitleOcrMinQualityScore = val)" />

                    <InputComponent
                        v-model="subtitleOcrLanguages"
                        validation-type="string"
                        :label="translate('settings.subtitle.ocrLanguages')"
                        :description="translate('settings.subtitle.ocrLanguagesDescription')" />
                </div>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.keepAssSsaWithSrt') }}
                    </span>
                    {{ translate('settings.subtitle.keepAssSsaWithSrtDescription') }}
                </div>
                <ToggleButton v-model="keepAssSsaWithSrt">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            keepAssSsaWithSrt == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.fixOverlappingSubtitles') }}
                    </span>
                    {{ translate('settings.subtitle.fixOverlappingSubtitlesDescription') }}
                </div>
                <ToggleButton v-model="fixOverlappingSubtitles">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            fixOverlappingSubtitles == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.stripSubtitleFormatting') }}
                    </span>
                    {{ translate('settings.subtitle.stripSubtitleFormattingDescription') }}
                </div>
                <ToggleButton v-model="stripSubtitleFormatting">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            stripSubtitleFormatting == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.addTranslatorInfo') }}
                    </span>
                    {{ translate('settings.subtitle.addTranslatorInfoDescription') }}
                </div>
                <ToggleButton v-model="addTranslatorInfo">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            addTranslatorInfo == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.stripAssDrawingCommands') }}
                    </span>
                    {{ translate('settings.subtitle.stripAssDrawingCommandsDescription') }}
                </div>
                <ToggleButton v-model="stripAssDrawingCommands">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            stripAssDrawingCommands == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div v-if="stripAssDrawingCommands == 'true'" class="ml-4 flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.cleanSourceAssDrawings') }}
                    </span>
                    {{ translate('settings.subtitle.cleanSourceAssDrawingsDescription') }}
                </div>
                <ToggleButton
                    v-if="stripAssDrawingCommands == 'true'"
                    v-model="cleanSourceAssDrawings">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            cleanSourceAssDrawings == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.removeLanguageTag') }}
                    </span>
                    {{ translate('settings.subtitle.removeLanguageTagDescription') }}
                </div>
                <ToggleButton v-model="removeLanguageTag">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            removeLanguageTag == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <!-- Embed subtitle mode -->
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.embedMode') }}
                    </span>
                    {{ translate('settings.subtitle.embedModeDescription') }}
                </div>
                <SelectComponent v-model:selected="embedMode" :options="embedOptions" />

                <!-- Detect unknown languages -->
                <div class="flex flex-col space-x-2">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.detectUnknownLanguages') }}
                    </span>
                    {{ translate('settings.subtitle.detectUnknownLanguagesDescription') }}
                </div>
                <ToggleButton v-model="detectUnknownLanguages">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            detectUnknownLanguages == 'true'
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                </ToggleButton>

                <div class="flex flex-col space-y-4">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.useSubtitleTagging') }}
                        </span>
                        {{ translate('settings.subtitle.useSubtitleTaggingDescription') }}
                    </div>
                    <ToggleButton v-model="useSubtitleTagging">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                useSubtitleTagging == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                    <InputComponent
                        v-if="useSubtitleTagging == 'true'"
                        v-model="subtitleTag"
                        validation-type="string"
                        :label="translate('settings.subtitle.subtitleTag')"
                        @update:validation="(val) => (isValid.subtitleTag = val)" />
                    <InputComponent
                        v-if="useSubtitleTagging == 'true'"
                        v-model="subtitleTagShort"
                        validation-type="string"
                        :label="translate('settings.subtitle.subtitleTagShort')"
                        :description="translate('settings.subtitle.subtitleTagShortDescription')"
                        @update:validation="(val) => (isValid.subtitleTagShort = val)" />

                    <!-- Orphan Subtitle Cleanup -->
                    <div class="mt-4 flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.cleanupOrphanedSubtitles') }}
                        </span>
                        {{ translate('settings.subtitle.cleanupOrphanedSubtitlesDescription') }}
                    </div>
                    <ToggleButton v-model="cleanupOrphanedSubtitles">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                cleanupOrphanedSubtitles == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                    <div
                        v-if="cleanupOrphanedSubtitles == 'true' && useSubtitleTagging != 'true'"
                        class="bg-warning/20 border-warning text-warning-content rounded-md border p-3 text-sm">
                        ⚠️ {{ translate('settings.subtitle.cleanupRequiresTagging') }}
                    </div>
                </div>

                <div class="border-accent mt-2 flex flex-col space-y-3 border-t pt-4">
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.reconcileOutputs') }}
                        </span>
                        {{ translate('settings.subtitle.reconcileOutputsDescription') }}
                    </div>
                    <button
                        class="border-accent bg-secondary hover:bg-accent/20 inline-flex w-fit items-center rounded-md border px-4 py-2 disabled:cursor-not-allowed disabled:opacity-60"
                        :disabled="isReconcilingOutputs"
                        @click="reconcileOutputs">
                        {{ translate('settings.subtitle.reconcileOutputs') }}
                    </button>

                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.recreateAllOutputs') }}
                        </span>
                        {{ translate('settings.subtitle.recreateAllOutputsDescription') }}
                    </div>
                    <button
                        class="border-accent bg-secondary hover:bg-accent/20 inline-flex w-fit items-center rounded-md border px-4 py-2 disabled:cursor-not-allowed disabled:opacity-60"
                        :disabled="isRecreatingAll"
                        @click="recreateAllOutputs">
                        {{ translate('settings.subtitle.recreateAllOutputs') }}
                    </button>
                </div>
            </div>
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue'
import { SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'

import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'

const { translate } = useI18n()
const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const isRecreatingAll = ref(false)
const isReconcilingOutputs = ref(false)
const isValid = reactive({
    subtitleTag: true,
    subtitleTagShort: true,
    subtitleOcrMinQualityScore: true
})

const keepAssSsaWithSrt = computed({
    get: (): string =>
        settingsStore.getSetting(SETTINGS.SUBTITLE_OUTPUT_MODE) === 'both' ? 'true' : 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_OUTPUT_MODE,
            newValue === 'true' ? 'both' : 'match-source',
            true
        )
        saveNotification.value?.show()
    }
})

const skipWhenTargetEmbedded = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SKIP_WHEN_TARGET_EMBEDDED) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SKIP_WHEN_TARGET_EMBEDDED, newValue, true)
        saveNotification.value?.show()
    }
})

const ignoreCaptions = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.IGNORE_CAPTIONS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.IGNORE_CAPTIONS, newValue, true)
        saveNotification.value?.show()
    }
})

const translateSupplementalSubtitles = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.TRANSLATE_SUPPLEMENTAL_SUBTITLES) as string) ?? 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.TRANSLATE_SUPPLEMENTAL_SUBTITLES, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleOcrEnabled = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.SUBTITLE_OCR_ENABLED) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_OCR_ENABLED, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleOcrAutoQueue = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SUBTITLE_OCR_AUTO_QUEUE) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_OCR_AUTO_QUEUE, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleOcrTranslationPromptEnabled = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SUBTITLE_OCR_TRANSLATION_PROMPT_ENABLED) as string) ??
        'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_OCR_TRANSLATION_PROMPT_ENABLED,
            newValue,
            true
        )
        saveNotification.value?.show()
    }
})

const subtitleOcrMinQualityScore = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SUBTITLE_OCR_MIN_QUALITY_SCORE) as string) ?? '80',
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_OCR_MIN_QUALITY_SCORE,
            newValue,
            isValid.subtitleOcrMinQualityScore
        )
        saveNotification.value?.show()
    }
})

const subtitleOcrLanguages = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.SUBTITLE_OCR_LANGUAGES) as string) ?? 'auto',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_OCR_LANGUAGES, newValue || 'auto', true)
        saveNotification.value?.show()
    }
})

const fixOverlappingSubtitles = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES, newValue, true)
        saveNotification.value?.show()
    }
})

const stripSubtitleFormatting = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING, newValue, true)
        saveNotification.value?.show()
    }
})

const addTranslatorInfo = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.ADD_TRANSLATOR_INFO) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.ADD_TRANSLATOR_INFO, newValue, true)
        saveNotification.value?.show()
    }
})

const removeLanguageTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.REMOVE_LANGUAGE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.REMOVE_LANGUAGE_TAG, newValue, true)
        saveNotification.value?.show()
    }
})

const useSubtitleTagging = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_SUBTITLE_TAGGING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_SUBTITLE_TAGGING, newValue, true)
        saveNotification.value?.show()
    }
})

const subtitleTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG, newValue, isValid.subtitleTag)
        saveNotification.value?.show()
    }
})

const subtitleTagShort = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG_SHORT) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG_SHORT, newValue, isValid.subtitleTagShort)
        saveNotification.value?.show()
    }
})

const cleanupOrphanedSubtitles = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.CLEANUP_ORPHANED_SUBTITLES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.CLEANUP_ORPHANED_SUBTITLES, newValue, true)
        saveNotification.value?.show()
    }
})

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
        saveNotification.value?.show()
    }
})
const embedOptions = computed(() => [
    { value: 'always' as const, label: translate('settings.subtitle.embedModeAlways') },
    { value: 'when_too_long' as const, label: translate('settings.subtitle.embedModeWhenTooLong') },
    { value: 'never' as const, label: translate('settings.subtitle.embedModeNever') }
])

const detectUnknownLanguages = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.DETECT_UNKNOWN_LANGUAGES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.DETECT_UNKNOWN_LANGUAGES, newValue, true)
        saveNotification.value?.show()
    }
})

const stripAssDrawingCommands = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.STRIP_ASS_DRAWING_COMMANDS) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_ASS_DRAWING_COMMANDS, newValue, true)
        // If disabling, also disable the dependent setting
        if (newValue === 'false') {
            settingsStore.updateSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS, 'false', true)
        }
        saveNotification.value?.show()
    }
})

const cleanSourceAssDrawings = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS) as string) ?? 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS, newValue, true)
        saveNotification.value?.show()
    }
})

const reconcileOutputs = async () => {
    if (isReconcilingOutputs.value) {
        return
    }

    isReconcilingOutputs.value = true
    try {
        await services.translate.reconcileOutputs()
    } catch (error) {
        console.error('Failed to reconcile subtitle outputs:', error)
    } finally {
        isReconcilingOutputs.value = false
    }
}

const recreateAllOutputs = async () => {
    if (isRecreatingAll.value) {
        return
    }

    isRecreatingAll.value = true
    try {
        await services.translate.recreateAllMedia()
    } catch (error) {
        console.error('Failed to recreate all outputs:', error)
    } finally {
        isRecreatingAll.value = false
    }
}
</script>

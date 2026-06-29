<template>
    <CardComponent :title="translate('settings.subtitle.title')">
        <template #description>
            {{ translate('settings.subtitle.description') }}
        </template>
        <template #content>
            <div class="flex flex-col space-y-8">
                <SaveNotification ref="saveNotification" />

                <!-- Group: Source Selection -->
                <section>
                    <h3
                        class="text-primary-content mb-3 text-sm font-semibold uppercase tracking-wider opacity-70">
                        {{ translate('settings.subtitle.groupSourceSelection') }}
                    </h3>
                    <div class="flex flex-col">
                        <SettingRow
                            :label="translate('settings.subtitle.skipWhenTargetEmbedded')"
                            :description="
                                translate('settings.subtitle.skipWhenTargetEmbeddedDescription')
                            ">
                            <ToggleButton v-model="skipWhenTargetEmbedded">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        skipWhenTargetEmbedded == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.ignoreCaptions')"
                            :description="translate('settings.subtitle.ignoreCaptionsDescription')">
                            <ToggleButton v-model="ignoreCaptions">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        ignoreCaptions == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.translateSupplementalSubtitles')"
                            :description="
                                translate('settings.subtitle.translateSupplementalSubtitlesDescription')
                            ">
                            <ToggleButton v-model="translateSupplementalSubtitles">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        translateSupplementalSubtitles == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.detectUnknownLanguages')"
                            :description="
                                translate('settings.subtitle.detectUnknownLanguagesDescription')
                            ">
                            <ToggleButton v-model="detectUnknownLanguages">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        detectUnknownLanguages == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                    </div>
                </section>

                <!-- Group: OCR (Bitmap Subtitles) -->
                <section>
                    <h3
                        class="text-primary-content mb-3 text-sm font-semibold uppercase tracking-wider opacity-70">
                        {{ translate('settings.subtitle.groupOcr') }}
                    </h3>
                    <div class="flex flex-col">
                        <SettingRow
                            :label="translate('settings.subtitle.ocrEnabled')"
                            :description="translate('settings.subtitle.ocrEnabledDescription')">
                            <ToggleButton v-model="subtitleOcrEnabled">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        subtitleOcrEnabled == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.ocrAutoQueue')"
                            :description="translate('settings.subtitle.ocrAutoQueueDescription')">
                            <ToggleButton v-model="subtitleOcrAutoQueue">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        subtitleOcrAutoQueue == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.ocrTranslationPrompt')"
                            :description="
                                translate('settings.subtitle.ocrTranslationPromptDescription')
                            ">
                            <ToggleButton v-model="subtitleOcrTranslationPromptEnabled">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        subtitleOcrTranslationPromptEnabled == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                    </div>
                    <div class="mt-4 flex flex-col gap-4">
                        <InputComponent
                            v-model="subtitleOcrMinQualityScore"
                            validation-type="number"
                            :label="translate('settings.subtitle.ocrMinQualityScore')"
                            :description="translate('settings.subtitle.ocrMinQualityScoreDescription')"
                            @update:validation="
                                (val) => (isValid.subtitleOcrMinQualityScore = val)
                            " />
                        <InputComponent
                            v-model="subtitleOcrLanguages"
                            validation-type="string"
                            :label="translate('settings.subtitle.ocrLanguages')"
                            :description="translate('settings.subtitle.ocrLanguagesDescription')" />
                    </div>
                </section>

                <!-- Group: Format & Cleaning -->
                <section>
                    <h3
                        class="text-primary-content mb-3 text-sm font-semibold uppercase tracking-wider opacity-70">
                        {{ translate('settings.subtitle.groupFormatCleaning') }}
                    </h3>
                    <div class="flex flex-col">
                        <SettingRow
                            :label="translate('settings.subtitle.keepAssSsaWithSrt')"
                            :description="translate('settings.subtitle.keepAssSsaWithSrtDescription')">
                            <ToggleButton v-model="keepAssSsaWithSrt">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        keepAssSsaWithSrt == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.fixOverlappingSubtitles')"
                            :description="
                                translate('settings.subtitle.fixOverlappingSubtitlesDescription')
                            ">
                            <ToggleButton v-model="fixOverlappingSubtitles">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        fixOverlappingSubtitles == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.stripSubtitleFormatting')"
                            :description="
                                translate('settings.subtitle.stripSubtitleFormattingDescription')
                            ">
                            <ToggleButton v-model="stripSubtitleFormatting">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        stripSubtitleFormatting == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.addTranslatorInfo')"
                            :description="translate('settings.subtitle.addTranslatorInfoDescription')">
                            <ToggleButton v-model="addTranslatorInfo">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        addTranslatorInfo == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.stripAssDrawingCommands')"
                            :description="
                                translate('settings.subtitle.stripAssDrawingCommandsDescription')
                            ">
                            <ToggleButton v-model="stripAssDrawingCommands">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        stripAssDrawingCommands == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            v-if="stripAssDrawingCommands == 'true'"
                            :label="translate('settings.subtitle.cleanSourceAssDrawings')"
                            :description="
                                translate('settings.subtitle.cleanSourceAssDrawingsDescription')
                            "
                            nested>
                            <ToggleButton v-model="cleanSourceAssDrawings">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        cleanSourceAssDrawings == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.removeLanguageTag')"
                            :description="translate('settings.subtitle.removeLanguageTagDescription')">
                            <ToggleButton v-model="removeLanguageTag">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        removeLanguageTag == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                    </div>
                </section>

                <!-- Group: Tagging & Identification -->
                <section>
                    <h3
                        class="text-primary-content mb-3 text-sm font-semibold uppercase tracking-wider opacity-70">
                        {{ translate('settings.subtitle.groupTagging') }}
                    </h3>
                    <div class="flex flex-col">
                        <SettingRow
                            :label="translate('settings.subtitle.useSubtitleTagging')"
                            :description="
                                translate('settings.subtitle.useSubtitleTaggingDescription')
                            ">
                            <ToggleButton v-model="useSubtitleTagging">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        useSubtitleTagging == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <div
                            v-if="cleanupOrphanedSubtitles == 'true' && useSubtitleTagging != 'true'"
                            class="bg-warning/20 border-warning text-warning-content mt-2 rounded-md border p-3 text-sm">
                            ⚠️ {{ translate('settings.subtitle.cleanupRequiresTagging') }}
                        </div>
                    </div>
                    <div v-if="useSubtitleTagging == 'true'" class="mt-4 flex flex-col gap-4">
                        <InputComponent
                            v-model="subtitleTag"
                            validation-type="string"
                            :label="translate('settings.subtitle.subtitleTag')"
                            @update:validation="(val) => (isValid.subtitleTag = val)" />
                        <InputComponent
                            v-model="subtitleTagShort"
                            validation-type="string"
                            :label="translate('settings.subtitle.subtitleTagShort')"
                            :description="translate('settings.subtitle.subtitleTagShortDescription')"
                            @update:validation="(val) => (isValid.subtitleTagShort = val)" />
                    </div>
                    <div class="mt-6 flex flex-col">
                        <SettingRow
                            :label="translate('settings.subtitle.cleanupOrphanedSubtitles')"
                            :description="
                                translate('settings.subtitle.cleanupOrphanedSubtitlesDescription')
                            ">
                            <ToggleButton v-model="cleanupOrphanedSubtitles">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        cleanupOrphanedSubtitles == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                    </div>
                </section>

                <!-- Group: Output & Embedding -->
                <section>
                    <h3
                        class="text-primary-content mb-3 text-sm font-semibold uppercase tracking-wider opacity-70">
                        {{ translate('settings.subtitle.groupOutputEmbedding') }}
                    </h3>
                    <div class="flex flex-col">
                        <SettingRow
                            :label="translate('settings.subtitle.embedMode')"
                            :description="translate('settings.subtitle.embedModeDescription')">
                            <SelectComponent v-model:selected="embedMode" :options="embedOptions" />
                        </SettingRow>
                    </div>
                    <div class="border-accent mt-6 flex flex-col gap-4 border-t pt-4">
                        <SettingRow
                            :label="translate('settings.subtitle.reconcileOutputs')"
                            :description="translate('settings.subtitle.reconcileOutputsDescription')">
                            <button
                                class="border-accent bg-secondary hover:bg-accent/20 inline-flex items-center rounded-md border px-4 py-2 disabled:cursor-not-allowed disabled:opacity-60"
                                :disabled="isReconcilingOutputs"
                                @click="reconcileOutputs">
                                {{ translate('settings.subtitle.reconcileOutputs') }}
                            </button>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.subtitle.recreateAllOutputs')"
                            :description="translate('settings.subtitle.recreateAllOutputsDescription')">
                            <button
                                class="border-accent bg-secondary hover:bg-accent/20 inline-flex items-center rounded-md border px-4 py-2 disabled:cursor-not-allowed disabled:opacity-60"
                                :disabled="isRecreatingAll"
                                @click="recreateAllOutputs">
                                {{ translate('settings.subtitle.recreateAllOutputs') }}
                            </button>
                        </SettingRow>
                    </div>
                </section>

                <!-- Group: Validation -->
                <section>
                    <h3
                        class="text-primary-content mb-3 text-sm font-semibold uppercase tracking-wider opacity-70">
                        {{ translate('settings.subtitle.groupValidation') }}
                    </h3>
                    <div class="flex flex-col">
                        <SettingRow
                            :label="translate('settings.validation.enabled')"
                            :description="translate('settings.validation.description')">
                            <ToggleButton v-model="validationEnabled">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        validationEnabled == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                        <SettingRow
                            :label="translate('settings.validation.integrityEnabled')"
                            :description="translate('settings.validation.integrityDescription')">
                            <ToggleButton v-model="integrityValidationEnabled">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        integrityValidationEnabled == 'true'
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </SettingRow>
                    </div>
                    <div v-if="validationEnabled == 'true'" class="mt-4 flex flex-col gap-4">
                        <InputComponent
                            v-model="minDurationMs"
                            validation-type="number"
                            :label="translate('settings.validation.minDurationMs')"
                            @update:validation="(val) => (isValid.minDurationMs = val)">
                            <div class="flex flex-wrap gap-2">
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="minDurationMs = '100'">
                                    0.2s
                                </button>
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="minDurationMs = '500'">
                                    0.5s
                                </button>
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="minDurationMs = '1000'">
                                    1s
                                </button>
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="minDurationMs = '1500'">
                                    1.5s
                                </button>
                            </div>
                        </InputComponent>
                        <InputComponent
                            v-model="maxDurationSecs"
                            validation-type="number"
                            :label="translate('settings.validation.maxDurationSecs')"
                            @update:validation="(val) => (isValid.maxDurationSecs = val)" />
                        <InputComponent
                            v-model="minSubtitleLength"
                            validation-type="number"
                            :label="translate('settings.validation.minSubtitleLength')"
                            @update:validation="(val) => (isValid.minSubtitleLength = val)" />
                        <InputComponent
                            v-model="maxSubtitleLength"
                            validation-type="number"
                            :label="translate('settings.validation.maxSubtitleLength')"
                            @update:validation="(val) => (isValid.maxSubtitleLength = val)" />
                        <InputComponent
                            v-model="maxFileSizeBytes"
                            validation-type="number"
                            :label="translate('settings.validation.maxFileSizeBytes')"
                            @update:validation="(val) => (isValid.maxFileSizeBytes = val)">
                            <div class="flex flex-wrap gap-2">
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="maxFileSizeBytes = (512 * 1024).toString()">
                                    0.5 KB
                                </button>
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="maxFileSizeBytes = (1024 * 1024).toString()">
                                    1 MB
                                </button>
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="maxFileSizeBytes = (1.5 * 1024 * 1024).toString()">
                                    1.5 MB
                                </button>
                                <button
                                    type="button"
                                    class="border-accent hover:bg-accent hover:text-primary-content cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    @click="maxFileSizeBytes = (2 * 1024 * 1024).toString()">
                                    2 MB
                                </button>
                            </div>
                        </InputComponent>
                    </div>
                </section>
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
import SettingRow from '@/components/common/SettingRow.vue'

const { translate } = useI18n()
const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const isRecreatingAll = ref(false)
const isReconcilingOutputs = ref(false)
const isValid = reactive({
    subtitleTag: true,
    subtitleTagShort: true,
    subtitleOcrMinQualityScore: true,
    maxDurationSecs: true,
    minDurationMs: true,
    minSubtitleLength: true,
    maxSubtitleLength: true,
    maxFileSizeBytes: true
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
        (settingsStore.getSetting(SETTINGS.SUBTITLE_OCR_MIN_QUALITY_SCORE) as string) ?? '70',
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
        (settingsStore.getSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS) as string) ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.CLEAN_SOURCE_ASS_DRAWINGS, newValue, true)
        saveNotification.value?.show()
    }
})

const validationEnabled = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_VALIDATION_ENABLED) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_VALIDATION_ENABLED, newValue, true)
        saveNotification.value?.show()
    }
})

const maxDurationSecs = computed({
    get: () => settingsStore.getSetting(SETTINGS.SUBTITLE_VALIDATION_MAXDURATIONSECS) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_VALIDATION_MAXDURATIONSECS,
            newValue,
            isValid.maxDurationSecs
        )
        if (isValid.maxDurationSecs) {
            saveNotification.value?.show()
        }
    }
})

const minDurationMs = computed({
    get: () => settingsStore.getSetting(SETTINGS.SUBTITLE_VALIDATION_MINDURATIONMS) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_VALIDATION_MINDURATIONMS,
            newValue,
            isValid.minDurationMs
        )
        if (isValid.minDurationMs) {
            saveNotification.value?.show()
        }
    }
})

const minSubtitleLength = computed({
    get: () => settingsStore.getSetting(SETTINGS.SUBTITLE_VALIDATION_MINSUBTITLELENGTH) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_VALIDATION_MINSUBTITLELENGTH,
            newValue,
            isValid.minSubtitleLength
        )
        if (isValid.minSubtitleLength) {
            saveNotification.value?.show()
        }
    }
})

const maxSubtitleLength = computed({
    get: () => settingsStore.getSetting(SETTINGS.SUBTITLE_VALIDATION_MAXSUBTITLELENGTH) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_VALIDATION_MAXSUBTITLELENGTH,
            newValue,
            isValid.maxSubtitleLength
        )
        if (isValid.maxSubtitleLength) {
            saveNotification.value?.show()
        }
    }
})

const maxFileSizeBytes = computed({
    get: () => settingsStore.getSetting(SETTINGS.SUBTITLE_VALIDATION_MAXFILESIZEBYTES) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(
            SETTINGS.SUBTITLE_VALIDATION_MAXFILESIZEBYTES,
            newValue,
            isValid.maxFileSizeBytes
        )
        if (isValid.maxFileSizeBytes) {
            saveNotification.value?.show()
        }
    }
})

const integrityValidationEnabled = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.SUBTITLE_INTEGRITY_VALIDATION_ENABLED) as string) ??
        'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_INTEGRITY_VALIDATION_ENABLED, newValue, true)
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

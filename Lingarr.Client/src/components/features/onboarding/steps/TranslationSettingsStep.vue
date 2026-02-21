<template>
    <div class="space-y-6">
        <!-- Batch Translation Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('settings.translation.title') }}
            </h3>
            <p class="text-secondary-content mb-4 text-sm">
                {{ translate('settings.translation.description') }}
            </p>

            <div class="grid grid-cols-2 gap-4">
                <!-- Use Batch Translation -->
                <div class="flex items-center justify-between">
                    <div>
                        <span class="font-semibold">
                            {{ translate('settings.translation.useBatchTranslation') }}
                        </span>
                        <p class="text-secondary-content text-xs">
                            {{ translate('settings.translation.useBatchTranslationDescription') }}
                        </p>
                    </div>
                    <ToggleButton v-model="useBatchTranslation">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                useBatchTranslation === 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                </div>

                <!-- Max Batch Size -->
                <div v-if="useBatchTranslation === 'true'">
                    <span class="font-semibold">
                        {{ translate('settings.translation.maxBatchSize') }}
                    </span>
                    <p class="text-secondary-content text-xs">
                        {{ translate('settings.translation.maxBatchSizeDescription') }}
                    </p>
                    <InputComponent
                        v-model="maxBatchSize"
                        validation-type="number"
                        placeholder="50"
                        @update:validation="(val) => (isValid.maxBatchSize = val)" />
                </div>

                <!-- Batch Retry Mode -->
                <div v-if="useBatchTranslation === 'true'">
                    <span class="font-semibold">
                        {{ translate('settings.translation.batchRetryMode') }}
                    </span>
                    <p class="text-secondary-content text-xs">
                        {{ translate('settings.translation.batchRetryModeDescription') }}
                    </p>
                    <select
                        v-model="batchRetryMode"
                        class="border-accent bg-primary text-primary-content focus:ring-accent mt-2 h-10 w-full cursor-pointer rounded-md border px-3 py-2 focus:ring-2 focus:outline-none">
                        <option value="deferred">
                            {{ translate('settings.translation.batchRetryModeDeferred') }}
                        </option>
                        <option value="immediate">
                            {{ translate('settings.translation.batchRetryModeImmediate') }}
                        </option>
                    </select>
                </div>
            </div>
        </div>

        <!-- Retry Settings Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                Retry Settings
            </h3>
            <p class="text-secondary-content mb-4 text-sm">
                Configure how failed translations are retried.
            </p>

            <div class="grid grid-cols-3 gap-4">
                <!-- Max Retries -->
                <div>
                    <span class="font-semibold">
                        {{ translate('settings.translation.maxRetries') }}
                    </span>
                    <p class="text-secondary-content text-xs">
                        {{ translate('settings.translation.maxRetriesDescription') }}
                    </p>
                    <InputComponent
                        v-model="maxRetries"
                        validation-type="number"
                        placeholder="3"
                        @update:validation="(val) => (isValid.maxRetries = val)" />
                </div>

                <!-- Retry Delay -->
                <div>
                    <span class="font-semibold">
                        {{ translate('settings.translation.retryDelay') }}
                    </span>
                    <p class="text-secondary-content text-xs">
                        {{ translate('settings.translation.retryDelayDescription') }}
                    </p>
                    <InputComponent
                        v-model="retryDelay"
                        validation-type="number"
                        placeholder="1000"
                        @update:validation="(val) => (isValid.retryDelay = val)" />
                </div>

                <!-- Retry Delay Multiplier -->
                <div>
                    <span class="font-semibold">
                        {{ translate('settings.translation.retryDelayMultiplier') }}
                    </span>
                    <p class="text-secondary-content text-xs">
                        {{ translate('settings.translation.retryDelayMultiplierDescription') }}
                    </p>
                    <InputComponent
                        v-model="retryDelayMultiplier"
                        validation-type="number"
                        placeholder="2"
                        @update:validation="(val) => (isValid.retryDelayMultiplier = val)" />
                </div>
            </div>
        </div>

        <!-- Parallel Translations Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                Parallel Translations
            </h3>
            <p class="text-secondary-content mb-4 text-sm">
                Control how many translations can run simultaneously.
            </p>

            <div class="grid grid-cols-2 gap-4">
                <!-- Max Parallel Translations -->
                <div>
                    <span class="font-semibold">
                        {{ translate('settings.translation.maxParallelTranslations') }}
                    </span>
                    <p class="text-secondary-content text-xs">
                        {{ translate('settings.translation.maxParallelTranslationsDescription') }}
                    </p>
                    <span v-if="maxConcurrentLimit" class="text-secondary-content/60 text-xs">
                        {{
                            translate('settings.translation.maxParallelTranslationsLimit').format({
                                max: maxConcurrentLimit
                            })
                        }}
                    </span>
                    <InputComponent
                        v-model="maxParallelTranslations"
                        validation-type="number"
                        placeholder="1"
                        :max="maxConcurrentLimit"
                        @update:validation="(val) => (isValid.maxParallelTranslations = val)" />
                </div>
            </div>
        </div>

        <!-- Subtitle Processing Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('settings.subtitle.title') }}
            </h3>
            <p class="text-secondary-content mb-4 text-sm">
                {{ translate('settings.subtitle.description') }}
            </p>

            <div class="grid grid-cols-2 gap-4">
                <!-- Fix Overlapping Subtitles -->
                <div class="flex items-center justify-between">
                    <div>
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.fixOverlappingSubtitles') }}
                        </span>
                        <p class="text-secondary-content text-xs">
                            {{ translate('settings.subtitle.fixOverlappingSubtitlesDescription') }}
                        </p>
                    </div>
                    <ToggleButton v-model="fixOverlappingSubtitles">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                fixOverlappingSubtitles === 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                </div>

                <!-- Strip Subtitle Formatting -->
                <div class="flex items-center justify-between">
                    <div>
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.stripSubtitleFormatting') }}
                        </span>
                        <p class="text-secondary-content text-xs">
                            {{ translate('settings.subtitle.stripSubtitleFormattingDescription') }}
                        </p>
                    </div>
                    <ToggleButton v-model="stripSubtitleFormatting">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                stripSubtitleFormatting === 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                </div>

                <!-- Add Translator Info -->
                <div class="flex items-center justify-between">
                    <div>
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.addTranslatorInfo') }}
                        </span>
                        <p class="text-secondary-content text-xs">
                            {{ translate('settings.subtitle.addTranslatorInfoDescription') }}
                        </p>
                    </div>
                    <ToggleButton v-model="addTranslatorInfo">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                addTranslatorInfo === 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                </div>
            </div>
        </div>

        <!-- Subtitle Tagging Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                Subtitle Tagging
            </h3>
            <p class="text-secondary-content mb-4 text-sm">
                Tag translated subtitles to identify them in your media player.
            </p>

            <div class="grid grid-cols-2 gap-4">
                <!-- Use Subtitle Tagging -->
                <div class="flex items-center justify-between">
                    <div>
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.useSubtitleTagging') }}
                        </span>
                        <p class="text-secondary-content text-xs">
                            {{ translate('settings.subtitle.useSubtitleTaggingDescription') }}
                        </p>
                    </div>
                    <ToggleButton v-model="useSubtitleTagging">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                useSubtitleTagging === 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>
                </div>

                <!-- Subtitle Tag -->
                <div v-if="useSubtitleTagging === 'true'">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.subtitleTag') }}
                    </span>
                    <InputComponent
                        v-model="subtitleTag"
                        validation-type="string"
                        placeholder="AI"
                        @update:validation="(val) => (isValid.subtitleTag = val)" />
                </div>

                <!-- Subtitle Tag Short -->
                <div v-if="useSubtitleTagging === 'true'">
                    <span class="font-semibold">
                        {{ translate('settings.subtitle.subtitleTagShort') }}
                    </span>
                    <p class="text-secondary-content text-xs">
                        {{ translate('settings.subtitle.subtitleTagShortDescription') }}
                    </p>
                    <InputComponent
                        v-model="subtitleTagShort"
                        validation-type="string"
                        placeholder="AI"
                        @update:validation="(val) => (isValid.subtitleTagShort = val)" />
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, reactive, onMounted } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import InputComponent from '@/components/common/InputComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import { useI18n } from '@/plugins/i18n'
import axios from 'axios'

const { translate } = useI18n()
const settingsStore = useSettingStore()
const maxConcurrentLimit = ref<number>(20)

const isValid = reactive({
    maxBatchSize: true,
    maxRetries: true,
    retryDelay: true,
    retryDelayMultiplier: true,
    maxParallelTranslations: true,
    subtitleTag: true,
    subtitleTagShort: true
})

onMounted(async () => {
    try {
        const response = await axios.get<{ maxConcurrentTranslations: number }>(
            '/api/setting/system/limits'
        )
        maxConcurrentLimit.value = response.data.maxConcurrentTranslations
    } catch (error) {
        console.error('Failed to fetch system limits:', error)
    }
})

// Batch Translation Settings
const useBatchTranslation = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_BATCH_TRANSLATION) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_BATCH_TRANSLATION, newValue, true)
    }
})

const maxBatchSize = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_BATCH_SIZE) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_BATCH_SIZE, newValue, isValid.maxBatchSize)
    }
})

const batchRetryMode = computed({
    get: (): string =>
        (settingsStore.getSetting(SETTINGS.BATCH_RETRY_MODE) as string) ?? 'deferred',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.BATCH_RETRY_MODE, newValue, true)
    }
})

// Retry Settings
const maxRetries = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_RETRIES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_RETRIES, newValue, isValid.maxRetries)
    }
})

const retryDelay = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RETRY_DELAY) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.RETRY_DELAY, newValue, isValid.retryDelay)
    }
})

const retryDelayMultiplier = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RETRY_DELAY_MULTIPLIER) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.RETRY_DELAY_MULTIPLIER,
            newValue,
            isValid.retryDelayMultiplier
        )
    }
})

// Parallel Translations
const maxParallelTranslations = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_PARALLEL_TRANSLATIONS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.MAX_PARALLEL_TRANSLATIONS,
            newValue,
            isValid.maxParallelTranslations
        )
    }
})

// Subtitle Processing Settings
const fixOverlappingSubtitles = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.FIX_OVERLAPPING_SUBTITLES, newValue, true)
    }
})

const stripSubtitleFormatting = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.STRIP_SUBTITLE_FORMATTING, newValue, true)
    }
})

const addTranslatorInfo = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.ADD_TRANSLATOR_INFO) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.ADD_TRANSLATOR_INFO, newValue, true)
    }
})

// Subtitle Tagging Settings
const useSubtitleTagging = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_SUBTITLE_TAGGING) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_SUBTITLE_TAGGING, newValue, true)
    }
})

const subtitleTag = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG, newValue, isValid.subtitleTag)
    }
})

const subtitleTagShort = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SUBTITLE_TAG_SHORT) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_TAG_SHORT, newValue, isValid.subtitleTagShort)
    }
})
</script>

<template>
    <div class="space-y-4 lg:space-y-5">
        <p class="text-secondary-content -mt-1 text-sm leading-6">
            {{ translate('settings.translation.description') }}
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

        <details class="group bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border">
            <summary
                class="bg-secondary/40 hover:bg-secondary/60 flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-4 sm:px-5">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('settings.translation.title') }}
                    </span>
                    <span class="text-secondary-content text-sm">
                        {{ translateBoolean(useBatchTranslation) }}
                    </span>
                </div>
                <CaretRightIcon
                    class="text-secondary-content h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
            </summary>
            <div class="space-y-4 px-4 py-4 sm:px-5">
                <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                    <div
                        class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4 md:col-span-2 xl:col-span-1">
                        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                            <div class="min-w-0">
                                <span class="text-primary-content font-semibold">
                                    {{ translate('settings.translation.useBatchTranslation') }}
                                </span>
                                <p class="text-secondary-content text-xs leading-5">
                                    {{
                                        translate('settings.translation.useBatchTranslationDescription')
                                    }}
                                </p>
                            </div>
                            <ToggleButton v-model="useBatchTranslation">
                                <span class="text-primary-content text-sm font-medium">
                                    {{ translateBoolean(useBatchTranslation) }}
                                </span>
                            </ToggleButton>
                        </div>
                    </div>

                    <div
                        v-if="useBatchTranslation === 'true'"
                        class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.translation.maxBatchSize') }}
                        </span>
                        <p class="text-secondary-content text-xs leading-5">
                            {{ translate('settings.translation.maxBatchSizeDescription') }}
                        </p>
                        <InputComponent
                            v-model="maxBatchSize"
                            validation-type="number"
                            placeholder="50"
                            @update:validation="(val) => (isValid.maxBatchSize = val)" />
                    </div>

                    <div
                        v-if="useBatchTranslation === 'true'"
                        class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.translation.batchRetryMode') }}
                        </span>
                        <p class="text-secondary-content text-xs leading-5">
                            {{ translate('settings.translation.batchRetryModeDescription') }}
                        </p>
                        <select
                            v-model="batchRetryMode"
                            class="border-accent bg-primary text-primary-content focus:ring-accent mt-2 h-10 w-full min-w-0 cursor-pointer rounded-md border px-3 py-2 focus:ring-2 focus:outline-none">
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
        </details>

        <details class="group bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border">
            <summary
                class="bg-secondary/40 hover:bg-secondary/60 flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-4 sm:px-5">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('onboarding.settings.retryTitle') }}
                    </span>
                    <span class="text-secondary-content text-sm">
                        {{ maxRetries }} {{ translate('onboarding.settings.retries') }}
                    </span>
                </div>
                <CaretRightIcon
                    class="text-secondary-content h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
            </summary>
            <div class="space-y-4 px-4 py-4 sm:px-5">
                <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                    <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.translation.maxRetries') }}
                        </span>
                        <p class="text-secondary-content text-xs leading-5">
                            {{ translate('settings.translation.maxRetriesDescription') }}
                        </p>
                        <InputComponent
                            v-model="maxRetries"
                            validation-type="number"
                            placeholder="3"
                            @update:validation="(val) => (isValid.maxRetries = val)" />
                    </div>

                    <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.translation.retryDelay') }}
                        </span>
                        <p class="text-secondary-content text-xs leading-5">
                            {{ translate('settings.translation.retryDelayDescription') }}
                        </p>
                        <InputComponent
                            v-model="retryDelay"
                            validation-type="number"
                            placeholder="1000"
                            @update:validation="(val) => (isValid.retryDelay = val)" />
                    </div>

                    <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.translation.retryDelayMultiplier') }}
                        </span>
                        <p class="text-secondary-content text-xs leading-5">
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
        </details>

        <details class="group bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border">
            <summary
                class="bg-secondary/40 hover:bg-secondary/60 flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-4 sm:px-5">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('onboarding.settings.parallelTitle') }}
                    </span>
                    <span class="text-secondary-content text-sm">
                        {{ maxParallelTranslations }}
                        {{ translate('onboarding.settings.concurrent') }}
                    </span>
                </div>
                <CaretRightIcon
                    class="text-secondary-content h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
            </summary>
            <div class="space-y-4 px-4 py-4 sm:px-5">
                <div class="grid grid-cols-1 gap-4">
                    <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.translation.maxParallelTranslations') }}
                        </span>
                        <p class="text-secondary-content text-xs leading-5">
                            {{ translate('settings.translation.maxParallelTranslationsDescription') }}
                        </p>
                        <span v-if="maxConcurrentLimit" class="text-secondary-content/70 text-xs leading-5">
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
        </details>

        <details class="group bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border">
            <summary
                class="bg-secondary/40 hover:bg-secondary/60 flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-4 sm:px-5">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('settings.subtitle.title') }}
                    </span>
                    <span class="text-secondary-content text-sm">
                        {{ enabledSubtitleProcessingCount }}/3
                    </span>
                </div>
                <CaretRightIcon
                    class="text-secondary-content h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
            </summary>
            <div class="space-y-4 px-4 py-4 sm:px-5">
                <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                    <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                            <div class="min-w-0">
                                <span class="text-primary-content font-semibold">
                                    {{ translate('settings.subtitle.fixOverlappingSubtitles') }}
                                </span>
                                <p class="text-secondary-content text-xs leading-5">
                                    {{
                                        translate(
                                            'settings.subtitle.fixOverlappingSubtitlesDescription'
                                        )
                                    }}
                                </p>
                            </div>
                            <ToggleButton v-model="fixOverlappingSubtitles">
                                <span class="text-primary-content text-sm font-medium">
                                    {{ translateBoolean(fixOverlappingSubtitles) }}
                                </span>
                            </ToggleButton>
                        </div>
                    </div>

                    <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                            <div class="min-w-0">
                                <span class="text-primary-content font-semibold">
                                    {{ translate('settings.subtitle.stripSubtitleFormatting') }}
                                </span>
                                <p class="text-secondary-content text-xs leading-5">
                                    {{
                                        translate(
                                            'settings.subtitle.stripSubtitleFormattingDescription'
                                        )
                                    }}
                                </p>
                            </div>
                            <ToggleButton v-model="stripSubtitleFormatting">
                                <span class="text-primary-content text-sm font-medium">
                                    {{ translateBoolean(stripSubtitleFormatting) }}
                                </span>
                            </ToggleButton>
                        </div>
                    </div>

                    <div class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                            <div class="min-w-0">
                                <span class="text-primary-content font-semibold">
                                    {{ translate('settings.subtitle.addTranslatorInfo') }}
                                </span>
                                <p class="text-secondary-content text-xs leading-5">
                                    {{ translate('settings.subtitle.addTranslatorInfoDescription') }}
                                </p>
                            </div>
                            <ToggleButton v-model="addTranslatorInfo">
                                <span class="text-primary-content text-sm font-medium">
                                    {{ translateBoolean(addTranslatorInfo) }}
                                </span>
                            </ToggleButton>
                        </div>
                    </div>
                </div>
            </div>
        </details>

        <details class="group bg-primary/35 border-secondary/30 overflow-hidden rounded-2xl border">
            <summary
                class="bg-secondary/40 hover:bg-secondary/60 flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-4 sm:px-5">
                <div class="min-w-0">
                    <span class="text-primary-content block font-semibold">
                        {{ translate('onboarding.settings.taggingTitle') }}
                    </span>
                    <span class="text-secondary-content text-sm">
                        {{ translateBoolean(useSubtitleTagging) }}
                    </span>
                </div>
                <CaretRightIcon
                    class="text-secondary-content h-4 w-4 shrink-0 transition-transform group-open:rotate-90" />
            </summary>
            <div class="space-y-4 px-4 py-4 sm:px-5">
                <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                    <div
                        class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4 md:col-span-2 xl:col-span-1">
                        <div class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                            <div class="min-w-0">
                                <span class="text-primary-content font-semibold">
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
                    </div>

                    <div
                        v-if="useSubtitleTagging === 'true'"
                        class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.subtitle.subtitleTag') }}
                        </span>
                        <InputComponent
                            v-model="subtitleTag"
                            validation-type="string"
                            placeholder="AI"
                            @update:validation="(val) => (isValid.subtitleTag = val)" />
                    </div>

                    <div
                        v-if="useSubtitleTagging === 'true'"
                        class="bg-secondary/20 border-secondary/20 min-w-0 rounded-xl border p-4">
                        <span class="text-primary-content font-semibold">
                            {{ translate('settings.subtitle.subtitleTagShort') }}
                        </span>
                        <p class="text-secondary-content text-xs leading-5">
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
        </details>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, reactive, onMounted } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import InputComponent from '@/components/common/InputComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import CaretRightIcon from '@/components/icons/CaretRightIcon.vue'
import { useI18n } from '@/plugins/i18n'
import axios from 'axios'

const { translate } = useI18n()
const settingsStore = useSettingStore()
const maxConcurrentLimit = ref<number>(20)

const enabledSubtitleProcessingCount = computed(() => {
    let count = 0
    if (fixOverlappingSubtitles.value === 'true') count++
    if (stripSubtitleFormatting.value === 'true') count++
    if (addTranslatorInfo.value === 'true') count++
    return count
})

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

const settingsSummaryItems = computed(() => [
    {
        label: translate('settings.translation.useBatchTranslation'),
        value: translateBoolean(useBatchTranslation.value),
        description: translate('settings.translation.useBatchTranslationDescription')
    },
    {
        label: translate('settings.translation.maxRetries'),
        value: `${maxRetries.value} ${translate('onboarding.settings.retries')}`,
        description: translate('settings.translation.maxRetriesDescription')
    },
    {
        label: translate('settings.translation.maxParallelTranslations'),
        value: `${maxParallelTranslations.value}/${maxConcurrentLimit.value}`,
        description: translate('settings.translation.maxParallelTranslationsDescription')
    },
    {
        label: translate('settings.subtitle.title'),
        value: `${enabledSubtitleProcessingCount.value}/3`,
        description: translate('settings.subtitle.fixOverlappingSubtitlesDescription')
    }
])

function translateBoolean(value: string): string {
    return value === 'true' ? translate('common.enabled') : translate('common.disabled')
}
</script>

<template>
    <CardComponent :title="translate('settings.translation.title')">
        <template #description>
            {{ translate('settings.translation.description') }}
        </template>
        <template #content>
            <SaveNotification ref="saveNotification" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.useBatchTranslation') }}
                </span>
                {{ translate('settings.translation.useBatchTranslationDescription') }}
            </div>
            <ToggleButton v-model="useBatchTranslation">
                <span class="text-primary-content text-sm font-medium">
                    {{
                        useBatchTranslation == 'true'
                            ? translate('common.enabled')
                            : translate('common.disabled')
                    }}
                </span>
            </ToggleButton>
            <div v-if="useBatchTranslation == 'true'" class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.maxBatchSize') }}
                </span>
                {{ translate('settings.translation.maxBatchSizeDescription') }}
            </div>
            <InputComponent
                v-if="useBatchTranslation == 'true'"
                v-model="maxBatchSize"
                validation-type="number"
                placeholder="50"
                @update:validation="(val) => (isValid.maxBatchSize = val)" />

            <!-- Batch Fallback Settings -->
            <div v-if="useBatchTranslation == 'true'" class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.enableBatchFallback') }}
                </span>
                {{ translate('settings.translation.enableBatchFallbackDescription') }}
            </div>
            <ToggleButton v-if="useBatchTranslation == 'true'" v-model="enableBatchFallback">
                <span class="text-primary-content text-sm font-medium">
                    {{
                        enableBatchFallback == 'true'
                            ? translate('common.enabled')
                            : translate('common.disabled')
                    }}
                </span>
            </ToggleButton>
            <div v-if="useBatchTranslation == 'true' && enableBatchFallback == 'true'" class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.maxBatchSplitAttempts') }}
                </span>
                {{ translate('settings.translation.maxBatchSplitAttemptsDescription') }}
            </div>
            <InputComponent
                v-if="useBatchTranslation == 'true' && enableBatchFallback == 'true'"
                v-model="maxBatchSplitAttempts"
                validation-type="number"
                placeholder="3"
                @update:validation="(val) => (isValid.maxBatchSplitAttempts = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.maxRetries') }}
                </span>
                {{ translate('settings.translation.maxRetriesDescription') }}
            </div>
            <InputComponent
                v-model="maxRetries"
                validation-type="number"
                @update:validation="(val) => (isValid.maxRetries = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.retryDelay') }}
                </span>
                {{ translate('settings.translation.retryDelayDescription') }}
            </div>
            <InputComponent
                v-model="retryDelay"
                validation-type="number"
                @update:validation="(val) => (isValid.retryDelay = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.retryDelayMultiplier') }}
                </span>
                {{ translate('settings.translation.retryDelayMultiplierDescription') }}
            </div>
            <InputComponent
                v-model="retryDelayMultiplier"
                validation-type="number"
                @update:validation="(val) => (isValid.retryDelayMultiplier = val)" />

            <div class="flex flex-col space-x-2">
                <span class="font-semibold">
                    {{ translate('settings.translation.maxParallelTranslations') }}
                </span>
                {{ translate('settings.translation.maxParallelTranslationsDescription') }}
                <span v-if="maxConcurrentLimit" class="text-xs text-secondary-content/60">
                    {{ translate('settings.translation.maxParallelTranslationsLimit').format({ max: maxConcurrentLimit }) }}
                </span>
            </div>
            <InputComponent
                v-model="maxParallelTranslations"
                validation-type="number"
                :placeholder="'1'"
                :max="maxConcurrentLimit"
                @update:validation="(val) => (isValid.maxParallelTranslations = val)" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, ref, reactive, onMounted } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import { useI18n } from '@/plugins/i18n'
import axios from 'axios'

const { translate } = useI18n()
const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()
const maxConcurrentLimit = ref<number>(20)
const isValid = reactive({
    maxBatchSize: true,
    maxRetries: true,
    retryDelay: true,
    retryDelayMultiplier: true,
    maxParallelTranslations: true,
    maxBatchSplitAttempts: true
})

onMounted(async () => {
    try {
        const response = await axios.get<{ maxConcurrentTranslations: number }>('/api/setting/system/limits')
        maxConcurrentLimit.value = response.data.maxConcurrentTranslations
    } catch (error) {
        console.error('Failed to fetch system limits:', error)
    }
})

const useBatchTranslation = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.USE_BATCH_TRANSLATION) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.USE_BATCH_TRANSLATION, newValue, true)
        saveNotification.value?.show()
    }
})

const maxBatchSize = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_BATCH_SIZE) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_BATCH_SIZE, newValue, isValid.maxBatchSize)
        saveNotification.value?.show()
    }
})

const maxRetries = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_RETRIES) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.MAX_RETRIES, newValue, isValid.maxRetries)
        saveNotification.value?.show()
    }
})

const retryDelay = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RETRY_DELAY) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.RETRY_DELAY, newValue, isValid.retryDelay)
        saveNotification.value?.show()
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
        saveNotification.value?.show()
    }
})

const maxParallelTranslations = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_PARALLEL_TRANSLATIONS) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.MAX_PARALLEL_TRANSLATIONS,
            newValue,
            isValid.maxParallelTranslations
        )
        saveNotification.value?.show()
    }
})

const enableBatchFallback = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.ENABLE_BATCH_FALLBACK) as string ?? 'true',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.ENABLE_BATCH_FALLBACK, newValue, true)
        saveNotification.value?.show()
    }
})

const maxBatchSplitAttempts = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.MAX_BATCH_SPLIT_ATTEMPTS) as string ?? '3',
    set: (newValue: string): void => {
        settingsStore.updateSetting(
            SETTINGS.MAX_BATCH_SPLIT_ATTEMPTS,
            newValue,
            isValid.maxBatchSplitAttempts
        )
        saveNotification.value?.show()
    }
})
</script>

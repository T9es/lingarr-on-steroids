<template>
    <div class="text-primary-content flex flex-col space-y-3">
        <div class="leading-6">
            {{ translate('settings.services.aiWarningIntro') }}
            <span class="font-semibold">
                {{
                    automationEnabled == 'true'
                        ? translate('settings.services.serviceEnabled')
                        : translate('settings.services.serviceDisabled')
                }}
            </span>
        </div>
        <p class="text-secondary-content text-xs leading-5">
            {{ translate('settings.services.nanoGptDescription') }}
        </p>
        <div class="rounded-md bg-yellow-500/10 p-3 text-sm leading-5 text-yellow-300">
            {{ translate('settings.services.nanoGptReliabilityWarning') }}
        </div>

        <InputComponent
            v-model="apiKey"
            validation-type="string"
            type="password"
            :label="translate('settings.services.apiKey')"
            :min-length="1"
            :error-message="translate('settings.services.apiKeyError')"
            @update:validation="(val) => (apiKeyIsValid = val)" />

        <div class="border-secondary/60 bg-tertiary rounded-md border p-3">
            <ToggleButton v-model="subscriptionModelsOnly" @toggle:update="refreshModels">
                <span class="text-primary-content text-sm font-medium">
                    {{ translate('settings.services.nanoGptSubscriptionOnly') }}
                </span>
            </ToggleButton>
            <p class="text-secondary-content mt-2 text-xs leading-5">
                {{ translate('settings.services.nanoGptSubscriptionOnlyDescription') }}
            </p>
        </div>

        <label class="text-secondary-content mb-1 block text-sm">
            {{ translate('settings.services.aiModel') }}
        </label>
        <SelectComponent
            ref="selectRef"
            v-model:selected="aiModel"
            :options="options"
            :load-on-open="true"
            enable-search
            :placeholder="translate('settings.services.selectModel')"
            :no-options="errorMessage || translate('settings.services.loadingModels')"
            @fetch-options="loadOptions" />

        <div
            v-if="selectedModelIsPaid"
            class="rounded-md bg-yellow-500/10 p-3 text-sm text-yellow-300">
            {{ translate('settings.services.nanoGptPaidModelWarning') }}
        </div>
        <div
            v-if="selectedModelHasKnownIssues"
            class="rounded-md bg-red-500/10 p-3 text-sm leading-5 text-red-300">
            {{ translate('settings.services.nanoGptKnownIssueModelWarning') }}
        </div>
        <div
            v-if="selectedModelLacksStructuredOutput"
            class="rounded-md bg-yellow-500/10 p-3 text-sm text-yellow-300">
            {{ translate('settings.services.nanoGptStructuredOutputWarning') }}
        </div>

        <p class="text-secondary-content text-sm leading-6">
            {{ translate('settings.services.batchSupportAvailable') }}
            <a
                class="text-accent cursor-pointer underline transition hover:brightness-125"
                @click="router.push({ name: 'subtitle-settings' })">
                {{ translate('settings.services.batchSupportLink') }}
            </a>
        </p>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'
import { useRouter } from 'vue-router'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS, type SelectComponentExpose } from '@/ts'
import { useModelOptions } from '@/composables/useModelOptions'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const settingsStore = useSettingStore()
const selectRef = useTemplateRef<SelectComponentExpose>('selectRef')
const { options, errorMessage, loadOptions } = useModelOptions(selectRef)
const router = useRouter()
const emit = defineEmits(['save'])

const apiKeyIsValid = ref(false)

const automationEnabled = computed(() => settingsStore.getSetting(SETTINGS.AUTOMATION_ENABLED))

const apiKey = computed({
    get: () => (settingsStore.getSetting(SETTINGS.NANOGPT_API_KEY) as string) || '',
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.NANOGPT_API_KEY, newValue, apiKeyIsValid.value)
        if (apiKeyIsValid.value) {
            emit('save')
        }
    }
})

const aiModel = computed({
    get: () => (settingsStore.getSetting(SETTINGS.NANOGPT_MODEL) as string) || '',
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.NANOGPT_MODEL, newValue, true)
        emit('save')
    }
})

const subscriptionModelsOnly = computed({
    get: () =>
        (settingsStore.getSetting(SETTINGS.NANOGPT_SUBSCRIPTION_MODELS_ONLY) as string) || 'true',
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.NANOGPT_SUBSCRIPTION_MODELS_ONLY, newValue, true)
        emit('save')
    }
})

const selectedModelLabel = computed(() => {
    return options.value.find((option) => option.value === aiModel.value)?.label ?? ''
})

const selectedModelIsPaid = computed(() => selectedModelLabel.value.includes('Paid'))

const selectedModelHasKnownIssues = computed(() => {
    const model = aiModel.value.toLowerCase()
    return (
        model === 'deepseek/deepseek-v4-flash' ||
        model === 'deepseek/deepseek-v4-flash:thinking' ||
        model === 'deepseek/deepseek-v4-pro' ||
        model === 'deepseek/deepseek-v4-pro-cheaper:thinking' ||
        model === 'qwen/qwen3-235b-a22b'
    )
})

const selectedModelLacksStructuredOutput = computed(() =>
    selectedModelLabel.value.toLowerCase().includes('no structured output')
)

const refreshModels = async () => {
    options.value = []
    await loadOptions()
}
</script>

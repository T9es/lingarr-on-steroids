<template>
    <div class="text-primary-content flex flex-col space-y-3">
        <p class="text-secondary-content text-xs leading-6">
            {{ translate('settings.services.localAiDescriptionPath') }}
            <span class="bg-primary/70 text-primary-content rounded-md px-1.5 py-1">
                /v1/chat/completions
            </span>
            {{ translate('settings.services.localAiDescriptionOr') }}
            <span class="bg-primary/70 text-primary-content my-1 inline-block rounded-md px-1.5 py-1">
                /api/generate
            </span>
            {{ translate('settings.services.localAiDescriptionFollow') }}
            <a
                href="https://platform.openai.com/docs/api-reference/chat/create"
                class="text-accent underline transition hover:brightness-125"
                target="_blank">
                Open AI
            </a>
            {{ translate('settings.services.localAiDescriptionSpecification') }}
        </p>

        <InputComponent
            v-model="address"
            validation-type="url"
            :placeholder="translate('settings.services.localAiPlaceholder')"
            :label="translate('settings.services.serviceAddress')"
            @update:validation="(val) => (isValid.address = val)" />

        <InputComponent
            v-model="aiModel"
            validation-type="string"
            :label="translate('settings.services.aiModel')"
            :placeholder="translate('settings.services.localAiModelPlaceholder')"
            @update:validation="(val) => (isValid.model = val)" />

        <InputComponent
            v-model="apiKey"
            validation-type="string"
            type="password"
            :label="translate('settings.services.apiKey')"
            @update:validation="(val) => (isValid.apiKey = val)" />
        <p class="text-secondary-content text-xs leading-5">
            {{ translate('settings.services.localAiNotification') }}
        </p>

        <div class="mt-2 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <span class="text-primary-content text-sm font-medium">
                {{ translate('settings.services.enableTokenLimit') }}
            </span>
            <ToggleButton v-model="tokenLimitEnabled" @update:modelValue="saveTokenLimitEnabled" />
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
import { computed, reactive } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import { useRouter } from 'vue-router'
import InputComponent from '@/components/common/InputComponent.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const settingsStore = useSettingStore()
const emit = defineEmits(['save'])
const isValid = reactive({
    address: false,
    model: false,
    apiKey: false
})
const router = useRouter()

const aiModel = computed({
    get: () => settingsStore.getSetting(SETTINGS.LOCAL_AI_MODEL) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.LOCAL_AI_MODEL, newValue, isValid.model)
        if (isValid.model) {
            emit('save')
        }
    }
})

const apiKey = computed({
    get: () => settingsStore.getSetting(SETTINGS.LOCAL_AI_API_KEY) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.LOCAL_AI_API_KEY, newValue, isValid.apiKey)
        if (isValid.apiKey) {
            emit('save')
        }
    }
})

const address = computed({
    get: () => settingsStore.getSetting(SETTINGS.LOCAL_AI_ENDPOINT) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.LOCAL_AI_ENDPOINT, newValue, isValid.address)
        if (isValid.address) {
            emit('save')
        }
    }
})

const tokenLimitEnabled = computed({
    get: () => settingsStore.getSetting(SETTINGS.LOCALAI_TOKEN_LIMIT_ENABLED) === 'true',
    set: () => {}
})

const saveTokenLimitEnabled = async (value: string) => {
    await settingsStore.updateSetting(SETTINGS.LOCALAI_TOKEN_LIMIT_ENABLED, value, true)
}
</script>

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
            {{ translate('settings.services.aiCostDescription') }}
        </p>

        <InputComponent
            v-model="apiKey"
            validation-type="string"
            type="password"
            :label="translate('settings.services.apiKey')"
            :min-length="1"
            :error-message="translate('settings.services.apiKeyError')"
            @update:validation="(val) => (apiKeyIsValid = val)" />

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
import { useSettingStore } from '@/store/setting'
import { SETTINGS, type SelectComponentExpose } from '@/ts'
import SelectComponent from '@/components/common/SelectComponent.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import { useI18n } from '@/plugins/i18n'
import { useModelOptions } from '@/composables/useModelOptions'
import { useRouter } from 'vue-router'

const { translate } = useI18n()
const selectRef = useTemplateRef<SelectComponentExpose>('selectRef')
const { options, errorMessage, loadOptions } = useModelOptions(selectRef)
const router = useRouter()

const settingsStore = useSettingStore()
const emit = defineEmits(['save'])
const apiKeyIsValid = ref(false)

const automationEnabled = computed(() => settingsStore.getSetting(SETTINGS.AUTOMATION_ENABLED))

const aiModel = computed({
    get: () => settingsStore.getSetting(SETTINGS.OPENAI_MODEL) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.OPENAI_MODEL, newValue, true)
        emit('save')
    }
})

const apiKey = computed({
    get: () => settingsStore.getSetting(SETTINGS.OPENAI_API_KEY) as string,
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.OPENAI_API_KEY, newValue, apiKeyIsValid.value)
        if (apiKeyIsValid.value) {
            emit('save')
        }
    }
})
</script>

<template>
    <div class="flex flex-col space-y-3">
        <div>
            {{ translate('settings.services.aiWarningIntro') }}
            <span :class="automationEnabled == 'true' ? 'text-red-500' : 'text-green-500'">
                {{
                    automationEnabled == 'true'
                        ? translate('settings.services.serviceEnabled')
                        : translate('settings.services.serviceDisabled')
                }}
            </span>
        </div>
        <p class="text-xs">
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

        <label class="mb-1 block text-sm">
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


    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import InputComponent from '@/components/common/InputComponent.vue'
import SelectComponent from '@/components/common/SelectComponent.vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import { useModelOptions } from '@/composables/useModelOptions'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const settingsStore = useSettingStore()
const { options, errorMessage, selectRef, loadOptions } = useModelOptions()
const emit = defineEmits(['save'])

const apiKeyIsValid = ref(false)

const automationEnabled = computed(() => settingsStore.getSetting(SETTINGS.AUTOMATION_ENABLED))

const apiKey = computed({
    get: () => (settingsStore.getSetting(SETTINGS.CHUTES_API_KEY) as string) || '',
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.CHUTES_API_KEY, newValue, apiKeyIsValid.value)
        if (apiKeyIsValid.value) {
            emit('save')
        }
    }
})

const aiModel = computed({
    get: () => (settingsStore.getSetting(SETTINGS.CHUTES_MODEL) as string) || '',
    set: (newValue: string) => {
        settingsStore.updateSetting(SETTINGS.CHUTES_MODEL, newValue, true)
        emit('save')
    }
})
</script>

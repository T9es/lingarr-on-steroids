<template>
    <div class="text-primary-content flex flex-col space-y-3">
        <InputComponent
            v-model="deepLApiKey"
            validation-type="string"
            type="password"
            :min-length="1"
            :label="translate('settings.services.apiKey')"
            :error-message="translate('settings.services.apiKeyError')"
            @update:validation="(val) => (isValid = val)" />
        <div
            v-translate="'settings.services.deeplNotification'"
            class="text-secondary-content text-xs leading-5" />
    </div>
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import InputComponent from '@/components/common/InputComponent.vue'

const isValid = ref(false)
const settingsStore = useSettingStore()
const emit = defineEmits(['save'])

const deepLApiKey = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.DEEPL_API_KEY) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.DEEPL_API_KEY, newValue, isValid.value)
        if (isValid.value) {
            emit('save')
        }
    }
})
</script>

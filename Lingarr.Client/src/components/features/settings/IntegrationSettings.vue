<template>
    <SaveNotification ref="saveNotification" />
    <CardComponent :title="translate('settings.integrations.radarrHeader')">
        <template #icon>
            <RadarrIcon />
        </template>
        <template #description>
            {{ translate('settings.integrations.description') }}
        </template>
        <template #content>
            <div class="flex flex-col space-y-2">
                <InputComponent
                    v-model="radarrUrl"
                    validation-type="url"
                    :label="translate('settings.integrations.radarrAddress')"
                    :error-message="translate('settings.integrations.radarrAddressError')"
                    @update:validation="(val) => (isValid.radarrUrl = val)" />
                <InputComponent
                    v-model="radarrApiKey"
                    :min-length="32"
                    :max-length="32"
                    validation-type="string"
                    type="password"
                    :label="translate('settings.integrations.radarrApiKey')"
                    :error-message="translate('settings.integrations.radarrApiKeyError')"
                    @update:validation="(val) => (isValid.radarrApiKey = val)" />

                <!-- Connection Status -->
                <div class="flex items-center gap-3 pt-2">
                    <button
                        type="button"
                        class="bg-primary-600 hover:bg-primary-700 rounded-md px-3 py-1.5 text-sm text-white transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                        :disabled="
                            radarrStatus.testing || !isValid.radarrUrl || !isValid.radarrApiKey
                        "
                        @click="testRadarrConnection">
                        <span v-if="radarrStatus.testing" class="flex items-center gap-2">
                            <svg
                                class="h-4 w-4 animate-spin"
                                xmlns="http://www.w3.org/2000/svg"
                                fill="none"
                                viewBox="0 0 24 24">
                                <circle
                                    class="opacity-25"
                                    cx="12"
                                    cy="12"
                                    r="10"
                                    stroke="currentColor"
                                    stroke-width="4"></circle>
                                <path
                                    class="opacity-75"
                                    fill="currentColor"
                                    d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                            </svg>
                            {{ translate('settings.integrations.testing') }}
                        </span>
                        <span v-else>{{ translate('settings.integrations.testConnection') }}</span>
                    </button>

                    <div v-if="radarrStatus.tested" class="flex items-center gap-2 text-sm">
                        <span
                            v-if="radarrStatus.connected"
                            class="flex items-center gap-1 text-green-500">
                            <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                                <path
                                    fill-rule="evenodd"
                                    d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z"
                                    clip-rule="evenodd" />
                            </svg>
                            {{ translate('settings.integrations.connectionSuccess') }}
                            <span v-if="radarrStatus.version" class="text-gray-500">
                                ({{ translate('settings.integrations.version') }}:
                                {{ radarrStatus.version }})
                            </span>
                        </span>
                        <span v-else class="flex items-center gap-1 text-red-500">
                            <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                                <path
                                    fill-rule="evenodd"
                                    d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
                                    clip-rule="evenodd" />
                            </svg>
                            {{ translate('settings.integrations.connectionFailed') }}:
                            {{ radarrStatus.message }}
                        </span>
                    </div>
                </div>
            </div>
            <div v-translate="'settings.integrations.reindexTask'" class="pt-2" />
        </template>
    </CardComponent>

    <CardComponent :title="translate('settings.integrations.sonarrHeader')">
        <template #icon>
            <SonarrIcon />
        </template>
        <template #description>
            {{ translate('settings.integrations.description') }}
        </template>
        <template #content>
            <div class="flex flex-col space-y-2">
                <InputComponent
                    v-model="sonarrUrl"
                    validation-type="url"
                    :label="translate('settings.integrations.sonarrAddress')"
                    :error-message="translate('settings.integrations.sonarrAddressError')"
                    @update:validation="(val) => (isValid.sonarrUrl = val)" />
                <InputComponent
                    v-model="sonarrApiKey"
                    :min-length="32"
                    :max-length="32"
                    validation-type="string"
                    type="password"
                    :label="translate('settings.integrations.sonarrApiKey')"
                    :error-message="translate('settings.integrations.sonarrApiKeyError')"
                    @update:validation="(val) => (isValid.sonarrApiKey = val)" />

                <!-- Connection Status -->
                <div class="flex items-center gap-3 pt-2">
                    <button
                        type="button"
                        class="bg-primary-600 hover:bg-primary-700 rounded-md px-3 py-1.5 text-sm text-white transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                        :disabled="
                            sonarrStatus.testing || !isValid.sonarrUrl || !isValid.sonarrApiKey
                        "
                        @click="testSonarrConnection">
                        <span v-if="sonarrStatus.testing" class="flex items-center gap-2">
                            <svg
                                class="h-4 w-4 animate-spin"
                                xmlns="http://www.w3.org/2000/svg"
                                fill="none"
                                viewBox="0 0 24 24">
                                <circle
                                    class="opacity-25"
                                    cx="12"
                                    cy="12"
                                    r="10"
                                    stroke="currentColor"
                                    stroke-width="4"></circle>
                                <path
                                    class="opacity-75"
                                    fill="currentColor"
                                    d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                            </svg>
                            {{ translate('settings.integrations.testing') }}
                        </span>
                        <span v-else>{{ translate('settings.integrations.testConnection') }}</span>
                    </button>

                    <div v-if="sonarrStatus.tested" class="flex items-center gap-2 text-sm">
                        <span
                            v-if="sonarrStatus.connected"
                            class="flex items-center gap-1 text-green-500">
                            <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                                <path
                                    fill-rule="evenodd"
                                    d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z"
                                    clip-rule="evenodd" />
                            </svg>
                            {{ translate('settings.integrations.connectionSuccess') }}
                            <span v-if="sonarrStatus.version" class="text-gray-500">
                                ({{ translate('settings.integrations.version') }}:
                                {{ sonarrStatus.version }})
                            </span>
                        </span>
                        <span v-else class="flex items-center gap-1 text-red-500">
                            <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                                <path
                                    fill-rule="evenodd"
                                    d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
                                    clip-rule="evenodd" />
                            </svg>
                            {{ translate('settings.integrations.connectionFailed') }}:
                            {{ sonarrStatus.message }}
                        </span>
                    </div>
                </div>
            </div>
            <div v-translate="'settings.integrations.reindexTask'" class="pt-2" />
        </template>
    </CardComponent>

    <SubtitleProviderSettings />
</template>

<script setup lang="ts">
import { computed, ref, reactive } from 'vue'
import { useSettingStore } from '@/store/setting'
import SaveNotification from '@/components/common/SaveNotification.vue'
import { SETTINGS } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'
import SubtitleProviderSettings from '@/components/features/settings/SubtitleProviderSettings.vue'
import services from '@/services'
import { useI18n } from '@/plugins/i18n'

interface ConnectionTestResult {
    isConnected: boolean
    message?: string
    version?: string
}

interface ConnectionStatus {
    testing: boolean
    tested: boolean
    connected: boolean
    message: string
    version: string | null
}

const { translate } = useI18n()

const isValid = reactive({
    radarrUrl: false,
    radarrApiKey: false,
    sonarrUrl: false,
    sonarrApiKey: false
})

const radarrStatus = reactive<ConnectionStatus>({
    testing: false,
    tested: false,
    connected: false,
    message: '',
    version: null
})

const sonarrStatus = reactive<ConnectionStatus>({
    testing: false,
    tested: false,
    connected: false,
    message: '',
    version: null
})

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()

const testRadarrConnection = async () => {
    radarrStatus.testing = true
    radarrStatus.tested = false
    try {
        const result = await services.setting.testRadarrConnection<ConnectionTestResult>()
        radarrStatus.connected = result.isConnected
        radarrStatus.message = result.message || ''
        radarrStatus.version = result.version || null
    } catch (error) {
        radarrStatus.connected = false
        radarrStatus.message = 'Request failed'
    } finally {
        radarrStatus.testing = false
        radarrStatus.tested = true
    }
}

const testSonarrConnection = async () => {
    sonarrStatus.testing = true
    sonarrStatus.tested = false
    try {
        const result = await services.setting.testSonarrConnection<ConnectionTestResult>()
        sonarrStatus.connected = result.isConnected
        sonarrStatus.message = result.message || ''
        sonarrStatus.version = result.version || null
    } catch (error) {
        sonarrStatus.connected = false
        sonarrStatus.message = 'Request failed'
    } finally {
        sonarrStatus.testing = false
        sonarrStatus.tested = true
    }
}

const radarrApiKey = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RADARR_API_KEY) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.RADARR_API_KEY, newValue, isValid.radarrApiKey)
        if (isValid.radarrApiKey) {
            saveNotification.value?.show()
            radarrStatus.tested = false // Reset status when settings change
        }
    }
})
const sonarrApiKey = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SONARR_API_KEY) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SONARR_API_KEY, newValue, isValid.sonarrApiKey)
        if (isValid.sonarrApiKey) {
            saveNotification.value?.show()
            sonarrStatus.tested = false // Reset status when settings change
        }
    }
})
const radarrUrl = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.RADARR_URL) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.RADARR_URL, newValue, isValid.radarrUrl)
        if (isValid.radarrUrl) {
            saveNotification.value?.show()
            radarrStatus.tested = false // Reset status when settings change
        }
    }
})
const sonarrUrl = computed({
    get: (): string => settingsStore.getSetting(SETTINGS.SONARR_URL) as string,
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SONARR_URL, newValue, isValid.sonarrUrl)
        if (isValid.sonarrUrl) {
            saveNotification.value?.show()
            sonarrStatus.tested = false // Reset status when settings change
        }
    }
})
</script>

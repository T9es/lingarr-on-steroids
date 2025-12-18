<template>
    <div class="flex flex-col space-y-6">
        <!-- Subdl Settings -->
        <CardComponent title="Subdl">
            <template #description>
                {{ translate('settings.subtitle.subdlDescription') }}
            </template>
            <template #content>
                <div class="flex flex-col space-y-4">
                    <SaveNotification ref="subdlSaveNotification" />
                    
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('common.enabled') }}
                        </span>
                    </div>
                    <ToggleButton v-model="subdlEnabled">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                subdlEnabled == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>

                    <InputComponent
                        v-if="subdlEnabled == 'true'"
                        v-model="subdlApiKey"
                        validation-type="string"
                        :label="translate('settings.subtitle.apiKey')"
                        @update:validation="(val) => (isValid.subdlApiKey = val)" />

                    <div v-if="subdlEnabled == 'true'" class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('settings.subtitle.vip') }}
                        </span>
                    </div>
                    <ToggleButton v-if="subdlEnabled == 'true'" v-model="subdlVip">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                subdlVip == 'true'
                                    ? translate('common.yes')
                                    : translate('common.no')
                            }}
                        </span>
                    </ToggleButton>
                </div>
            </template>
        </CardComponent>

        <!-- OpenSubtitles Settings -->
        <CardComponent title="OpenSubtitles">
            <template #description>
                {{ translate('settings.subtitle.opensubtitlesDescription') }}
            </template>
            <template #content>
                <div class="flex flex-col space-y-4">
                    <SaveNotification ref="osSaveNotification" />
                    
                    <div class="flex flex-col space-x-2">
                        <span class="font-semibold">
                            {{ translate('common.enabled') }}
                        </span>
                    </div>
                    <ToggleButton v-model="openSubtitlesEnabled">
                        <span class="text-primary-content text-sm font-medium">
                            {{
                                openSubtitlesEnabled == 'true'
                                    ? translate('common.enabled')
                                    : translate('common.disabled')
                            }}
                        </span>
                    </ToggleButton>

                    <div v-if="openSubtitlesEnabled == 'true'" class="flex flex-col space-y-4">
                        <InputComponent
                            v-model="openSubtitlesUsername"
                            validation-type="string"
                            :label="translate('common.username')"
                            @update:validation="(val) => (isValid.openSubtitlesUsername = val)" />
                            
                        <InputComponent
                            v-model="openSubtitlesPassword"
                            validation-type="string"
                            type="password"
                            :label="translate('common.password')"
                            @update:validation="(val) => (isValid.openSubtitlesPassword = val)" />
                            
                        <InputComponent
                            v-model="openSubtitlesApiKey"
                            validation-type="string"
                            :label="translate('settings.subtitle.apiKey')"
                            @update:validation="(val) => (isValid.openSubtitlesApiKey = val)" />
                    </div>
                </div>
            </template>
        </CardComponent>

        <!-- General Provider Settings -->
        <CardComponent :title="translate('settings.subtitle.generalProviderSettings')">
            <template #content>
                <div class="flex flex-col space-y-4">
                    <SaveNotification ref="generalSaveNotification" />

                    <InputComponent
                        v-model="dailyLimit"
                        validation-type="number"
                        :label="translate('settings.subtitle.dailyLimit')"
                        description="Max downloads per provider per day"
                        @update:validation="(val) => (isValid.dailyLimit = val)" />
                        
                    <InputComponent
                        v-model="minScore"
                        validation-type="number"
                        :label="translate('settings.subtitle.minScore')"
                        description="Minimum match score (0-100)"
                        @update:validation="(val) => (isValid.minScore = val)" />
                </div>
            </template>
        </CardComponent>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue'
import { SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'
import { useI18n } from '@/plugins/i18n'

import CardComponent from '@/components/common/CardComponent.vue'
import SaveNotification from '@/components/common/SaveNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'

const { translate } = useI18n()
const settingsStore = useSettingStore()

const subdlSaveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const osSaveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const generalSaveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)

const isValid = reactive({
    subdlApiKey: true,
    openSubtitlesUsername: true,
    openSubtitlesPassword: true,
    openSubtitlesApiKey: true,
    dailyLimit: true,
    minScore: true
})

// Subdl Settings
const subdlEnabled = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.SUBDL_ENABLED) as string) ?? 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBDL_ENABLED, newValue, true)
        subdlSaveNotification.value?.show()
    }
})

const subdlApiKey = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.SUBDL_API_KEY) as string) ?? '',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBDL_API_KEY, newValue, isValid.subdlApiKey)
        subdlSaveNotification.value?.show()
    }
})

const subdlVip = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.SUBDL_VIP) as string) ?? 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBDL_VIP, newValue, true)
        subdlSaveNotification.value?.show()
    }
})

// OpenSubtitles Settings
const openSubtitlesEnabled = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.OPENSUBTITLES_ENABLED) as string) ?? 'false',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.OPENSUBTITLES_ENABLED, newValue, true)
        osSaveNotification.value?.show()
    }
})

const openSubtitlesUsername = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.OPENSUBTITLES_USERNAME) as string) ?? '',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.OPENSUBTITLES_USERNAME, newValue, isValid.openSubtitlesUsername)
        osSaveNotification.value?.show()
    }
})

const openSubtitlesPassword = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.OPENSUBTITLES_PASSWORD) as string) ?? '',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.OPENSUBTITLES_PASSWORD, newValue, isValid.openSubtitlesPassword)
        osSaveNotification.value?.show()
    }
})

const openSubtitlesApiKey = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.OPENSUBTITLES_API_KEY) as string) ?? '',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.OPENSUBTITLES_API_KEY, newValue, isValid.openSubtitlesApiKey)
        osSaveNotification.value?.show()
    }
})

// General Settings
const dailyLimit = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.SUBTITLE_PROVIDER_DAILY_LIMIT) as string) ?? '20',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_PROVIDER_DAILY_LIMIT, newValue, isValid.dailyLimit)
        generalSaveNotification.value?.show()
    }
})

const minScore = computed({
    get: (): string => (settingsStore.getSetting(SETTINGS.SUBTITLE_PROVIDER_MIN_SCORE) as string) ?? '80',
    set: (newValue: string): void => {
        settingsStore.updateSetting(SETTINGS.SUBTITLE_PROVIDER_MIN_SCORE, newValue, isValid.minScore)
        generalSaveNotification.value?.show()
    }
})
</script>

<template>
    <div class="space-y-6 text-center">
        <!-- Success Icon -->
        <div class="flex justify-center">
            <CheckMarkCicleIcon class="h-16 w-16 text-green-500" />
        </div>

        <!-- Title -->
        <h2 class="text-primary-content text-2xl font-bold">{{ translate('onboarding.completeStep.title') }}</h2>

        <!-- Summary -->
        <div class="bg-primary/50 rounded-md p-4 text-left space-y-2">
            <div v-if="radarrCount > 0">
                <span class="text-secondary-content">{{ translate('onboarding.completeStep.radarrInstances') }}</span>
                <span class="text-primary-content font-semibold ml-2">{{
                    radarrCount
                }}</span>
            </div>
            <div v-if="sonarrCount > 0">
                <span class="text-secondary-content">{{ translate('onboarding.completeStep.sonarrInstances') }}</span>
                <span class="text-primary-content font-semibold ml-2">{{
                    sonarrCount
                }}</span>
            </div>
            <div v-if="serviceName">
                <span class="text-secondary-content">{{ translate('onboarding.completeStep.translationService') }}</span>
                <span class="text-primary-content font-semibold ml-2">{{
                    serviceName
                }}</span>
            </div>
            <div v-if="sourceLanguageNames">
                <span class="text-secondary-content">{{ translate('onboarding.completeStep.sourceLanguages') }}</span>
                <span class="text-primary-content font-semibold ml-2">{{
                    sourceLanguageNames
                }}</span>
            </div>
            <div v-if="targetLanguageNames">
                <span class="text-secondary-content">{{ translate('onboarding.completeStep.targetLanguages') }}</span>
                <span class="text-primary-content font-semibold ml-2">{{
                    targetLanguageNames
                }}</span>
            </div>
        </div>

        <!-- Actions -->
        <div class="flex flex-col items-center gap-4">
            <button
                @click="handleComplete"
                class="border-accent bg-accent hover:bg-accent/80 text-primary-content rounded-md border px-6 py-2 font-semibold transition-colors">
                {{ translate('onboarding.completeStep.startTranslating') }}
            </button>
            <router-link
                to="/settings"
                class="text-secondary-content hover:text-primary-content text-sm underline transition-colors">
                {{ translate('onboarding.completeStep.goToSettings') }}
            </router-link>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useOnboardingStore } from '@/store/onboarding'
import { useSettingStore } from '@/store/setting'
import { SETTINGS, SERVICE_TYPE } from '@/ts'
import type { ILanguage } from '@/ts/language'
import CheckMarkCicleIcon from '@/components/icons/CheckMarkCicleIcon.vue'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const router = useRouter()
const onboardingStore = useOnboardingStore()
const settingStore = useSettingStore()

// Get counts from onboarding store
const radarrCount = computed(() => onboardingStore.radarrInstances.length)
const sonarrCount = computed(() => onboardingStore.sonarrInstances.length)

// Get service name from settings
const serviceName = computed(() => {
    const serviceType = settingStore.getSetting(SETTINGS.SERVICE_TYPE) as string
    if (!serviceType) return null

    const serviceNames: Record<string, string> = {
        [SERVICE_TYPE.LIBRETRANSLATE]: 'LibreTranslate',
        [SERVICE_TYPE.OPENAI]: 'OpenAI',
        [SERVICE_TYPE.ANTHROPIC]: 'Anthropic',
        [SERVICE_TYPE.LOCALAI]: 'LocalAI',
        [SERVICE_TYPE.DEEPL]: 'DeepL',
        [SERVICE_TYPE.GEMINI]: 'Google Gemini',
        [SERVICE_TYPE.DEEPSEEK]: 'DeepSeek',
        [SERVICE_TYPE.GOOGLE]: 'Google Translate',
        [SERVICE_TYPE.BING]: 'Bing Translate',
        [SERVICE_TYPE.MICROSOFT]: 'Microsoft Translator',
        [SERVICE_TYPE.YANDEX]: 'Yandex Translate',
        [SERVICE_TYPE.CHUTES]: 'Chutes.ai'
    }

    return serviceNames[serviceType] || serviceType
})

// Get source language names
const sourceLanguageNames = computed(() => {
    const languages = settingStore.getSetting(SETTINGS.SOURCE_LANGUAGES) as
        | ILanguage[]
        | null
    if (!languages || languages.length === 0) return null
    return languages.map((lang) => lang.name).join(', ')
})

// Get target language names
const targetLanguageNames = computed(() => {
    const languages = settingStore.getSetting(SETTINGS.TARGET_LANGUAGES) as
        | ILanguage[]
        | null
    if (!languages || languages.length === 0) return null
    return languages.map((lang) => lang.name).join(', ')
})

// Handle complete button click
const handleComplete = () => {
    onboardingStore.complete()
    router.push('/')
}
</script>

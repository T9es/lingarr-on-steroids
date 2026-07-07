<template>
    <div class="space-y-6 text-center">
        <!-- Success Icon -->
        <div class="flex justify-center">
            <CheckMarkCicleIcon class="h-16 w-16 text-green-500" />
        </div>

        <!-- Title -->
        <h2 class="text-primary-content text-2xl font-bold">
            {{ translate('onboarding.completeStep.title') }}
        </h2>

        <!-- Summary -->
        <div class="bg-primary/50 space-y-2 rounded-md p-4 text-left">
            <div v-if="radarrCount > 0">
                <span class="text-secondary-content">
                    {{ translate('onboarding.completeStep.radarrInstances') }}
                </span>
                <span class="text-primary-content ml-2 font-semibold">{{ radarrCount }}</span>
            </div>
            <div v-if="sonarrCount > 0">
                <span class="text-secondary-content">
                    {{ translate('onboarding.completeStep.sonarrInstances') }}
                </span>
                <span class="text-primary-content ml-2 font-semibold">{{ sonarrCount }}</span>
            </div>
            <div v-if="serviceName">
                <span class="text-secondary-content">
                    {{ translate('onboarding.completeStep.translationService') }}
                </span>
                <span class="text-primary-content ml-2 font-semibold">{{ serviceName }}</span>
            </div>
            <div v-if="sourceLanguageNames">
                <span class="text-secondary-content">
                    {{ translate('onboarding.completeStep.sourceLanguages') }}
                </span>
                <span class="text-primary-content ml-2 font-semibold">
                    {{ sourceLanguageNames }}
                </span>
            </div>
            <div v-if="targetLanguageNames">
                <span class="text-secondary-content">
                    {{ translate('onboarding.completeStep.targetLanguages') }}
                </span>
                <span class="text-primary-content ml-2 font-semibold">
                    {{ targetLanguageNames }}
                </span>
            </div>

            <!-- Subtitle Configuration Summary -->
            <div class="border-secondary-content/20 mt-2 border-t pt-2">
                <div v-if="useSubtitleTagging">
                    <span class="text-secondary-content">
                        {{ translate('onboarding.completeStep.subtitleTagging') }}
                    </span>
                    <span class="text-primary-content ml-2 font-semibold">
                        {{ translate('common.enabled') }} ({{ subtitleTag }})
                    </span>
                </div>
                <div v-if="ocrEnabled">
                    <span class="text-secondary-content">
                        {{ translate('onboarding.completeStep.ocrEnabled') }}
                    </span>
                    <span class="text-primary-content ml-2 font-semibold">
                        {{ translate('common.enabled') }}
                    </span>
                </div>
                <div>
                    <span class="text-secondary-content">
                        {{ translate('onboarding.completeStep.embedMode') }}
                    </span>
                    <span class="text-primary-content ml-2 font-semibold">
                        {{ embedModeLabel }}
                    </span>
                </div>
            </div>
        </div>

        <!-- Actions -->
        <div class="flex flex-col items-center gap-4">
            <button
                class="border-accent bg-accent hover:bg-accent/80 text-primary-content rounded-md border px-6 py-2 font-semibold transition-colors"
                @click="handleComplete">
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
import { SETTINGS } from '@/ts'
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

    const serviceKey = `services.serviceNames.${serviceType}`
    const translatedName = translate(serviceKey)

    // If translation key exists, return translated name, otherwise use the key as fallback
    return translatedName !== serviceKey ? translatedName : serviceType
})

// Get source language names
const sourceLanguageNames = computed(() => {
    const languages = settingStore.getSetting(SETTINGS.SOURCE_LANGUAGES) as ILanguage[] | null
    if (!languages || languages.length === 0) return null
    return languages.map((lang) => lang.name).join(', ')
})

// Get target language names
const targetLanguageNames = computed(() => {
    const languages = settingStore.getSetting(SETTINGS.TARGET_LANGUAGES) as ILanguage[] | null
    if (!languages || languages.length === 0) return null
    return languages.map((lang) => lang.name).join(', ')
})

// Subtitle tagging summary
const useSubtitleTagging = computed(() => {
    return (settingStore.getSetting(SETTINGS.USE_SUBTITLE_TAGGING) as string) === 'true'
})

const subtitleTag = computed(() => {
    return (settingStore.getSetting(SETTINGS.SUBTITLE_TAG) as string) || '-AI-TRANSLATED-'
})

// OCR summary
const ocrEnabled = computed(() => {
    return (settingStore.getSetting(SETTINGS.SUBTITLE_OCR_ENABLED) as string) === 'true'
})

// Embedding mode summary
const embedModeLabel = computed(() => {
    const always = settingStore.getSetting(SETTINGS.EMBED_IN_CONTAINER) as string
    const whenTooLong = settingStore.getSetting(SETTINGS.EMBED_WHEN_PATH_TOO_LONG) as string
    if (always === 'true') return translate('settings.subtitle.embedModeAlways')
    if (whenTooLong === 'true') return translate('settings.subtitle.embedModeWhenTooLong')
    return translate('settings.subtitle.embedModeNever')
})

// Handle complete button click
const handleComplete = () => {
    onboardingStore.complete()
    router.push('/')
}
</script>

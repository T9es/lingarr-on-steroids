<template>
    <div class="space-y-6">
        <!-- Source Languages Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('onboarding.language.sourceTitle') }}
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                {{ translate('onboarding.language.sourceDescription') }}
            </p>
            <LanguageSelect
                v-model:selected="sourceLanguages"
                :options="languages"
            />
        </div>

        <!-- Target Languages Section (only shown when source languages are selected) -->
        <div v-if="sourceLanguages.length > 0">
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('onboarding.language.targetTitle') }}
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                {{ translate('onboarding.language.targetDescription') }}
            </p>
            <LanguageSelect
                v-model:selected="targetLanguages"
                :options="targetLanguageOptions"
            />
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import LanguageSelect from '@/components/features/settings/LanguageSelect.vue'
import { ILanguage, SETTINGS } from '@/ts'
import { useTranslateStore } from '@/store/translate'
import { useSettingStore } from '@/store/setting'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const settingStore = useSettingStore()
const translateStore = useTranslateStore()

// Get languages from translate store
const languages = computed(() => translateStore.getLanguages)

// Source languages computed property with getter/setter
const sourceLanguages = computed({
    get: (): ILanguage[] => {
        const stored = settingStore.getSetting(SETTINGS.SOURCE_LANGUAGES)
        return (stored as ILanguage[]) || []
    },
    set: (newValue: ILanguage[]): void => {
        settingStore.updateSetting(SETTINGS.SOURCE_LANGUAGES, newValue, true, true)
    }
})

// Target languages computed property with getter/setter
const targetLanguages = computed({
    get: (): ILanguage[] => {
        const stored = settingStore.getSetting(SETTINGS.TARGET_LANGUAGES)
        return (stored as ILanguage[]) || []
    },
    set: (newValue: ILanguage[]): void => {
        settingStore.updateSetting(SETTINGS.TARGET_LANGUAGES, newValue, true, true)
    }
})

// Compute available target languages based on selected source languages
const targetLanguageOptions = computed(() => {
    if (sourceLanguages.value.length === 0) {
        return []
    }

    // Get all target codes from selected source languages
    const allTargets = sourceLanguages.value.flatMap((sourceLanguage) => {
        const sourceTargetSet = languages.value.find(
            (lang) => lang.code === sourceLanguage.code
        )
        if (!sourceTargetSet) {
            return []
        }
        return sourceTargetSet.targets || []
    })

    // Get unique target codes
    const uniqueTargets = [...new Set(allTargets)]

    // Map target codes to language objects
    return uniqueTargets
        .map((targetCode) => {
            const languageInfo = languages.value.find((lang) => lang.code === targetCode)
            if (languageInfo) {
                return { ...languageInfo }
            }
            return null
        })
        .filter((lang): lang is ILanguage => lang !== null)
})

// Load languages on mount
onMounted(() => {
    translateStore.setLanguages()
})
</script>

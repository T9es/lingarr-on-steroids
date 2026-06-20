<template>
    <div class="space-y-6">
        <!-- Source Language Mode Toggle -->
        <div class="border-accent bg-secondary rounded-md border p-4">
            <div class="mb-3 flex flex-col space-x-2">
                <span class="text-primary-content font-semibold">
                    {{ translate('settings.translate.sourceLanguageModeTitle') }}
                </span>
                <span class="text-secondary-content text-sm">
                    {{ translate('settings.translate.sourceLanguageModeAutoDescription') }}
                </span>
            </div>
            <ToggleButton v-model="autoModeEnabled">
                <span class="text-primary-content text-sm font-medium">
                    {{
                        isAutoMode
                            ? translate('settings.translate.sourceLanguageModeAuto')
                            : translate('settings.translate.sourceLanguageModeManual')
                    }}
                </span>
            </ToggleButton>
            <div
                v-if="isAutoMode"
                class="bg-accent/10 text-accent mt-3 inline-flex items-center rounded-md px-3 py-1 text-xs font-medium">
                {{ translate('settings.translate.sourceLanguageModeAutoBadge') }}
            </div>
            <div
                v-else
                class="border-accent bg-accent/5 mt-3 rounded-md border border-dashed px-3 py-2">
                <span class="text-secondary-content text-xs leading-5">
                    {{ translate('onboarding.language.autoModeRecommendation') }}
                </span>
            </div>
        </div>

        <!-- Source Languages Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('onboarding.language.sourceTitle') }}
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                {{ translate('onboarding.language.sourceDescription') }}
            </p>
            <LanguageSelect v-model:selected="sourceLanguages" :options="languages" :disabled="isAutoMode" />
        </div>

        <!-- Target Languages Section (only shown when source languages are selected) -->
        <div v-if="isAutoMode || sourceLanguages.length > 0">
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('onboarding.language.targetTitle') }}
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                {{ translate('onboarding.language.targetDescription') }}
            </p>
            <LanguageSelect v-model:selected="targetLanguages" :options="targetLanguageOptions" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import LanguageSelect from '@/components/features/settings/LanguageSelect.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
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
    const sourceOptions = isAutoMode.value && sourceLanguages.value.length === 0
        ? languages.value
        : sourceLanguages.value
    if (sourceOptions.length === 0) {
        return []
    }

    // Get all target codes from selected source languages
    const allTargets = sourceOptions.flatMap((sourceLanguage) => {
        const sourceTargetSet = languages.value.find((lang) => lang.code === sourceLanguage.code)
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

const autoModeEnabled = computed({
    get: (): string =>
        (settingStore.getSetting(SETTINGS.SOURCE_LANGUAGE_MODE) as string) === 'auto'
            ? 'true'
            : 'false',
    set: (newValue: string): void => {
        settingStore.updateSetting(
            SETTINGS.SOURCE_LANGUAGE_MODE,
            newValue === 'true' ? 'auto' : 'manual',
            true
        )
    }
})

const isAutoMode = computed((): boolean => autoModeEnabled.value === 'true')

// Load languages on mount
onMounted(() => {
    translateStore.setLanguages()
})
</script>

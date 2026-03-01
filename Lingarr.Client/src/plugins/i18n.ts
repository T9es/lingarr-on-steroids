import { App, ref, Ref, computed, inject, InjectionKey } from 'vue'
import { I18n, I18nPluginOptions, Translations, Language } from '@/ts/plugins/i18n'
import { useLocalStorage } from '@/composables/useLocalStorage'
import services from '@/services'
import { SUPPORTED_LOCALES, type ILocale } from '@/ts'

export const I18nInjectionKey: InjectionKey<I18n> = Symbol('i18n')

/**
 * Detects the user's preferred language from browser settings.
 * Implements a priority chain:
 * 1. Check navigator.languages array (most browsers)
 * 2. Fall back to navigator.language (older browsers)
 * 3. Try to match primary language code (e.g., 'de' from 'de-DE')
 * 4. Return 'en' as ultimate fallback
 */
function detectBrowserLocale(): ILocale {
    // Get browser languages (most recent browsers support navigator.languages)
    const browserLanguages = navigator.languages || [navigator.language]

    // Try each browser language preference in order
    for (const browserLang of browserLanguages) {
        if (!browserLang) continue

        // Extract primary language code (e.g., 'de' from 'de-DE' or 'de-AT')
        const primaryCode = browserLang.split('-')[0].toLowerCase()

        // Check if we support this language
        if (SUPPORTED_LOCALES.includes(primaryCode as ILocale)) {
            return primaryCode as ILocale
        }

        // Special case: Try to match Chinese variants
        // zh-CN, zh-TW, zh-HK all map to our 'zh' (Simplified Chinese)
        if (primaryCode === 'zh') {
            return 'zh'
        }
    }

    // Ultimate fallback: English
    return 'en'
}

/**
 * Validates if a locale string is a supported locale.
 * Returns the validated locale or 'en' as fallback.
 */
function validateLocale(locale: string | null | undefined): ILocale {
    if (!locale) return 'en'

    // Check if it's a valid supported locale
    if (SUPPORTED_LOCALES.includes(locale as ILocale)) {
        return locale as ILocale
    }

    // Invalid locale, fallback to English
    console.warn(`Invalid locale "${locale}" provided. Falling back to English.`)
    return 'en'
}

export function createI18nPlugin(options: I18nPluginOptions = {}) {
    const localStorage = useLocalStorage()

    // Priority chain for initial locale:
    // 1. User's explicit choice stored in localStorage
    // 2. Default locale from plugin options (if configured)
    // 3. Browser language detection (first visit)
    // 4. Ultimate fallback to English
    const storedLocale = localStorage.getItem<string>('locale')
    const defaultLocale = options.defaultLocale ? validateLocale(options.defaultLocale) : null

    let initialLocale: ILocale
    if (storedLocale && typeof storedLocale === 'string') {
        initialLocale = validateLocale(storedLocale)
    } else if (defaultLocale) {
        initialLocale = defaultLocale
    } else {
        initialLocale = detectBrowserLocale()
    }

    const currentLocale: Ref<string> = ref(initialLocale)
    const messages = ref<Record<string, Translations>>({})
    const availableLanguages: Ref<Language[]> = ref<Language[]>([])

    const translate = (key: string, args?: Record<string, string | number>): string => {
        const keys = key.split('.')
        let value: string | Translations = messages.value[currentLocale.value] || {}

        for (const k of keys) {
            if (typeof value === 'object' && k in value) {
                value = value[k]
            } else {
                console.warn(`Translation key not found: ${key}`)
                return key
            }
        }

        if (typeof value === 'string') {
            if (args) {
                return Object.entries(args).reduce((acc, [argKey, argValue]) => {
                    return acc.replace(`{${argKey}}`, String(argValue))
                }, value)
            }
            return value
        }

        return key
    }

    const loadTranslations = async (languages = true) => {
        if (languages) {
            const languagesResponse = await fetch(`/api/translation/languages`)
            availableLanguages.value = await languagesResponse.json()
        }
        const response = await fetch(`/api/translation`)
        const data = await response.json()
        messages.value = { [data.locale]: data.messages }
        currentLocale.value = data.locale
        localStorage.setItem('locale', data.locale)
    }

    const setLocale = async (locale: string) => {
        // Save preference to backend database
        await services.setting.setSetting('locale', locale)
        // Dynamically reload translations (Vue reactivity handles UI updates)
        await loadTranslations(false)
    }

    const i18n: I18n = {
        translate,
        loadTranslations,
        setLocale,
        locale: computed(() => currentLocale.value),
        languages: computed(() => availableLanguages.value)
    }

    const plugin = {
        install(app: App) {
            // injection for i18n
            app.provide(I18nInjectionKey, i18n)

            // helper for translations
            app.config.globalProperties.translate = translate

            // directive for translations
            app.directive('translate', {
                mounted(el, binding) {
                    el.innerHTML = translate(binding.value)
                },
                updated(el, binding) {
                    el.innerHTML = translate(binding.value)
                }
            })
        }
    }

    return { plugin, i18n }
}

// Composable for use in components
export function useI18n(): I18n {
    const i18n = inject(I18nInjectionKey)
    if (!i18n) throw new Error('i18n plugin not installed')
    return i18n
}

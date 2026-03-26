import { createApp } from 'vue'
import { createPinia } from 'pinia'

import { useTranslationRequestStore } from '@/store/translationRequest'
import { useInstanceStore } from '@/store/instance'
import { useSettingStore } from '@/store/setting'
import { createI18nPlugin } from '@/plugins/i18n'

import router from '@/router'
import App from './App.vue'

const { plugin: i18nPlugin, i18n } = createI18nPlugin({
    defaultLocale: 'en'
})
import { highlight, showTitle } from '@/directives'
import '@/assets/style.css'
import './utils'

const pinia = createPinia()
const app = createApp(App)

app.use(i18nPlugin)
app.directive('highlight', highlight)
app.directive('show-title', showTitle)
app.use(pinia)
app.use(router)

const bootstrap = async () => {
    try {
        await i18n.loadTranslations()
        await useSettingStore(pinia).applySettingsOnLoad()
        await useInstanceStore(pinia).applyVersionOnLoad()
        await useTranslationRequestStore(pinia).getActiveCount()
    } catch (error) {
        console.error('Failed to initialize app bootstrap:', error)
    } finally {
        app.mount('#app')
    }
}

void bootstrap()

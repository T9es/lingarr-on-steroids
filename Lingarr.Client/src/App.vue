<template>
    <router-view></router-view>
    <OnboardingWizard v-if="showOnboarding" />
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useSignalR } from '@/composables/useSignalR'
import { Hub, ISettings, SETTINGS } from '@/ts'
import { useSettingStore } from '@/store/setting'
import { useTranslationRequestStore } from '@/store/translationRequest'
import { useOnboardingStore } from '@/store/onboarding'
import OnboardingWizard from '@/components/features/onboarding/OnboardingWizard.vue'

const translationRequestStore = useTranslationRequestStore()
const settingStore = useSettingStore()
const onboardingStore = useOnboardingStore()
const signalR = useSignalR()
const settingHubConnection = ref<Hub>()
const requestHubConnection = ref<Hub>()
let activeCountReconciliationInterval: number | null = null

const showOnboarding = computed(() => {
    if (onboardingStore.isActive) return true
    const completed = settingStore.getSetting(SETTINGS.ONBOARDING_COMPLETED) === 'true'
    const skipped = settingStore.getSetting(SETTINGS.ONBOARDING_SKIPPED) === 'true'
    return !completed && !skipped
})

const handleRequestActive = (request: { count: number }) => {
    translationRequestStore.setActiveCount(request.count)
}

const handleSettingUpdate = (setting: { key: keyof ISettings; value: string }) => {
    settingStore.storeSetting(setting.key, setting.value)
}

onMounted(async () => {
    settingHubConnection.value = await signalR.connect('SettingUpdates', '/signalr/SettingUpdates')
    await settingHubConnection.value.joinGroup({ group: 'SettingUpdates' })
    settingHubConnection.value.on('SettingUpdate', handleSettingUpdate)

    requestHubConnection.value = await signalR.connect(
        'TranslationRequests',
        '/signalr/TranslationRequests'
    )
    await requestHubConnection.value.joinGroup({ group: 'TranslationRequests' })
    requestHubConnection.value.on('RequestActive', handleRequestActive)

    await translationRequestStore.getActiveCount()
    activeCountReconciliationInterval = window.setInterval(() => {
        void translationRequestStore.getActiveCount()
    }, 30000)

    // Check if onboarding should start
    if (showOnboarding.value && !onboardingStore.isActive) {
        onboardingStore.start()
    }
})

onUnmounted(async () => {
    settingHubConnection.value?.off('SettingUpdate', handleSettingUpdate)
    requestHubConnection.value?.off('RequestActive', handleRequestActive)
    if (activeCountReconciliationInterval) {
        clearInterval(activeCountReconciliationInterval)
        activeCountReconciliationInterval = null
    }
})
</script>

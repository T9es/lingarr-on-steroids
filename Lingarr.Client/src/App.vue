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

const showOnboarding = computed(() => {
    if (onboardingStore.isActive) return true
    const completed = settingStore.getSetting(SETTINGS.ONBOARDING_COMPLETED) === 'true'
    const skipped = settingStore.getSetting(SETTINGS.ONBOARDING_SKIPPED) === 'true'
    return !completed && !skipped
})

onMounted(async () => {
    settingHubConnection.value = await signalR.connect('SettingUpdates', '/signalr/SettingUpdates')
    await settingHubConnection.value.joinGroup({ group: 'SettingUpdates' })
    settingHubConnection.value.on(
        'SettingUpdate',
        (setting: { key: keyof ISettings; value: string }) => {
            settingStore.storeSetting(setting.key, setting.value)
        }
    )

    requestHubConnection.value = await signalR.connect(
        'TranslationRequests',
        '/signalr/TranslationRequests'
    )
    await requestHubConnection.value.joinGroup({ group: 'TranslationRequests' })
    requestHubConnection.value.on('RequestActive', (request: { count: number }) => {
        translationRequestStore.setActiveCount(request.count)
    })

    await translationRequestStore.getActiveCount()

    // Check if onboarding should start
    if (showOnboarding.value && !onboardingStore.isActive) {
        onboardingStore.start()
    }
})

onUnmounted(async () => {
    settingHubConnection.value?.off('SettingUpdate', () => {})
    requestHubConnection.value?.off('RequestActive', () => {})
})
</script>

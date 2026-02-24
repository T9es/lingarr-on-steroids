<template>
    <div class="space-y-6 p-6">
        <CardComponent title="Onboarding Assistant">
            <template #description>
                The onboarding wizard helps you configure Lingarr for the first time. It will guide
                you through connecting Radarr/Sonarr, selecting a translation service, and choosing
                your languages.
            </template>
            <template #content>
                <div class="flex flex-col items-center justify-center space-y-4">
                    <div v-if="onboardingCompleted" class="text-green-500">
                        Onboarding was completed previously.
                    </div>
                    <div v-if="onboardingSkipped" class="text-yellow-500">
                        Onboarding was skipped previously.
                    </div>
                    <button
                        class="bg-accent rounded-md px-4 py-2 text-white transition-all hover:brightness-125"
                        @click="startOnboarding">
                        Start Onboarding Wizard
                    </button>
                </div>
            </template>
        </CardComponent>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue'
import CardComponent from '@/components/common/CardComponent.vue'
import { useOnboardingStore } from '@/store/onboarding'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts/setting'

const onboardingStore = useOnboardingStore()
const settingStore = useSettingStore()

const onboardingCompleted = computed(() => {
    return settingStore.getSetting(SETTINGS.ONBOARDING_COMPLETED) === 'true'
})

const onboardingSkipped = computed(() => {
    return settingStore.getSetting(SETTINGS.ONBOARDING_SKIPPED) === 'true'
})

const startOnboarding = () => {
    onboardingStore.start()
}

onMounted(() => {
    settingStore.applySettingsOnLoad()
})
</script>

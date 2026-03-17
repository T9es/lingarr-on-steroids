<template>
    <div class="space-y-6 p-6">
        <CardComponent :title="translate('help.onboarding.title')">
            <template #description>
                {{ translate('help.onboarding.description') }}
            </template>
            <template #content>
                <div class="flex flex-col items-center justify-center space-y-4">
                    <div v-if="onboardingCompleted" class="text-green-500">
                        {{ translate('help.onboarding.completed') }}
                    </div>
                    <div v-else-if="onboardingSkipped" class="text-yellow-500">
                        {{ translate('help.onboarding.skipped') }}
                    </div>
                    <button
                        class="bg-accent text-primary-content hover:bg-accent/80 rounded-md px-4 py-2 transition-colors"
                        @click="startOnboarding">
                        {{ translate('help.onboarding.startButton') }}
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
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

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

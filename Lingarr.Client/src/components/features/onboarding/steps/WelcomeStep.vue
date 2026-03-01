<template>
    <div class="flex flex-col items-center justify-center space-y-6 py-8 text-center">
        <div class="bg-accent/10 text-accent rounded-full p-4">
            <RocketIcon class="h-12 w-12" />
        </div>

        <div class="space-y-2">
            <h3 class="text-primary-content text-2xl font-bold">
                {{ translate('onboarding.welcome.title') }}
            </h3>
            <p class="text-secondary-content max-w-md text-base">
                {{ translate('onboarding.welcome.description') }}
            </p>
        </div>

        <div class="flex flex-col gap-3 pt-4">
            <button
                class="bg-accent text-primary-content hover:bg-accent/80 w-full rounded-md px-8 py-3 text-base font-medium transition-all hover:scale-[1.02]"
                @click="handleStart">
                {{ translate('onboarding.welcome.startButton') }}
            </button>
            <button
                class="text-secondary-content hover:text-primary-content text-sm transition-colors"
                @click="handleSkip">
                {{ translate('onboarding.welcome.skipButton') }}
            </button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { useOnboardingStore } from '@/store/onboarding'
import { useI18n } from '@/plugins/i18n'
import RocketIcon from '@/components/icons/RocketIcon.vue'

const { translate } = useI18n()
const onboardingStore = useOnboardingStore()

const handleStart = () => {
    onboardingStore.next()
}

const handleSkip = async () => {
    await onboardingStore.skip()
}
</script>

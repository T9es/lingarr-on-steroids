<template>
    <div
        v-if="onboardingStore.isActive"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
        <div
            class="bg-secondary flex w-full max-w-2xl max-h-[90vh] flex-col overflow-hidden rounded-md shadow-lg">
            <!-- Header with theme selector and skip button -->
            <header class="border-accent flex items-center justify-between border-b px-6 py-4">
                <div class="flex items-center gap-3">
                    <DropdownComponent width="medium">
                        <template #button>
                            <ThemeIcon class="h-5 w-5 cursor-pointer" />
                        </template>
                        <template #content>
                            <div class="py-1" role="menu">
                                <button
                                    v-for="theme in Object.values(THEMES)"
                                    :key="theme"
                                    class="hover:bg-secondary-focus text-secondary-content block w-full cursor-pointer px-4 py-2 text-left text-sm capitalize"
                                    @click="setTheme(theme)">
                                    {{ theme }}
                                </button>
                            </div>
                        </template>
                    </DropdownComponent>
                </div>
                <button
                    class="text-secondary-content hover:text-primary-content cursor-pointer text-sm transition-colors"
                    @click="handleSkip">
                    Skip Setup
                </button>
            </header>

            <!-- Progress indicator -->
            <div class="border-accent flex items-center justify-center gap-2 border-b px-6 py-4">
                <button
                    v-for="(step, index) in onboardingStore.steps"
                    :key="step.id"
                    class="h-2 w-2 rounded-full transition-all"
                    :class="[
                        index === onboardingStore.currentStep
                            ? 'bg-accent w-6'
                            : index < onboardingStore.currentStep
                              ? 'bg-accent/60'
                              : 'bg-secondary-content/30'
                    ]"
                    :title="step.title"
                    @click="onboardingStore.goToStep(index)" />
            </div>

            <!-- Step content -->
            <div class="flex-1 overflow-y-auto px-6 py-6">
                <div v-if="currentStepData" class="space-y-4">
                    <h2 class="text-primary-content text-xl font-bold">
                        {{ currentStepData.title }}
                    </h2>
                    <p class="text-secondary-content/80">
                        {{ currentStepData.description }}
                    </p>
                    <component :is="currentStepComponent" :key="currentStepData.id" />
                </div>
            </div>

            <!-- Navigation buttons -->
            <footer class="border-accent flex items-center justify-between border-t px-6 py-4">
                <button
                    v-if="!isFirstStep"
                    class="border-accent text-secondary-content hover:bg-secondary-focus cursor-pointer rounded-md border px-4 py-2 text-sm transition-colors"
                    @click="onboardingStore.previous()">
                    Back
                </button>
                <div v-else />

                <div class="flex gap-3">
                    <button
                        v-if="!isLastStep"
                        class="bg-accent text-primary-content hover:bg-accent/80 cursor-pointer rounded-md px-6 py-2 text-sm font-medium transition-colors"
                        @click="onboardingStore.next()">
                        Next
                    </button>
                    <button
                        v-else
                        class="bg-accent text-primary-content hover:bg-accent/80 cursor-pointer rounded-md px-6 py-2 text-sm font-medium transition-colors"
                        @click="handleComplete">
                        Complete
                    </button>
                </div>
            </footer>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useOnboardingStore } from '@/store/onboarding'
import { useInstanceStore } from '@/store/instance'
import { THEMES, type ITheme } from '@/ts'
import DropdownComponent from '@/components/common/DropdownComponent.vue'
import ThemeIcon from '@/components/icons/ThemeIcon.vue'

const onboardingStore = useOnboardingStore()
const instanceStore = useInstanceStore()

// Step component mapping
const stepComponents: Record<string, any> = {
    IntegrationStep: defineAsyncComponent(
        () => import('./steps/IntegrationStep.vue')
    ),
    ServiceStep: defineAsyncComponent(() => import('./steps/ServiceStep.vue')),
    ServiceConfigStep: defineAsyncComponent(
        () => import('./steps/ServiceConfigStep.vue')
    ),
    LanguageStep: defineAsyncComponent(() => import('./steps/LanguageStep.vue')),
    TranslationSettingsStep: defineAsyncComponent(
        () => import('./steps/TranslationSettingsStep.vue')
    ),
    CompleteStep: defineAsyncComponent(
        () => import('./steps/CompleteStep.vue')
    )
}

// Computed properties
const currentStepData = computed(() => onboardingStore.currentStepData)
const isFirstStep = computed(() => onboardingStore.currentStep === 0)
const isLastStep = computed(
    () => onboardingStore.currentStep === onboardingStore.steps.length - 1
)

const currentStepComponent = computed(() => {
    const componentName = currentStepData.value?.component
    return componentName ? stepComponents[componentName] : null
})

// Methods
const setTheme = (theme: ITheme) => {
    instanceStore.storeTheme(theme)
}

const handleSkip = () => {
    onboardingStore.skip()
}

const handleComplete = () => {
    onboardingStore.complete()
}
</script>

<template>
    <div
        v-if="onboardingStore.isActive"
        class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
        <div
            class="bg-secondary flex max-h-[90vh] w-full max-w-2xl flex-col overflow-hidden rounded-md shadow-lg">
            <!-- Header with language selector, theme selector and skip button -->
            <header
                class="border-accent flex items-center justify-between overflow-visible border-b px-6 py-4">
                <div class="relative flex items-center gap-3">
                    <!-- Language Selector -->
                    <LanguageSelect />
                    
                    <!-- Theme Selector -->
                    <button
                        class="inline-flex h-10 w-10 cursor-pointer items-center justify-center"
                        @click="toggleThemeDropdown">
                        <ThemeIcon
                            class="text-secondary-content hover:text-primary-content h-5 w-5 cursor-pointer transition-colors" />
                    </button>
                    <Teleport to="body">
                        <transition
                            enter-active-class="transition ease-out duration-100"
                            enter-from-class="transform opacity-0 scale-95"
                            enter-to-class="transform opacity-100 scale-100"
                            leave-active-class="transition ease-in duration-75"
                            leave-from-class="transform opacity-100 scale-100"
                            leave-to-class="transform opacity-0 scale-95">
                            <div
                                v-if="isThemeDropdownOpen"
                                ref="themeDropdownRef"
                                class="border-accent bg-secondary fixed z-[100] mt-2 w-48 origin-top-right rounded-md border shadow-lg"
                                :style="{
                                    top: themeDropdownPosition.top + 'px',
                                    left: themeDropdownPosition.left + 'px'
                                }">
                                <div class="py-1" role="menu">
                                    <button
                                        v-for="theme in Object.values(THEMES)"
                                        :key="theme"
                                        class="hover:bg-secondary-focus text-secondary-content block w-full cursor-pointer px-4 py-2 text-left text-sm capitalize"
                                        @click="setTheme(theme)">
                                        {{ theme }}
                                    </button>
                                </div>
                            </div>
                        </transition>
                    </Teleport>
                </div>
                <button
                    class="text-secondary-content hover:text-primary-content cursor-pointer text-sm transition-colors"
                    @click="handleSkip">
                    {{ translate('onboarding.skipSetup') }}
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
                    <p class="text-secondary-content">
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
                    {{ translate('onboarding.back') }}
                </button>
                <div v-else />

                <div class="flex gap-3">
                    <button
                        v-if="!isLastStep"
                        class="bg-accent text-primary-content hover:bg-accent/80 cursor-pointer rounded-md px-6 py-2 text-sm font-medium transition-colors"
                        @click="onboardingStore.next()">
                        {{ translate('onboarding.next') }}
                    </button>
                    <button
                        v-else
                        class="bg-accent text-primary-content hover:bg-accent/80 cursor-pointer rounded-md px-6 py-2 text-sm font-medium transition-colors"
                        @click="handleComplete">
                        {{ translate('onboarding.complete') }}
                    </button>
                </div>
            </footer>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, ref, onMounted, onUnmounted } from 'vue'
import { useOnboardingStore } from '@/store/onboarding'
import { useInstanceStore } from '@/store/instance'
import { THEMES, type ITheme } from '@/ts'
import ThemeIcon from '@/components/icons/ThemeIcon.vue'
import LanguageSelect from '@/components/common/LanguageSelect.vue'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const onboardingStore = useOnboardingStore()
const instanceStore = useInstanceStore()

// Theme dropdown state
const isThemeDropdownOpen = ref(false)
const themeDropdownPosition = ref({ top: 0, left: 0 })
const themeButtonRef = ref<HTMLElement | null>(null)

// Step component mapping
const stepComponents: Record<string, any> = {
    IntegrationStep: defineAsyncComponent(() => import('./steps/IntegrationStep.vue')),
    ServiceStep: defineAsyncComponent(() => import('./steps/ServiceStep.vue')),
    ServiceConfigStep: defineAsyncComponent(() => import('./steps/ServiceConfigStep.vue')),
    LanguageStep: defineAsyncComponent(() => import('./steps/LanguageStep.vue')),
    TranslationSettingsStep: defineAsyncComponent(
        () => import('./steps/TranslationSettingsStep.vue')
    ),
    CompleteStep: defineAsyncComponent(() => import('./steps/CompleteStep.vue'))
}

// Computed properties
const currentStepData = computed(() => onboardingStore.currentStepData)
const isFirstStep = computed(() => onboardingStore.currentStep === 0)
const isLastStep = computed(() => onboardingStore.currentStep === onboardingStore.steps.length - 1)

const currentStepComponent = computed(() => {
    const componentName = currentStepData.value?.component
    return componentName ? stepComponents[componentName] : null
})

// Theme dropdown methods
const toggleThemeDropdown = (event: MouseEvent) => {
    isThemeDropdownOpen.value = !isThemeDropdownOpen.value
    if (isThemeDropdownOpen.value) {
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
        themeDropdownPosition.value = {
            top: rect.bottom + window.scrollY,
            left: rect.left + window.scrollX
        }
    }
}

const setTheme = (theme: ITheme) => {
    instanceStore.storeTheme(theme)
    isThemeDropdownOpen.value = false
}

// Close dropdown when clicking outside
const handleClickOutside = (event: MouseEvent) => {
    if (themeButtonRef.value && !themeButtonRef.value.contains(event.target as Node)) {
        isThemeDropdownOpen.value = false
    }
}

onMounted(() => {
    document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
    document.removeEventListener('click', handleClickOutside)
})

// Methods
const handleSkip = async () => {
    await onboardingStore.skip()
}

const handleComplete = async () => {
    await onboardingStore.complete()
}
</script>

import { acceptHMRUpdate, defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { IInstance } from '@/ts/setting'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import config from '@/config/onboarding-config.json'

export const useOnboardingStore = defineStore('onboarding', () => {
    // State
    const isActive = ref(false)
    const currentStep = ref(0)
    const skipped = ref(false)
    const radarrInstances = ref<IInstance[]>([])
    const sonarrInstances = ref<IInstance[]>([])

    // Getters
    const steps = computed(() => config.steps)
    const isComplete = computed(() => currentStep.value >= steps.value.length)
    const currentStepData = computed(() => steps.value[currentStep.value])

    // Actions
    function start(): void {
        isActive.value = true
        currentStep.value = 0
        skipped.value = false
    }

    function next(): void {
        if (currentStep.value < steps.value.length - 1) {
            currentStep.value++
        }
    }

    function previous(): void {
        if (currentStep.value > 0) {
            currentStep.value--
        }
    }

    async function skip(): Promise<void> {
        const settingStore = useSettingStore()
        await settingStore.saveSetting(SETTINGS.ONBOARDING_SKIPPED, 'true')
        skipped.value = true
        isActive.value = false
    }

    async function complete(): Promise<void> {
        const settingStore = useSettingStore()
        await settingStore.saveSetting(SETTINGS.ONBOARDING_COMPLETED, 'true')
        isActive.value = false
    }

    function addRadarrInstance(instance: IInstance): void {
        radarrInstances.value.push(instance)
    }

    function removeRadarrInstance(id: string): void {
        radarrInstances.value = radarrInstances.value.filter(
            (instance) => instance.id !== id
        )
    }

    function addSonarrInstance(instance: IInstance): void {
        sonarrInstances.value.push(instance)
    }

    function removeSonarrInstance(id: string): void {
        sonarrInstances.value = sonarrInstances.value.filter(
            (instance) => instance.id !== id
        )
    }

    function goToStep(index: number): void {
        if (index >= 0 && index < steps.value.length) {
            currentStep.value = index
        }
    }

    return {
        // State
        isActive,
        currentStep,
        skipped,
        radarrInstances,
        sonarrInstances,
        // Getters
        steps,
        isComplete,
        currentStepData,
        // Actions
        start,
        next,
        previous,
        skip,
        complete,
        addRadarrInstance,
        removeRadarrInstance,
        addSonarrInstance,
        removeSonarrInstance,
        goToStep
    }
})

if (import.meta.hot) {
    import.meta.hot.accept(acceptHMRUpdate(useOnboardingStore, import.meta.hot))
}

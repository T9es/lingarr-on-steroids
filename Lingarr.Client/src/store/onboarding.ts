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

        // Migrate legacy settings when starting onboarding
        migrateLegacySettings()
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
        radarrInstances.value = radarrInstances.value.filter((instance) => instance.id !== id)
    }

    function addSonarrInstance(instance: IInstance): void {
        sonarrInstances.value.push(instance)
    }

    function removeSonarrInstance(id: string): void {
        sonarrInstances.value = sonarrInstances.value.filter((instance) => instance.id !== id)
    }

    function goToStep(index: number): void {
        if (index >= 0 && index < steps.value.length) {
            currentStep.value = index
        }
    }

    /**
     * Migrate legacy single-instance settings to multi-instance arrays.
     * This is called when onboarding starts to pre-populate instance arrays
     * with existing legacy settings if the arrays are empty.
     *
     * IMPORTANT: Uses 'default' as the instance ID to match backend fallback behavior.
     * This prevents duplicate records when upgrading from pre-multi-instance versions.
     */
    function migrateLegacySettings(): void {
        const settingStore = useSettingStore()

        // Only migrate if instance arrays are empty
        if (radarrInstances.value.length === 0) {
            const radarrUrl = settingStore.getSetting(SETTINGS.RADARR_URL) as string
            const radarrApiKey = settingStore.getSetting(SETTINGS.RADARR_API_KEY) as string

            if (radarrUrl || radarrApiKey) {
                const instance: IInstance = {
                    id: 'default', // Use 'default' to match backend fallback
                    name: 'Radarr',
                    url: radarrUrl || '',
                    apiKey: radarrApiKey || ''
                }
                radarrInstances.value.push(instance)
            }
        }

        if (sonarrInstances.value.length === 0) {
            const sonarrUrl = settingStore.getSetting(SETTINGS.SONARR_URL) as string
            const sonarrApiKey = settingStore.getSetting(SETTINGS.SONARR_API_KEY) as string

            if (sonarrUrl || sonarrApiKey) {
                const instance: IInstance = {
                    id: 'default', // Use 'default' to match backend fallback
                    name: 'Sonarr',
                    url: sonarrUrl || '',
                    apiKey: sonarrApiKey || ''
                }
                sonarrInstances.value.push(instance)
            }
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
        goToStep,
        migrateLegacySettings
    }
})

if (import.meta.hot) {
    import.meta.hot.accept(acceptHMRUpdate(useOnboardingStore, import.meta.hot))
}

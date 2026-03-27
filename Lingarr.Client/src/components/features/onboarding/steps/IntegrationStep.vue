<template>
    <div class="space-y-8">
        <!-- Radarr Section -->
        <div>
            <div class="mb-4 flex items-center gap-2">
                <RadarrIcon class="text-primary-content h-6 w-6" />
                <h3 class="text-primary-content text-lg font-semibold">Radarr</h3>
            </div>
            <!-- Empty state for Radarr -->
            <div
                v-if="onboardingStore.radarrInstances.length === 0"
                class="border-accent/30 bg-primary/50 rounded-lg border-2 border-dashed p-8 text-center">
                <RadarrIcon class="text-secondary-content mx-auto mb-3 h-10 w-10 opacity-50" />
                <p class="text-secondary-content mb-3">
                    {{ translate('onboarding.integration.noRadarrInstances') }}
                </p>
                <button
                    class="bg-accent text-primary-content hover:bg-accent/80 rounded-md px-4 py-2 text-sm font-medium transition-colors"
                    @click="addRadarrInstance">
                    {{ translate('onboarding.integration.addFirstRadarr') }}
                </button>
            </div>
            <!-- Instance cards grid -->
            <div v-else class="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                <InstanceCard
                    v-for="instance in onboardingStore.radarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="radarr"
                    :connection-status="getConnectionStatus('radarr', instance.id)"
                    @update:instance="updateRadarrInstance(instance.id, $event)"
                    @remove="removeRadarrInstance(instance.id)"
                    @test-connection="testRadarrConnection(instance)" />

                <AddInstanceButton
                    v-if="onboardingStore.radarrInstances.length < 5"
                    type="radarr"
                    @add="addRadarrInstance" />
            </div>
        </div>

        <!-- Sonarr Section -->
        <div>
            <div class="mb-4 flex items-center gap-2">
                <SonarrIcon class="text-primary-content h-6 w-6" />
                <h3 class="text-primary-content text-lg font-semibold">Sonarr</h3>
            </div>
            <!-- Empty state for Sonarr -->
            <div
                v-if="onboardingStore.sonarrInstances.length === 0"
                class="border-accent/30 bg-primary/50 rounded-lg border-2 border-dashed p-8 text-center">
                <SonarrIcon class="text-secondary-content mx-auto mb-3 h-10 w-10 opacity-50" />
                <p class="text-secondary-content mb-3">
                    {{ translate('onboarding.integration.noSonarrInstances') }}
                </p>
                <button
                    class="bg-accent text-primary-content hover:bg-accent/80 rounded-md px-4 py-2 text-sm font-medium transition-colors"
                    @click="addSonarrInstance">
                    {{ translate('onboarding.integration.addFirstSonarr') }}
                </button>
            </div>
            <!-- Instance cards grid -->
            <div v-else class="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                <InstanceCard
                    v-for="instance in onboardingStore.sonarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="sonarr"
                    :connection-status="getConnectionStatus('sonarr', instance.id)"
                    @update:instance="updateSonarrInstance(instance.id, $event)"
                    @remove="removeSonarrInstance(instance.id)"
                    @test-connection="testSonarrConnection(instance)" />

                <AddInstanceButton
                    v-if="onboardingStore.sonarrInstances.length < 5"
                    type="sonarr"
                    @add="addSonarrInstance" />
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { reactive, onMounted } from 'vue'
import { useOnboardingStore } from '@/store/onboarding'
import { SETTINGS } from '@/ts'
import type { IInstance } from '@/ts/setting'
import InstanceCard from '@/components/features/onboarding/InstanceCard.vue'
import AddInstanceButton from '@/components/features/onboarding/AddInstanceButton.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'

const { translate } = useI18n()
const onboardingStore = useOnboardingStore()

interface ConnectionStatus {
    testing: boolean
    tested: boolean
    connected: boolean
    message: string
    version: string | null
}

interface ConnectionTestResult {
    isConnected: boolean
    message?: string
    version?: string
}

// Connection status per instance
const connectionStatuses = reactive<Record<string, Record<string, ConnectionStatus>>>({
    radarr: {},
    sonarr: {}
})

// Generate unique ID
const generateId = (): string => {
    return `${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
}

// Get connection status for an instance
const getConnectionStatus = (type: 'radarr' | 'sonarr', id: string): ConnectionStatus => {
    return (
        connectionStatuses[type][id] || {
            testing: false,
            tested: false,
            connected: false,
            message: '',
            version: null
        }
    )
}

// Initialize connection status for an instance
const initConnectionStatus = (type: 'radarr' | 'sonarr', id: string): void => {
    if (!connectionStatuses[type][id]) {
        connectionStatuses[type][id] = {
            testing: false,
            tested: false,
            connected: false,
            message: '',
            version: null
        }
    }
}

// Add Radarr instance
const addRadarrInstance = (): void => {
    const id = generateId()
    const instance: IInstance = {
        id,
        name: `Radarr ${onboardingStore.radarrInstances.length + 1}`,
        url: '',
        apiKey: ''
    }
    initConnectionStatus('radarr', id)
    onboardingStore.radarrInstances = [...onboardingStore.radarrInstances, instance]
}

// Add Sonarr instance
const addSonarrInstance = (): void => {
    const id = generateId()
    const instance: IInstance = {
        id,
        name: `Sonarr ${onboardingStore.sonarrInstances.length + 1}`,
        url: '',
        apiKey: ''
    }
    initConnectionStatus('sonarr', id)
    onboardingStore.sonarrInstances = [...onboardingStore.sonarrInstances, instance]
}

// Update Radarr instance
const updateRadarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = onboardingStore.radarrInstances.findIndex((inst) => inst.id === id)
    if (index !== -1) {
        const newInstances = [...onboardingStore.radarrInstances]
        newInstances[index] = updatedInstance
        onboardingStore.radarrInstances = newInstances
    }
}

// Update Sonarr instance
const updateSonarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = onboardingStore.sonarrInstances.findIndex((inst) => inst.id === id)
    if (index !== -1) {
        const newInstances = [...onboardingStore.sonarrInstances]
        newInstances[index] = updatedInstance
        onboardingStore.sonarrInstances = newInstances
    }
}

// Remove Radarr instance
const removeRadarrInstance = (id: string): void => {
    onboardingStore.radarrInstances = onboardingStore.radarrInstances.filter(
        (inst) => inst.id !== id
    )
    delete connectionStatuses.radarr[id]
}

// Remove Sonarr instance
const removeSonarrInstance = (id: string): void => {
    onboardingStore.sonarrInstances = onboardingStore.sonarrInstances.filter(
        (inst) => inst.id !== id
    )
    delete connectionStatuses.sonarr[id]
}

// Test Radarr connection
const testRadarrConnection = async (instance: IInstance): Promise<void> => {
    const status = connectionStatuses.radarr[instance.id]
    if (!status) return

    status.testing = true
    status.tested = false

    try {
        // Temporarily save settings to test connection
        await services.setting.setSetting(SETTINGS.RADARR_URL, instance.url)
        await services.setting.setSetting(SETTINGS.RADARR_API_KEY, instance.apiKey)

        const result = await services.setting.testRadarrConnection<ConnectionTestResult>()
        status.connected = result.isConnected
        status.message = result.message || ''
        status.version = result.version || null
    } catch (error) {
        status.connected = false
        status.message = translate('onboarding.instanceCard.connectionFailed')
    } finally {
        status.testing = false
        status.tested = true
    }
}

// Test Sonarr connection
const testSonarrConnection = async (instance: IInstance): Promise<void> => {
    const status = connectionStatuses.sonarr[instance.id]
    if (!status) return

    status.testing = true
    status.tested = false

    try {
        // Temporarily save settings to test connection
        await services.setting.setSetting(SETTINGS.SONARR_URL, instance.url)
        await services.setting.setSetting(SETTINGS.SONARR_API_KEY, instance.apiKey)

        const result = await services.setting.testSonarrConnection<ConnectionTestResult>()
        status.connected = result.isConnected
        status.message = result.message || ''
        status.version = result.version || null
    } catch (error) {
        status.connected = false
        status.message = translate('onboarding.instanceCard.connectionFailed')
    } finally {
        status.testing = false
        status.tested = true
    }
}

// Initialize connection statuses for existing instances
const initExistingInstances = (): void => {
    onboardingStore.radarrInstances.forEach((inst) => {
        initConnectionStatus('radarr', inst.id)
    })

    onboardingStore.sonarrInstances.forEach((inst) => {
        initConnectionStatus('sonarr', inst.id)
    })
}

// Run initialization on mount
onMounted(() => {
    initExistingInstances()
})
</script>

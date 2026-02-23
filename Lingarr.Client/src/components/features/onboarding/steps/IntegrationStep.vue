<template>
    <div class="space-y-6">
        <!-- Radarr Section -->
        <div>
            <h3 class="text-primary-content mb-3 text-lg font-semibold">{{ translate('onboarding.integration.radarr') }}</h3>
            <div class="space-y-3">
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
                    @add-radarr="addRadarrInstance"
                    @add-sonarr="addSonarrInstance" />
            </div>
        </div>

        <!-- Sonarr Section -->
        <div>
            <h3 class="text-primary-content mb-3 text-lg font-semibold">{{ translate('onboarding.integration.sonarr') }}</h3>
            <div class="space-y-3">
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
                    @add-radarr="addRadarrInstance"
                    @add-sonarr="addSonarrInstance" />
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { onMounted, reactive } from 'vue'
import { useOnboardingStore } from '@/store/onboarding'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'
import type { IInstance } from '@/ts/setting'
import InstanceCard from '@/components/features/onboarding/InstanceCard.vue'
import AddInstanceButton from '@/components/features/onboarding/AddInstanceButton.vue'
import services from '@/services'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

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

const onboardingStore = useOnboardingStore()
const settingStore = useSettingStore()

// Connection status per instance
const connectionStatuses = reactive<
    Record<string, Record<string, ConnectionStatus>>
>({
    radarr: {},
    sonarr: {}
})

// Debounce timers for auto-connect
const debounceTimers = reactive<
    Record<string, Record<string, ReturnType<typeof setTimeout> | null>>
>({
    radarr: {},
    sonarr: {}
})

// Generate unique ID
const generateId = (): string => {
    return `${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
}

// Get connection status for an instance
const getConnectionStatus = (
    type: 'radarr' | 'sonarr',
    id: string
): ConnectionStatus => {
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
        name: 'Radarr',
        url: '',
        apiKey: ''
    }
    initConnectionStatus('radarr', id)
    onboardingStore.addRadarrInstance(instance)
}

// Add Sonarr instance
const addSonarrInstance = (): void => {
    const id = generateId()
    const instance: IInstance = {
        id,
        name: 'Sonarr',
        url: '',
        apiKey: ''
    }
    initConnectionStatus('sonarr', id)
    onboardingStore.addSonarrInstance(instance)
}

// Update Radarr instance
const updateRadarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = onboardingStore.radarrInstances.findIndex(
        (inst) => inst.id === id
    )
    if (index !== -1) {
        onboardingStore.radarrInstances[index] = updatedInstance
        // Trigger auto-connect with debounce
        scheduleAutoConnect('radarr', updatedInstance)
    }
}

// Update Sonarr instance
const updateSonarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = onboardingStore.sonarrInstances.findIndex(
        (inst) => inst.id === id
    )
    if (index !== -1) {
        onboardingStore.sonarrInstances[index] = updatedInstance
        // Trigger auto-connect with debounce
        scheduleAutoConnect('sonarr', updatedInstance)
    }
}

// Remove Radarr instance
const removeRadarrInstance = (id: string): void => {
    onboardingStore.removeRadarrInstance(id)
    delete connectionStatuses.radarr[id]
    if (debounceTimers.radarr[id]) {
        clearTimeout(debounceTimers.radarr[id]!)
        delete debounceTimers.radarr[id]
    }
}

// Remove Sonarr instance
const removeSonarrInstance = (id: string): void => {
    onboardingStore.removeSonarrInstance(id)
    delete connectionStatuses.sonarr[id]
    if (debounceTimers.sonarr[id]) {
        clearTimeout(debounceTimers.sonarr[id]!)
        delete debounceTimers.sonarr[id]
    }
}

// Schedule auto-connect with 500ms debounce
const scheduleAutoConnect = (
    type: 'radarr' | 'sonarr',
    instance: IInstance
): void => {
    // Clear existing timer
    if (debounceTimers[type][instance.id]) {
        clearTimeout(debounceTimers[type][instance.id]!)
    }

    // Validate URL and API key
    const isValidUrl =
        instance.url &&
        instance.url.length > 0 &&
        isValidUrlFormat(instance.url)
    const isValidApiKey =
        instance.apiKey && instance.apiKey.length === 32

    if (!isValidUrl || !isValidApiKey) {
        // Reset connection status if inputs are invalid
        if (connectionStatuses[type][instance.id]) {
            connectionStatuses[type][instance.id].tested = false
        }
        return
    }

    // Set new timer for auto-connect
    debounceTimers[type][instance.id] = setTimeout(() => {
        if (type === 'radarr') {
            testRadarrConnection(instance)
        } else {
            testSonarrConnection(instance)
        }
    }, 500)
}

// Validate URL format
const isValidUrlFormat = (url: string): boolean => {
    try {
        new URL(url)
        return true
    } catch {
        return false
    }
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
        await services.setting.setSetting(
            SETTINGS.RADARR_API_KEY,
            instance.apiKey
        )

        const result =
            await services.setting.testRadarrConnection<ConnectionTestResult>()
        status.connected = result.isConnected
        status.message = result.message || ''
        status.version = result.version || null
    } catch (error) {
        status.connected = false
        status.message = 'Connection failed'
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
        await services.setting.setSetting(
            SETTINGS.SONARR_API_KEY,
            instance.apiKey
        )

        const result =
            await services.setting.testSonarrConnection<ConnectionTestResult>()
        status.connected = result.isConnected
        status.message = result.message || ''
        status.version = result.version || null
    } catch (error) {
        status.connected = false
        status.message = 'Connection failed'
    } finally {
        status.testing = false
        status.tested = true
    }
}

// Pre-populate from existing settings on mount
onMounted(() => {
    const radarrUrl = settingStore.getSetting(SETTINGS.RADARR_URL) as string
    const radarrApiKey = settingStore.getSetting(
        SETTINGS.RADARR_API_KEY
    ) as string
    const sonarrUrl = settingStore.getSetting(SETTINGS.SONARR_URL) as string
    const sonarrApiKey = settingStore.getSetting(
        SETTINGS.SONARR_API_KEY
    ) as string

    // Pre-populate Radarr if settings exist
    if (radarrUrl || radarrApiKey) {
        const id = generateId()
        const instance: IInstance = {
            id,
            name: 'Radarr',
            url: radarrUrl || '',
            apiKey: radarrApiKey || ''
        }
        initConnectionStatus('radarr', id)
        onboardingStore.addRadarrInstance(instance)

        // Auto-test if both values exist
        if (radarrUrl && radarrApiKey) {
            setTimeout(() => testRadarrConnection(instance), 100)
        }
    }

    // Pre-populate Sonarr if settings exist
    if (sonarrUrl || sonarrApiKey) {
        const id = generateId()
        const instance: IInstance = {
            id,
            name: 'Sonarr',
            url: sonarrUrl || '',
            apiKey: sonarrApiKey || ''
        }
        initConnectionStatus('sonarr', id)
        onboardingStore.addSonarrInstance(instance)

        // Auto-test if both values exist
        if (sonarrUrl && sonarrApiKey) {
            setTimeout(() => testSonarrConnection(instance), 100)
        }
    }
})
</script>

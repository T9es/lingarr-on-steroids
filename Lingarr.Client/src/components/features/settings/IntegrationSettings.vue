<template>
    <SaveNotification ref="saveNotification" />

    <!-- Media Servers Section -->
    <CardComponent :title="translate('settings.integrations.header') || 'Media Servers'">
        <template #icon>
            <div class="flex gap-2">
                <RadarrIcon />
                <SonarrIcon />
            </div>
        </template>
        <template #description>
            {{ translate('settings.integrations.description') }}
        </template>
        <template #content>
            <div class="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
                <!-- Radarr Instances -->
                <InstanceCard
                    v-for="instance in radarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="radarr"
                    :connection-status="getConnectionStatus('radarr', instance.id)"
                    @update:instance="updateRadarrInstance(instance.id, $event)"
                    @remove="removeRadarrInstance(instance.id)"
                    @test-connection="testRadarrConnection(instance)" />
                
                <!-- Sonarr Instances -->
                <InstanceCard
                    v-for="instance in sonarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="sonarr"
                    :connection-status="getConnectionStatus('sonarr', instance.id)"
                    @update:instance="updateSonarrInstance(instance.id, $event)"
                    @remove="removeSonarrInstance(instance.id)"
                    @test-connection="testSonarrConnection(instance)" />
                
                <!-- Add New Button -->
                <AddInstanceButton
                    v-if="radarrInstances.length + sonarrInstances.length < 10"
                    @add-radarr="addRadarrInstance"
                    @add-sonarr="addSonarrInstance" />
            </div>
            <div v-translate="'settings.integrations.reindexTask'" class="pt-4 text-sm text-secondary-content" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, ref, reactive, onMounted } from 'vue'
import { useSettingStore } from '@/store/setting'
import SaveNotification from '@/components/common/SaveNotification.vue'
import { SETTINGS } from '@/ts'
import type { IInstance } from '@/ts/setting'
import CardComponent from '@/components/common/CardComponent.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'
import InstanceCard from '@/components/features/onboarding/InstanceCard.vue'
import AddInstanceButton from '@/components/features/onboarding/AddInstanceButton.vue'
import services from '@/services'
import { useI18n } from '@/plugins/i18n'

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

const { translate } = useI18n()

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()

// Connection status per instance
const connectionStatuses = reactive<
    Record<string, Record<string, ConnectionStatus>>
>({
    radarr: {},
    sonarr: {}
})

// Debounce timers for auto-save
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

// Parse instances from settings (handle both string and array formats)
const parseInstances = (value: string | IInstance[]): IInstance[] => {
    if (Array.isArray(value)) {
        return value
    }
    if (typeof value === 'string' && value) {
        try {
            const parsed = JSON.parse(value)
            return Array.isArray(parsed) ? parsed : []
        } catch {
            return []
        }
    }
    return []
}

// Radarr instances computed
const radarrInstances = computed<IInstance[]>({
    get: () => {
        const instances = settingsStore.getSetting(SETTINGS.RADARR_INSTANCES)
        return parseInstances(instances as string | IInstance[])
    },
    set: (value: IInstance[]) => {
        settingsStore.updateSetting(
            SETTINGS.RADARR_INSTANCES,
            value,
            true,
            true
        )
        saveNotification.value?.show()
    }
})

// Sonarr instances computed
const sonarrInstances = computed<IInstance[]>({
    get: () => {
        const instances = settingsStore.getSetting(SETTINGS.SONARR_INSTANCES)
        return parseInstances(instances as string | IInstance[])
    },
    set: (value: IInstance[]) => {
        settingsStore.updateSetting(
            SETTINGS.SONARR_INSTANCES,
            value,
            true,
            true
        )
        saveNotification.value?.show()
    }
})

// Add Radarr instance
const addRadarrInstance = (): void => {
    const id = generateId()
    const instance: IInstance = {
        id,
        name: `Radarr ${radarrInstances.value.length + 1}`,
        url: '',
        apiKey: ''
    }
    initConnectionStatus('radarr', id)
    radarrInstances.value = [...radarrInstances.value, instance]
}

// Add Sonarr instance
const addSonarrInstance = (): void => {
    const id = generateId()
    const instance: IInstance = {
        id,
        name: `Sonarr ${sonarrInstances.value.length + 1}`,
        url: '',
        apiKey: ''
    }
    initConnectionStatus('sonarr', id)
    sonarrInstances.value = [...sonarrInstances.value, instance]
}

// Update Radarr instance
const updateRadarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = radarrInstances.value.findIndex((inst) => inst.id === id)
    if (index !== -1) {
        const newInstances = [...radarrInstances.value]
        newInstances[index] = updatedInstance
        radarrInstances.value = newInstances
    }
}

// Update Sonarr instance
const updateSonarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = sonarrInstances.value.findIndex((inst) => inst.id === id)
    if (index !== -1) {
        const newInstances = [...sonarrInstances.value]
        newInstances[index] = updatedInstance
        sonarrInstances.value = newInstances
    }
}

// Remove Radarr instance
const removeRadarrInstance = (id: string): void => {
    radarrInstances.value = radarrInstances.value.filter(
        (inst) => inst.id !== id
    )
    delete connectionStatuses.radarr[id]
    if (debounceTimers.radarr[id]) {
        clearTimeout(debounceTimers.radarr[id]!)
        delete debounceTimers.radarr[id]
    }
}

// Remove Sonarr instance
const removeSonarrInstance = (id: string): void => {
    sonarrInstances.value = sonarrInstances.value.filter(
        (inst) => inst.id !== id
    )
    delete connectionStatuses.sonarr[id]
    if (debounceTimers.sonarr[id]) {
        clearTimeout(debounceTimers.sonarr[id]!)
        delete debounceTimers.sonarr[id]
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

// Migrate legacy single instance to instances array
const migrateLegacySettings = (): void => {
    const radarrUrl = settingsStore.getSetting(SETTINGS.RADARR_URL) as string
    const radarrApiKey = settingsStore.getSetting(
        SETTINGS.RADARR_API_KEY
    ) as string
    const sonarrUrl = settingsStore.getSetting(SETTINGS.SONARR_URL) as string
    const sonarrApiKey = settingsStore.getSetting(
        SETTINGS.SONARR_API_KEY
    ) as string

    // Check if instances already exist
    const existingRadarrInstances = parseInstances(
        settingsStore.getSetting(SETTINGS.RADARR_INSTANCES) as string | IInstance[]
    )
    const existingSonarrInstances = parseInstances(
        settingsStore.getSetting(SETTINGS.SONARR_INSTANCES) as string | IInstance[]
    )

    // Migrate Radarr if legacy settings exist and no instances
    if (
        (radarrUrl || radarrApiKey) &&
        existingRadarrInstances.length === 0
    ) {
        const id = generateId()
        const instance: IInstance = {
            id,
            name: 'Radarr',
            url: radarrUrl || '',
            apiKey: radarrApiKey || ''
        }
        initConnectionStatus('radarr', id)
        settingsStore.updateSetting(
            SETTINGS.RADARR_INSTANCES,
            [instance],
            true,
            true
        )
    }

    // Migrate Sonarr if legacy settings exist and no instances
    if (
        (sonarrUrl || sonarrApiKey) &&
        existingSonarrInstances.length === 0
    ) {
        const id = generateId()
        const instance: IInstance = {
            id,
            name: 'Sonarr',
            url: sonarrUrl || '',
            apiKey: sonarrApiKey || ''
        }
        initConnectionStatus('sonarr', id)
        settingsStore.updateSetting(
            SETTINGS.SONARR_INSTANCES,
            [instance],
            true,
            true
        )
    }
}

// Initialize connection statuses for existing instances
const initExistingInstances = (): void => {
    const radarrInsts = parseInstances(
        settingsStore.getSetting(SETTINGS.RADARR_INSTANCES) as string | IInstance[]
    )
    const sonarrInsts = parseInstances(
        settingsStore.getSetting(SETTINGS.SONARR_INSTANCES) as string | IInstance[]
    )

    radarrInsts.forEach((inst) => {
        initConnectionStatus('radarr', inst.id)
    })

    sonarrInsts.forEach((inst) => {
        initConnectionStatus('sonarr', inst.id)
    })
}

// Run migration and initialization on mount
onMounted(() => {
    migrateLegacySettings()
    initExistingInstances()
})
</script>

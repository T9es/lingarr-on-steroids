<template>
    <SaveNotification ref="saveNotification" />

    <!-- Media Servers Section -->
    <CardComponent :title="translate('settings.integrations.title') || 'Media Servers'">
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
            <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                <!-- All Instances -->
                <InstanceCard
                    v-for="item in localInstances"
                    :key="`${item.type}-${item.instance.id}`"
                    :instance="item.instance"
                    :type="item.type"
                    :connection-status="getConnectionStatus(item.type, item.instance.id)"
                    @update:instance="updateLocalInstance(item.instance.id, $event)"
                    @remove="removeLocalInstance(item.instance.id)"
                    @test-connection="testConnection(item.type, item.instance)" />

                <!-- Single Add Button with Dropdown -->
                <AddInstanceButton v-if="localInstances.length < 10" @add="handleAddInstance" />
            </div>

            <!-- Save/Discard Button Bar -->
            <div
                v-if="hasChanges"
                class="border-secondary-content/20 mt-6 flex items-center justify-end gap-3 border-t pt-4">
                <span class="text-secondary-content text-sm">
                    {{ translate('common.unsavedChanges') || 'You have unsaved changes' }}
                </span>
                <button
                    @click="discardChanges"
                    class="text-secondary-content hover:bg-secondary-content/10 rounded-md border border-gray-600 px-4 py-2 text-sm transition-colors">
                    {{ translate('common.discard') || 'Discard' }}
                </button>
                <button
                    @click="saveChanges"
                    :disabled="isSaving"
                    class="bg-accent text-primary-content hover:bg-accent/80 rounded-md px-4 py-2 text-sm font-medium transition-colors disabled:opacity-50">
                    {{
                        isSaving
                            ? translate('common.saving') || 'Saving...'
                            : translate('common.saveChanges') || 'Save Changes'
                    }}
                </button>
            </div>

            <div
                v-translate="'settings.integrations.reindexTask'"
                class="text-secondary-content pt-4 text-sm" />
        </template>
    </CardComponent>
</template>

<script setup lang="ts">
import { computed, ref, reactive, onMounted } from 'vue'
import { useSettingStore } from '@/store/setting'
import { useOnboardingStore } from '@/store/onboarding'
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

interface InstanceWrapper {
    type: 'radarr' | 'sonarr'
    instance: IInstance
}

const { translate } = useI18n()

const saveNotification = ref<InstanceType<typeof SaveNotification> | null>(null)
const settingsStore = useSettingStore()

// Local state for unsaved changes
const localInstances = ref<InstanceWrapper[]>([])
const originalInstances = ref<InstanceWrapper[]>([])
const isSaving = ref(false)

// Connection status per instance
const connectionStatuses = reactive<Record<string, Record<string, ConnectionStatus>>>({
    radarr: {},
    sonarr: {}
})

// Generate unique ID
const generateId = (): string => {
    return `${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
}

// Deep equality check for instances
const instancesEqual = (a: InstanceWrapper[], b: InstanceWrapper[]): boolean => {
    if (a.length !== b.length) return false
    return JSON.stringify(a) === JSON.stringify(b)
}

// Computed to check if there are unsaved changes
const hasChanges = computed(() => {
    return !instancesEqual(localInstances.value, originalInstances.value)
})

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

// Parse instances from settings (handle both string and array formats)
const parseInstances = (value: string | IInstance[]): IInstance[] => {
    if (Array.isArray(value)) {
        return JSON.parse(JSON.stringify(value)) // Deep clone
    }
    if (typeof value === 'string' && value) {
        try {
            const parsed = JSON.parse(value)
            return Array.isArray(parsed) ? JSON.parse(JSON.stringify(parsed)) : []
        } catch {
            return []
        }
    }
    return []
}

// Load instances from store into local state
const loadInstancesFromStore = (): void => {
    const radarrInsts = parseInstances(
        settingsStore.getSetting(SETTINGS.RADARR_INSTANCES) as string | IInstance[]
    )
    const sonarrInsts = parseInstances(
        settingsStore.getSetting(SETTINGS.SONARR_INSTANCES) as string | IInstance[]
    )

    const combined: InstanceWrapper[] = [
        ...radarrInsts.map((i) => ({ type: 'radarr' as const, instance: i })),
        ...sonarrInsts.map((i) => ({ type: 'sonarr' as const, instance: i }))
    ]

    localInstances.value = combined
    originalInstances.value = JSON.parse(JSON.stringify(combined))

    // Initialize connection statuses
    radarrInsts.forEach((inst) => initConnectionStatus('radarr', inst.id))
    sonarrInsts.forEach((inst) => initConnectionStatus('sonarr', inst.id))
}

// Save changes to store
const saveChanges = async (): Promise<void> => {
    isSaving.value = true
    try {
        const radarrs = localInstances.value
            .filter((i) => i.type === 'radarr')
            .map((i) => i.instance)
        const sonarrs = localInstances.value
            .filter((i) => i.type === 'sonarr')
            .map((i) => i.instance)

        settingsStore.updateSetting(
            SETTINGS.RADARR_INSTANCES,
            JSON.parse(JSON.stringify(radarrs)),
            true,
            true
        )
        settingsStore.updateSetting(
            SETTINGS.SONARR_INSTANCES,
            JSON.parse(JSON.stringify(sonarrs)),
            true,
            true
        )

        // Update originals to match current state
        originalInstances.value = JSON.parse(JSON.stringify(localInstances.value))

        saveNotification.value?.show()
    } finally {
        isSaving.value = false
    }
}

// Discard changes and reset to original
const discardChanges = (): void => {
    localInstances.value = JSON.parse(JSON.stringify(originalInstances.value))

    // Re-initialize connection statuses for current instances
    connectionStatuses.radarr = {}
    connectionStatuses.sonarr = {}
    localInstances.value.forEach((wrapper) =>
        initConnectionStatus(wrapper.type, wrapper.instance.id)
    )
}

// Handle add instance from unified button
const handleAddInstance = (type: 'radarr' | 'sonarr'): void => {
    const id = generateId()
    const currentCount = localInstances.value.filter((i) => i.type === type).length
    const namePrefix = type === 'radarr' ? 'Radarr' : 'Sonarr'

    const instance: IInstance = {
        id,
        name: `${namePrefix} ${currentCount + 1}`,
        url: '',
        apiKey: ''
    }

    initConnectionStatus(type, id)
    localInstances.value.push({ type, instance })
}

// Update local instance
const updateLocalInstance = (id: string, updatedInstance: IInstance): void => {
    const index = localInstances.value.findIndex((wrapper) => wrapper.instance.id === id)
    if (index !== -1) {
        const newInstances = [...localInstances.value]
        newInstances[index].instance = updatedInstance
        localInstances.value = newInstances
    }
}

// Remove local instance
const removeLocalInstance = (id: string): void => {
    const wrapper = localInstances.value.find((w) => w.instance.id === id)
    if (wrapper) {
        delete connectionStatuses[wrapper.type][id]
    }
    localInstances.value = localInstances.value.filter((wrapper) => wrapper.instance.id !== id)
}

// Test Radarr connection
const testRadarrConnection = async (instance: IInstance): Promise<void> => {
    const status = connectionStatuses.radarr[instance.id]
    if (!status) return

    status.testing = true
    status.tested = false

    try {
        await services.setting.setSetting(SETTINGS.RADARR_URL, instance.url)
        await services.setting.setSetting(SETTINGS.RADARR_API_KEY, instance.apiKey)

        const result = await services.setting.testRadarrConnection<ConnectionTestResult>()
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
        await services.setting.setSetting(SETTINGS.SONARR_URL, instance.url)
        await services.setting.setSetting(SETTINGS.SONARR_API_KEY, instance.apiKey)

        const result = await services.setting.testSonarrConnection<ConnectionTestResult>()
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

// Test connection
const testConnection = async (type: 'radarr' | 'sonarr', instance: IInstance): Promise<void> => {
    if (type === 'radarr') {
        await testRadarrConnection(instance)
    } else {
        await testSonarrConnection(instance)
    }
}

// Migrate legacy single instance to instances array
// Only runs if onboarding is NOT active (prevents race condition)
const migrateLegacySettings = (): void => {
    const onboardingStore = useOnboardingStore()

    // Don't migrate if onboarding is active - let onboarding handle it
    if (onboardingStore.isActive) {
        return
    }

    const radarrUrl = settingsStore.getSetting(SETTINGS.RADARR_URL) as string
    const radarrApiKey = settingsStore.getSetting(SETTINGS.RADARR_API_KEY) as string
    const sonarrUrl = settingsStore.getSetting(SETTINGS.SONARR_URL) as string
    const sonarrApiKey = settingsStore.getSetting(SETTINGS.SONARR_API_KEY) as string

    const existingRadarrInstances = parseInstances(
        settingsStore.getSetting(SETTINGS.RADARR_INSTANCES) as string | IInstance[]
    )
    const existingSonarrInstances = parseInstances(
        settingsStore.getSetting(SETTINGS.SONARR_INSTANCES) as string | IInstance[]
    )

    if ((radarrUrl || radarrApiKey) && existingRadarrInstances.length === 0) {
        // Use 'default' ID to match backend fallback and prevent duplicates
        const instance: IInstance = {
            id: 'default',
            name: 'Radarr',
            url: radarrUrl || '',
            apiKey: radarrApiKey || ''
        }
        initConnectionStatus('radarr', 'default')
        settingsStore.updateSetting(SETTINGS.RADARR_INSTANCES, [instance], true, true)
    }

    if ((sonarrUrl || sonarrApiKey) && existingSonarrInstances.length === 0) {
        // Use 'default' ID to match backend fallback and prevent duplicates
        const instance: IInstance = {
            id: 'default',
            name: 'Sonarr',
            url: sonarrUrl || '',
            apiKey: sonarrApiKey || ''
        }
        initConnectionStatus('sonarr', 'default')
        settingsStore.updateSetting(SETTINGS.SONARR_INSTANCES, [instance], true, true)
    }
}

onMounted(() => {
    migrateLegacySettings()
    loadInstancesFromStore()
})
</script>

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
                <!-- Radarr Instances -->
                <InstanceCard
                    v-for="instance in localRadarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="radarr"
                    :connection-status="getConnectionStatus('radarr', instance.id)"
                    @update:instance="updateLocalRadarrInstance(instance.id, $event)"
                    @remove="removeLocalRadarrInstance(instance.id)"
                    @test-connection="testRadarrConnection(instance)" />
                
                <!-- Sonarr Instances -->
                <InstanceCard
                    v-for="instance in localSonarrInstances"
                    :key="instance.id"
                    :instance="instance"
                    type="sonarr"
                    :connection-status="getConnectionStatus('sonarr', instance.id)"
                    @update:instance="updateLocalSonarrInstance(instance.id, $event)"
                    @remove="removeLocalSonarrInstance(instance.id)"
                    @test-connection="testSonarrConnection(instance)" />
                
                <!-- Single Add Button with Dropdown -->
                <AddInstanceButton
                    v-if="localRadarrInstances.length + localSonarrInstances.length < 10"
                    @add="handleAddInstance" />
            </div>
            
            <!-- Save/Discard Button Bar -->
            <div 
                v-if="hasChanges" 
                class="mt-6 flex items-center justify-end gap-3 border-t border-secondary-content/20 pt-4"
            >
                <span class="text-sm text-secondary-content">
                    {{ translate('common.unsavedChanges') || 'You have unsaved changes' }}
                </span>
                <button
                    @click="discardChanges"
                    class="rounded-md border border-gray-600 px-4 py-2 text-sm text-secondary-content transition-colors hover:bg-secondary-content/10"
                >
                    {{ translate('common.discard') || 'Discard' }}
                </button>
                <button
                    @click="saveChanges"
                    :disabled="isSaving"
                    class="rounded-md bg-accent px-4 py-2 text-sm font-medium text-primary-content transition-colors hover:bg-accent/80 disabled:opacity-50"
                >
                    {{ isSaving ? (translate('common.saving') || 'Saving...') : (translate('common.saveChanges') || 'Save Changes') }}
                </button>
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

// Local state for unsaved changes
const localRadarrInstances = ref<IInstance[]>([])
const localSonarrInstances = ref<IInstance[]>([])
const originalRadarrInstances = ref<IInstance[]>([])
const originalSonarrInstances = ref<IInstance[]>([])
const isSaving = ref(false)

// Connection status per instance
const connectionStatuses = reactive<
    Record<string, Record<string, ConnectionStatus>>
>({
    radarr: {},
    sonarr: {}
})

// Generate unique ID
const generateId = (): string => {
    return `${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
}

// Deep equality check for instances
const instancesEqual = (a: IInstance[], b: IInstance[]): boolean => {
    if (a.length !== b.length) return false
    return JSON.stringify(a) === JSON.stringify(b)
}

// Computed to check if there are unsaved changes
const hasChanges = computed(() => {
    return !instancesEqual(localRadarrInstances.value, originalRadarrInstances.value) ||
           !instancesEqual(localSonarrInstances.value, originalSonarrInstances.value)
})

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
    
    localRadarrInstances.value = radarrInsts
    localSonarrInstances.value = sonarrInsts
    originalRadarrInstances.value = JSON.parse(JSON.stringify(radarrInsts))
    originalSonarrInstances.value = JSON.parse(JSON.stringify(sonarrInsts))
    
    // Initialize connection statuses
    radarrInsts.forEach((inst) => initConnectionStatus('radarr', inst.id))
    sonarrInsts.forEach((inst) => initConnectionStatus('sonarr', inst.id))
}

// Save changes to store
const saveChanges = async (): Promise<void> => {
    isSaving.value = true
    try {
        settingsStore.updateSetting(
            SETTINGS.RADARR_INSTANCES,
            JSON.parse(JSON.stringify(localRadarrInstances.value)),
            true,
            true
        )
        settingsStore.updateSetting(
            SETTINGS.SONARR_INSTANCES,
            JSON.parse(JSON.stringify(localSonarrInstances.value)),
            true,
            true
        )
        
        // Update originals to match current state
        originalRadarrInstances.value = JSON.parse(JSON.stringify(localRadarrInstances.value))
        originalSonarrInstances.value = JSON.parse(JSON.stringify(localSonarrInstances.value))
        
        saveNotification.value?.show()
    } finally {
        isSaving.value = false
    }
}

// Discard changes and reset to original
const discardChanges = (): void => {
    localRadarrInstances.value = JSON.parse(JSON.stringify(originalRadarrInstances.value))
    localSonarrInstances.value = JSON.parse(JSON.stringify(originalSonarrInstances.value))
    
    // Re-initialize connection statuses for current instances
    connectionStatuses.radarr = {}
    connectionStatuses.sonarr = {}
    localRadarrInstances.value.forEach((inst) => initConnectionStatus('radarr', inst.id))
    localSonarrInstances.value.forEach((inst) => initConnectionStatus('sonarr', inst.id))
}

// Handle add instance from unified button
const handleAddInstance = (type: 'radarr' | 'sonarr'): void => {
    if (type === 'radarr') {
        addLocalRadarrInstance()
    } else {
        addLocalSonarrInstance()
    }
}

// Add Radarr instance (local only)
const addLocalRadarrInstance = (): void => {
    const id = generateId()
    const instance: IInstance = {
        id,
        name: `Radarr ${localRadarrInstances.value.length + 1}`,
        url: '',
        apiKey: ''
    }
    initConnectionStatus('radarr', id)
    localRadarrInstances.value = [...localRadarrInstances.value, instance]
}

// Add Sonarr instance (local only)
const addLocalSonarrInstance = (): void => {
    const id = generateId()
    const instance: IInstance = {
        id,
        name: `Sonarr ${localSonarrInstances.value.length + 1}`,
        url: '',
        apiKey: ''
    }
    initConnectionStatus('sonarr', id)
    localSonarrInstances.value = [...localSonarrInstances.value, instance]
}

// Update local Radarr instance
const updateLocalRadarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = localRadarrInstances.value.findIndex((inst) => inst.id === id)
    if (index !== -1) {
        const newInstances = [...localRadarrInstances.value]
        newInstances[index] = updatedInstance
        localRadarrInstances.value = newInstances
    }
}

// Update local Sonarr instance
const updateLocalSonarrInstance = (id: string, updatedInstance: IInstance): void => {
    const index = localSonarrInstances.value.findIndex((inst) => inst.id === id)
    if (index !== -1) {
        const newInstances = [...localSonarrInstances.value]
        newInstances[index] = updatedInstance
        localSonarrInstances.value = newInstances
    }
}

// Remove local Radarr instance
const removeLocalRadarrInstance = (id: string): void => {
    localRadarrInstances.value = localRadarrInstances.value.filter(
        (inst) => inst.id !== id
    )
    delete connectionStatuses.radarr[id]
}

// Remove local Sonarr instance
const removeLocalSonarrInstance = (id: string): void => {
    localSonarrInstances.value = localSonarrInstances.value.filter(
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

    const existingRadarrInstances = parseInstances(
        settingsStore.getSetting(SETTINGS.RADARR_INSTANCES) as string | IInstance[]
    )
    const existingSonarrInstances = parseInstances(
        settingsStore.getSetting(SETTINGS.SONARR_INSTANCES) as string | IInstance[]
    )

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

onMounted(() => {
    migrateLegacySettings()
    loadInstancesFromStore()
})
</script>

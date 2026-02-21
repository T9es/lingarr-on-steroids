<template>
    <div class="border-accent bg-primary/50 rounded-md border p-4">
        <!-- Header with icon, name input, and X button -->
        <div class="mb-4 flex items-center gap-2">
            <component :is="typeIcon" class="h-5 w-5" />
            <input
                v-model="localInstance.name"
                type="text"
                :placeholder="type === 'radarr' ? 'Radarr Instance' : 'Sonarr Instance'"
                class="w-full rounded-md border border-accent bg-transparent px-3 py-1.5 text-sm outline-hidden transition-colors focus:border-accent/70" />
            <button
                type="button"
                class="text-gray-400 hover:text-gray-200 ml-auto cursor-pointer transition-colors"
                @click="$emit('remove')">
                <TimesIcon class="h-4 w-4" />
            </button>
        </div>

        <!-- URL Input -->
        <InputComponent
            v-model="localInstance.url"
            validation-type="url"
            :label="'URL'"
            @update:validation="(val) => (isValid.url = val)" />

        <!-- API Key Input -->
        <InputComponent
            v-model="localInstance.apiKey"
            type="password"
            validation-type="string"
            :min-length="32"
            :max-length="32"
            label="API Key"
            @update:validation="(val) => (isValid.apiKey = val)" />

        <!-- Connection Status -->
        <div class="flex items-center gap-3 pt-2">
            <button
                type="button"
                class="bg-primary-600 hover:bg-primary-700 rounded-md px-3 py-1.5 text-sm text-white transition-colors disabled:cursor-not-allowed disabled:opacity-50"
                :disabled="connectionStatus.testing || !isValid.url || !isValid.apiKey"
                @click="$emit('test-connection')">
                <span v-if="connectionStatus.testing" class="flex items-center gap-2">
                    <svg
                        class="h-4 w-4 animate-spin"
                        xmlns="http://www.w3.org/2000/svg"
                        fill="none"
                        viewBox="0 0 24 24">
                        <circle
                            class="opacity-25"
                            cx="12"
                            cy="12"
                            r="10"
                            stroke="currentColor"
                            stroke-width="4"></circle>
                        <path
                            class="opacity-75"
                            fill="currentColor"
                            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                    Testing...
                </span>
                <span v-else>Test Connection</span>
            </button>

            <div v-if="connectionStatus.tested" class="flex items-center gap-2 text-sm">
                <span
                    v-if="connectionStatus.connected"
                    class="flex items-center gap-1 text-green-500">
                    <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                        <path
                            fill-rule="evenodd"
                            d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z"
                            clip-rule="evenodd" />
                    </svg>
                    Connected
                    <span v-if="connectionStatus.version" class="text-gray-500">
                        (v{{ connectionStatus.version }})
                    </span>
                </span>
                <span v-else class="flex items-center gap-1 text-red-500">
                    <svg class="h-4 w-4" fill="currentColor" viewBox="0 0 20 20">
                        <path
                            fill-rule="evenodd"
                            d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z"
                            clip-rule="evenodd" />
                    </svg>
                    {{ connectionStatus.message || 'Connection failed' }}
                </span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import type { IInstance } from '@/ts/setting'
import InputComponent from '@/components/common/InputComponent.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'
import TimesIcon from '@/components/icons/TimesIcon.vue'

interface ConnectionStatus {
    testing: boolean
    tested: boolean
    connected: boolean
    message: string
    version: string | null
}

const props = defineProps<{
    instance: IInstance
    type: 'radarr' | 'sonarr'
    connectionStatus: ConnectionStatus
}>()

const emit = defineEmits<{
    (e: 'update:instance', value: IInstance): void
    (e: 'remove'): void
    (e: 'test-connection'): void
}>()

const isValid = reactive({
    url: false,
    apiKey: false
})

// Create a local copy that syncs with parent
const localInstance = reactive<IInstance>({ ...props.instance })

// Watch for changes and emit update
watch(
    localInstance,
    (newValue) => {
        emit('update:instance', { ...newValue })
    },
    { deep: true }
)

// Watch for external changes to instance prop
watch(
    () => props.instance,
    (newValue) => {
        Object.assign(localInstance, newValue)
    },
    { deep: true }
)

// Dynamic icon based on type
const typeIcon = computed(() => (props.type === 'radarr' ? RadarrIcon : SonarrIcon))
</script>

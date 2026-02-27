<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import axios from 'axios'

const i18n = useI18n()

interface ErrorLog {
    id: number
    timestamp: string
    type: 'error' | 'warning' | 'info'
    message: string
    source?: string
    details?: string
}

const errors = ref<ErrorLog[]>([])
const isLoading = ref(false)
const expandedId = ref<number | null>(null)

const fetchErrors = async () => {
    isLoading.value = true

    try {
        const response = await axios.get('/api/dashboard/errors?limit=10')
        errors.value = response.data
    } catch (e) {
        console.error('Failed to fetch error log:', e)
    } finally {
        isLoading.value = false
    }
}

const refreshInterval = ref<number | null>(null)

onMounted(() => {
    fetchErrors()
    // Refresh every 30 seconds
    refreshInterval.value = window.setInterval(fetchErrors, 30000)
})

onUnmounted(() => {
    if (refreshInterval.value) {
        clearInterval(refreshInterval.value)
    }
})

const getTypeColor = (type: string): string => {
    switch (type) {
        case 'error':
            return 'text-red-400 bg-red-500/10'
        case 'warning':
            return 'text-yellow-400 bg-yellow-500/10'
        case 'info':
            return 'text-accent bg-accent/10'
        default:
            return 'text-secondary-content bg-secondary/30'
    }
}

const toggleExpand = (id: number) => {
    expandedId.value = expandedId.value === id ? null : id
}

const errorCount = () => errors.value.filter((e) => e.type === 'error').length
const warningCount = () => errors.value.filter((e) => e.type === 'warning').length
</script>

<template>
    <div class="flex h-full flex-col">
        <div class="mb-4 flex items-center justify-between">
            <div class="flex items-center gap-3">
                <h3 class="text-primary-content/70 text-sm font-medium">{{ i18n.translate('statistics.errorLog') }}</h3>
                <div class="flex gap-2 text-xs">
                    <span v-if="errorCount() > 0" class="text-red-400">
                        {{ errorCount() }} {{ i18n.translate('statistics.errors') }}
                    </span>
                    <span v-if="warningCount() > 0" class="text-yellow-400">
                        {{ warningCount() }} {{ i18n.translate('statistics.warnings') }}
                    </span>
                </div>
            </div>
            <button
                @click="fetchErrors"
                :disabled="isLoading"
                class="text-secondary-content hover:text-primary-content p-1 transition-colors"
                :title="i18n.translate('statistics.refresh')">
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="errors.length === 0" class="text-secondary-content py-8 text-center text-sm italic opacity-70">
            {{ i18n.translate('statistics.noErrors') }}
        </div>

        <div v-else class="flex-1 space-y-3 overflow-y-auto pr-1">
            <div
                v-for="error in errors"
                :key="error.id"
                class="border-secondary/20 cursor-pointer border-b pb-3 transition-opacity hover:opacity-80 last:border-0 last:pb-0"
                @click="toggleExpand(error.id)">
                <div class="flex items-start justify-between">
                    <div class="min-w-0 flex-1">
                        <div class="text-primary-content truncate text-sm font-medium">{{ error.message }}</div>
                        <div class="text-secondary-content mt-0.5 text-xs">
                            {{ error.timestamp }}
                            <span v-if="error.source">• {{ error.source }}</span>
                        </div>
                    </div>
                    <span 
                        class="ml-2 shrink-0 rounded px-2 py-0.5 text-xs font-medium uppercase"
                        :class="getTypeColor(error.type)">
                        {{ error.type }}
                    </span>
                </div>

                <!-- Expanded details -->
                <div
                    v-if="expandedId === error.id && error.details"
                    class="text-secondary-content border-secondary/20 mt-2 border-t pt-2 text-xs">
                    {{ error.details }}
                </div>
            </div>
        </div>
    </div>
</template>

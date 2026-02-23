<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CardComponent from '@/components/common/CardComponent.vue'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import ExclamationIcon from '@/components/icons/ExclamationIcon.vue'
import axios from 'axios'

const i18n = useI18n()

interface ErrorLog {
    id: string
    timestamp: string
    type: 'error' | 'warning' | 'info'
    message: string
    source?: string
    details?: string
}

const errors = ref<ErrorLog[]>([])
const isLoading = ref(false)
const expandedId = ref<string | null>(null)

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
        case 'error': return 'text-red-400 bg-red-500/20 border-red-500/30'
        case 'warning': return 'text-yellow-400 bg-yellow-500/20 border-yellow-500/30'
        case 'info': return 'text-blue-400 bg-blue-500/20 border-blue-500/30'
        default: return 'text-secondary-content bg-gray-500/20 border-gray-500/30'
    }
}

const toggleExpand = (id: string) => {
    expandedId.value = expandedId.value === id ? null : id
}

const errorCount = () => errors.value.filter(e => e.type === 'error').length
const warningCount = () => errors.value.filter(e => e.type === 'warning').length
</script>

<template>
    <CardComponent :title="i18n.translate('statistics.errorLog')" class="h-full">
        <div class="flex justify-between items-center mb-2">
            <div class="flex gap-2 text-xs">
                <span v-if="errorCount() > 0" class="text-red-400">
                    {{ errorCount() }} {{ i18n.translate('statistics.errors') }}
                </span>
                <span v-if="warningCount() > 0" class="text-yellow-400">
                    {{ warningCount() }} {{ i18n.translate('statistics.warnings') }}
                </span>
            </div>
            <button 
                @click="fetchErrors" 
                :disabled="isLoading"
                class="p-1 text-secondary-content hover:text-primary-content transition-colors"
                :title="i18n.translate('statistics.refresh')"
            >
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="errors.length === 0" class="text-secondary-content text-sm py-4 text-center">
            <ExclamationIcon class="h-8 w-8 mx-auto mb-2 text-green-500" />
            {{ i18n.translate('statistics.noErrors') }}
        </div>

        <div v-else class="space-y-2 max-h-64 overflow-y-auto">
            <div 
                v-for="error in errors" 
                :key="error.id"
                class="p-2 rounded-md border cursor-pointer transition-colors hover:bg-black/20"
                :class="getTypeColor(error.type)"
                @click="toggleExpand(error.id)"
            >
                <div class="flex items-start justify-between">
                    <div class="flex-1 min-w-0">
                        <div class="text-sm font-medium truncate">{{ error.message }}</div>
                        <div class="text-xs opacity-70 mt-0.5">
                            {{ error.timestamp }}
                            <span v-if="error.source"> • {{ error.source }}</span>
                        </div>
                    </div>
                    <span class="text-xs uppercase px-1.5 py-0.5 rounded ml-2 shrink-0">
                        {{ error.type }}
                    </span>
                </div>
                
                <!-- Expanded details -->
                <div 
                    v-if="expandedId === error.id && error.details"
                    class="mt-2 pt-2 border-t border-current/20 text-xs opacity-80"
                >
                    {{ error.details }}
                </div>
            </div>
        </div>
    </CardComponent>
</template>

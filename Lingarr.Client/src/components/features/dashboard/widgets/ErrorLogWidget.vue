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
        case 'error':
            return 'text-red-400 bg-red-500/20 border-red-500/30'
        case 'warning':
            return 'text-yellow-400 bg-yellow-500/20 border-yellow-500/30'
        case 'info':
            return 'text-blue-400 bg-blue-500/20 border-blue-500/30'
        default:
            return 'text-secondary-content bg-gray-500/20 border-gray-500/30'
    }
}

const toggleExpand = (id: string) => {
    expandedId.value = expandedId.value === id ? null : id
}

const errorCount = () => errors.value.filter((e) => e.type === 'error').length
const warningCount = () => errors.value.filter((e) => e.type === 'warning').length
</script>

<template>
    <CardComponent :title="i18n.translate('statistics.errorLog')" class="h-full">
        <div class="mb-2 flex items-center justify-between">
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
                class="text-secondary-content hover:text-primary-content p-1 transition-colors"
                :title="i18n.translate('statistics.refresh')">
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="errors.length === 0" class="text-secondary-content py-4 text-center text-sm">
            <ExclamationIcon class="mx-auto mb-2 h-8 w-8 text-green-500" />
            {{ i18n.translate('statistics.noErrors') }}
        </div>

        <div v-else class="max-h-64 space-y-2 overflow-y-auto">
            <div
                v-for="error in errors"
                :key="error.id"
                class="cursor-pointer rounded-md border p-2 transition-colors hover:bg-black/20"
                :class="getTypeColor(error.type)"
                @click="toggleExpand(error.id)">
                <div class="flex items-start justify-between">
                    <div class="min-w-0 flex-1">
                        <div class="truncate text-sm font-medium">{{ error.message }}</div>
                        <div class="mt-0.5 text-xs opacity-70">
                            {{ error.timestamp }}
                            <span v-if="error.source">• {{ error.source }}</span>
                        </div>
                    </div>
                    <span class="ml-2 shrink-0 rounded px-1.5 py-0.5 text-xs uppercase">
                        {{ error.type }}
                    </span>
                </div>

                <!-- Expanded details -->
                <div
                    v-if="expandedId === error.id && error.details"
                    class="mt-2 border-t border-current/20 pt-2 text-xs opacity-80">
                    {{ error.details }}
                </div>
            </div>
        </div>
    </CardComponent>
</template>

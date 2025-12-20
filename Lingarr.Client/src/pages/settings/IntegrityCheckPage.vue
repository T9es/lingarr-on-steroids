<template>
    <div class="grid grid-flow-row auto-rows-max grid-cols-1 gap-4 p-4">
        <CardComponent :title="translate('settings.integrity.title')">
            <template #description>
                {{ translate('settings.integrity.description') }}
            </template>
            <template #content>
                <div class="flex flex-col items-center space-y-6">
                    <!-- Action Button -->
                    <div class="flex items-center justify-center">
                        <button
                            :disabled="isRunning"
                            class="bg-accent hover:bg-accent/80 disabled:bg-base-300 rounded px-6 py-3 font-semibold text-white transition-colors disabled:cursor-not-allowed disabled:text-gray-500"
                            @click="startBulkCheck">
                            <span v-if="isRunning" class="flex items-center">
                                <svg
                                    class="mr-2 h-5 w-5 animate-spin"
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
                                {{ translate('settings.integrity.running') }}
                            </span>
                            <span v-else>
                                {{ translate('settings.integrity.startButton') }}
                            </span>
                        </button>
                    </div>

                    <!-- Progress Section -->
                    <div v-if="hasStarted" class="w-full max-w-2xl space-y-4">
                        <!-- Progress Bar -->
                        <div class="w-full">
                            <div class="mb-2 flex justify-between text-sm">
                                <span>{{ translate('settings.integrity.progress') }}</span>
                                <span>{{ Math.round(stats.progressPercent) }}%</span>
                            </div>
                            <div class="bg-base-300 h-4 w-full overflow-hidden rounded-full">
                                <div
                                    class="bg-accent h-full transition-all duration-300"
                                    :style="{ width: `${stats.progressPercent}%` }"></div>
                            </div>
                        </div>

                        <!-- Stats Grid -->
                        <div class="grid grid-cols-2 gap-4 md:grid-cols-4">
                            <div class="bg-base-200 rounded p-4 text-center">
                                <div class="text-2xl font-bold">{{ stats.processedCount }}</div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.processed') }}
                                </div>
                            </div>
                            <div class="bg-base-200 rounded p-4 text-center">
                                <div class="text-2xl font-bold text-green-500">
                                    {{ stats.validCount }}
                                </div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.valid') }}
                                </div>
                            </div>
                            <div class="bg-base-200 rounded p-4 text-center">
                                <div class="text-2xl font-bold text-yellow-500">
                                    {{ stats.corruptCount }}
                                </div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.corrupt') }}
                                </div>
                            </div>
                            <div class="bg-base-200 rounded p-4 text-center">
                                <div class="text-2xl font-bold text-blue-500">
                                    {{ stats.queuedCount }}
                                </div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.queued') }}
                                </div>
                            </div>
                        </div>

                        <!-- Totals -->
                        <div class="text-center text-sm opacity-70">
                            {{ translate('settings.integrity.stats.total') }}: {{ stats.total }} ({{
                                stats.totalMovies
                            }}
                            {{ translate('settings.integrity.stats.movies') }},
                            {{ stats.totalEpisodes }}
                            {{ translate('settings.integrity.stats.episodes') }})
                        </div>

                        <!-- Completion Message -->
                        <div
                            v-if="stats.isComplete"
                            class="rounded border border-green-500/30 bg-green-500/10 p-4 text-center text-green-400">
                            {{ translate('settings.integrity.completed') }}
                        </div>

                        <!-- Error Message -->
                        <div
                            v-if="stats.error"
                            class="rounded border border-red-500/30 bg-red-500/10 p-4 text-center text-red-400">
                            {{ stats.error }}
                        </div>
                    </div>
                </div>
            </template>
        </CardComponent>
    </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CardComponent from '@/components/common/CardComponent.vue'
import { useSignalR } from '@/composables/useSignalR'
import { Hub } from '@/ts'
import axios from 'axios'

const { translate } = useI18n()
const signalR = useSignalR()
const hubConnection = ref<Hub>()

interface BulkIntegrityStats {
    total: number
    totalMovies: number
    totalEpisodes: number
    processedCount: number
    validCount: number
    corruptCount: number
    queuedCount: number
    errorCount: number
    isComplete: boolean
    error: string | null
    progressPercent: number
}

const isRunning = ref(false)
const hasStarted = ref(false)
const stats = reactive<BulkIntegrityStats>({
    total: 0,
    totalMovies: 0,
    totalEpisodes: 0,
    processedCount: 0,
    validCount: 0,
    corruptCount: 0,
    queuedCount: 0,
    errorCount: 0,
    isComplete: false,
    error: null,
    progressPercent: 0
})

const startBulkCheck = async () => {
    try {
        isRunning.value = true
        hasStarted.value = true

        // Reset stats
        Object.assign(stats, {
            total: 0,
            totalMovies: 0,
            totalEpisodes: 0,
            processedCount: 0,
            validCount: 0,
            corruptCount: 0,
            queuedCount: 0,
            errorCount: 0,
            isComplete: false,
            error: null,
            progressPercent: 0
        })

        await axios.post('/api/media/bulk-integrity-check')
    } catch (error) {
        console.error('Failed to start bulk integrity check:', error)
        stats.error = 'Failed to start integrity check'
        isRunning.value = false
    }
}

const handleProgress = (newStats: BulkIntegrityStats) => {
    Object.assign(stats, newStats)
    if (newStats.isComplete) {
        isRunning.value = false
    }
}

onMounted(async () => {
    // Check if a job is already running and restore state
    try {
        const response = await axios.get('/api/media/bulk-integrity-status')
        if (response.data && response.data.isRunning) {
            hasStarted.value = true
            isRunning.value = true
            Object.assign(stats, response.data)
        } else if (response.data && response.data.isComplete) {
            // Show completed state if job finished while page was closed
            hasStarted.value = true
            isRunning.value = false
            Object.assign(stats, response.data)
        }
    } catch (error) {
        console.debug('No existing integrity check status')
    }

    hubConnection.value = await signalR.connect('JobProgress', '/signalr/JobProgress')
    await hubConnection.value.joinGroup({ group: 'JobProgress' })
    hubConnection.value.on('BulkIntegrityProgress', handleProgress)
})

onUnmounted(() => {
    hubConnection.value?.off('BulkIntegrityProgress', handleProgress)
})
</script>

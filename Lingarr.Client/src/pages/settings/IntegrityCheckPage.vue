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

        <!-- Verify ASS Integrity Section -->
        <CardComponent title="Verify ASS Integrity">
            <template #description>
                Scans translated subtitles for vector drawing artifacts. Run after major updates.
            </template>
            <template #content>
                <div class="flex flex-col space-y-6">
                    <!-- Action Button -->
                    <div class="flex items-center justify-center">
                        <button
                            :disabled="assIsRunning"
                            class="bg-accent hover:bg-accent/80 disabled:bg-base-300 rounded px-6 py-3 font-semibold text-white transition-colors disabled:cursor-not-allowed disabled:text-gray-500"
                            @click="startAssVerification">
                            <span v-if="assIsRunning" class="flex items-center">
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
                                Scanning...
                            </span>
                            <span v-else>Verify ASS Integrity</span>
                        </button>
                    </div>

                    <!-- Persistent Results -->
                    <div v-if="assResult" class="w-full space-y-4">
                        <!-- Stats Grid -->
                        <div class="grid w-full grid-cols-2 gap-4">
                            <div class="bg-base-200 rounded p-4 text-center">
                                <div class="text-2xl font-bold">
                                    {{ assResult.totalFilesScanned }}
                                </div>
                                <div class="text-sm opacity-70">Files Scanned</div>
                            </div>
                            <div class="bg-base-200 rounded p-4 text-center">
                                <div
                                    class="text-2xl font-bold"
                                    :class="
                                        assResult.filesWithDrawings > 0
                                            ? 'text-yellow-500'
                                            : 'text-green-500'
                                    ">
                                    {{ assResult.filesWithDrawings }}
                                </div>
                                <div class="text-sm opacity-70">Files with Issues</div>
                            </div>
                        </div>

                        <!-- Flagged Items List -->
                        <div
                            v-if="assResult.flaggedItems && assResult.flaggedItems.length > 0"
                            class="w-full space-y-3">
                            <div class="flex items-center justify-between">
                                <h4 class="font-semibold">Flagged Files</h4>
                                <button
                                    class="bg-accent hover:bg-accent/80 rounded px-4 py-2 text-sm font-semibold text-white"
                                    @click="requeueAll">
                                    Requeue All for Translation
                                </button>
                            </div>
                            <div class="bg-base-200 max-h-96 w-full overflow-y-auto rounded">
                                <div
                                    v-for="item in assResult.flaggedItems"
                                    :key="item.subtitlePath"
                                    class="border-base-300 border-b last:border-0">
                                    <div
                                        class="hover:bg-base-300/50 flex cursor-pointer items-center justify-between p-3"
                                        @click="toggleExpand(item.subtitlePath)">
                                        <div class="flex-1 overflow-hidden">
                                            <div class="truncate font-medium">
                                                {{ item.mediaTitle }}
                                            </div>
                                            <div class="truncate text-xs opacity-50">
                                                {{ item.subtitlePath }}
                                            </div>
                                            <div class="text-xs text-yellow-500">
                                                {{ item.suspiciousLineCount }} suspicious lines
                                                (click to view)
                                            </div>
                                        </div>
                                        <button
                                            class="ml-2 text-sm opacity-50 hover:opacity-100"
                                            @click.stop="dismissItem(item)">
                                            Dismiss
                                        </button>
                                    </div>
                                    <!-- Expandable suspicious lines -->
                                    <div
                                        v-if="
                                            expandedItems.includes(item.subtitlePath) &&
                                            item.suspiciousLines
                                        "
                                        class="bg-base-300/30 border-base-300 border-t p-3 text-xs">
                                        <div class="mb-2 font-semibold opacity-70">
                                            Suspicious lines:
                                        </div>
                                        <div
                                            v-for="(line, idx) in item.suspiciousLines"
                                            :key="idx"
                                            class="truncate py-1 font-mono text-yellow-400">
                                            {{ line }}
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Success Message -->
                        <div
                            v-if="assResult.filesWithDrawings === 0"
                            class="w-full rounded border border-green-500/30 bg-green-500/10 p-4 text-center text-green-400">
                            All files passed verification!
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

    // Load persisted ASS verification result
    try {
        const assResponse = await axios.get('/api/setting/subtitle_ass_verification_last_result')
        // API returns the value directly as a string, not as {value: ...}
        if (assResponse.data) {
            assResult.value = JSON.parse(assResponse.data)
        }
    } catch (error) {
        // 400 is expected when no scan has been run yet
        console.debug('No existing ASS verification result')
    }

    hubConnection.value = await signalR.connect('JobProgress', '/signalr/JobProgress')
    await hubConnection.value.joinGroup({ group: 'JobProgress' })
    hubConnection.value.on('BulkIntegrityProgress', handleProgress)
})

onUnmounted(() => {
    hubConnection.value?.off('BulkIntegrityProgress', handleProgress)
})

// ASS Verification
interface AssVerificationItem {
    mediaId: number
    mediaType: string
    mediaTitle: string
    subtitlePath: string
    suspiciousLineCount: number
    suspiciousLines: string[]
    dismissed: boolean
}

interface AssVerificationResult {
    totalFilesScanned: number
    filesWithDrawings: number
    flaggedItems: AssVerificationItem[]
}

const assIsRunning = ref(false)
const assResult = ref<AssVerificationResult | null>(null)
const expandedItems = ref<string[]>([])

const toggleExpand = (path: string) => {
    if (expandedItems.value.includes(path)) {
        expandedItems.value = expandedItems.value.filter((p) => p !== path)
    } else {
        expandedItems.value.push(path)
    }
}

const startAssVerification = async () => {
    try {
        assIsRunning.value = true
        const response = await axios.post('/api/subtitle/verify-ass')
        assResult.value = response.data

        // Persist result
        await axios.post('/api/setting', {
            key: 'subtitle_ass_verification_last_result',
            value: JSON.stringify(response.data)
        })
    } catch (error) {
        console.error('Failed to start ASS verification:', error)
    } finally {
        assIsRunning.value = false
    }
}

const requeueAll = async () => {
    if (!assResult.value?.flaggedItems) return

    try {
        for (const item of assResult.value.flaggedItems) {
            // MediaType should be string like 'Movie' or 'Episode'
            await axios.post('/api/translate/media', {
                mediaId: item.mediaId,
                mediaType: item.mediaType
            })
        }
        // Clear the list after requeue
        assResult.value.flaggedItems = []
        assResult.value.filesWithDrawings = 0

        // Update persisted result
        await axios.post('/api/setting', {
            key: 'subtitle_ass_verification_last_result',
            value: JSON.stringify(assResult.value)
        })
    } catch (error) {
        console.error('Failed to requeue items:', error)
    }
}

const dismissItem = async (item: AssVerificationItem) => {
    if (!assResult.value?.flaggedItems) return

    // Remove from list
    assResult.value.flaggedItems = assResult.value.flaggedItems.filter(
        (i) => i.subtitlePath !== item.subtitlePath
    )
    assResult.value.filesWithDrawings = assResult.value.flaggedItems.length

    // Update persisted result
    await axios.post('/api/setting', {
        key: 'subtitle_ass_verification_last_result',
        value: JSON.stringify(assResult.value)
    })
}
</script>

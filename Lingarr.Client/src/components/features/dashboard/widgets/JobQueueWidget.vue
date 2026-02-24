<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CardComponent from '@/components/common/CardComponent.vue'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import axios from 'axios'

const i18n = useI18n()

interface JobInfo {
    id: string
    name: string
    state: 'running' | 'scheduled' | 'failed' | 'completed' | 'pending' | 'queued'
    progress?: number
    nextRun?: string
    lastRun?: string
    error?: string
    queue?: string
    sourceLanguage?: string
    targetLanguage?: string
}

const jobs = ref<JobInfo[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)

const fetchJobs = async () => {
    isLoading.value = true
    error.value = null

    try {
        const response = await axios.get('/api/dashboard/jobs')
        // Handle both array response and object with Jobs property
        const data = response.data
        const rawJobs = Array.isArray(data) ? data : data.jobs || data.Jobs || []

        // Map backend DTOs to frontend interface
        jobs.value = rawJobs.map((job: any) => ({
            id: job.Id || job.id,
            name: job.Name || job.name,
            state: (job.State || job.state || '').toLowerCase(),
            progress: job.Progress || job.progress,
            nextRun: job.ScheduledAt || job.nextRun,
            lastRun: job.StartedAt || job.lastRun,
            error: job.ErrorMessage || job.error,
            queue: job.Queue || job.queue,
            sourceLanguage: job.SourceLanguage || job.sourceLanguage,
            targetLanguage: job.TargetLanguage || job.targetLanguage
        }))
    } catch (e) {
        error.value = 'Failed to fetch job queue'
        console.error('Failed to fetch job queue:', e)
    } finally {
        isLoading.value = false
    }
}

const refreshInterval = ref<number | null>(null)

onMounted(() => {
    fetchJobs()
    // Refresh every 30 seconds
    refreshInterval.value = window.setInterval(fetchJobs, 30000)
})

onUnmounted(() => {
    if (refreshInterval.value) {
        clearInterval(refreshInterval.value)
    }
})

const getStateColor = (state: string): string => {
    switch (state) {
        case 'running':
            return 'text-blue-400'
        case 'pending':
            return 'text-purple-400'
        case 'queued':
            return 'text-yellow-400'
        case 'scheduled':
            return 'text-yellow-400'
        case 'failed':
            return 'text-red-400'
        case 'completed':
            return 'text-green-400'
        default:
            return 'text-secondary-content'
    }
}

const getStateBg = (state: string): string => {
    switch (state) {
        case 'running':
            return 'bg-blue-500/20'
        case 'pending':
            return 'bg-purple-500/20'
        case 'queued':
            return 'bg-yellow-500/20'
        case 'scheduled':
            return 'bg-yellow-500/20'
        case 'failed':
            return 'bg-red-500/20'
        case 'completed':
            return 'bg-green-500/20'
        default:
            return 'bg-gray-500/20'
    }
}

const getQueueLabel = (queue?: string): string => {
    if (!queue) return ''
    switch (queue) {
        case 'priority':
            return '⚡'
        case 'translation':
            return '🌐'
        default:
            return ''
    }
}
</script>

<template>
    <CardComponent :title="i18n.translate('statistics.jobQueue')" class="h-full">
        <div class="mb-2 flex justify-end">
            <button
                @click="fetchJobs"
                :disabled="isLoading"
                class="text-secondary-content hover:text-primary-content p-1 transition-colors"
                :title="i18n.translate('statistics.refresh')">
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="error" class="py-4 text-center text-sm text-red-400">
            {{ error }}
        </div>

        <div v-else-if="jobs.length === 0" class="text-secondary-content py-4 text-center text-sm">
            {{ i18n.translate('statistics.noJobs') }}
        </div>

        <div v-else class="max-h-64 space-y-2 overflow-y-auto">
            <div
                v-for="job in jobs"
                :key="job.id"
                class="rounded-md border border-gray-700 bg-black/30 p-2">
                <div class="flex items-center justify-between">
                    <div class="flex items-center gap-1">
                        <span v-if="getQueueLabel(job.queue)" class="text-xs">
                            {{ getQueueLabel(job.queue) }}
                        </span>
                        <span
                            class="text-primary-content max-w-[120px] truncate text-sm font-medium">
                            {{ job.name }}
                        </span>
                    </div>
                    <span
                        class="rounded-full px-2 py-0.5 text-xs"
                        :class="[getStateColor(job.state), getStateBg(job.state)]">
                        {{ job.state }}
                    </span>
                </div>

                <!-- Language info for translation jobs -->
                <div
                    v-if="job.sourceLanguage && job.targetLanguage"
                    class="text-secondary-content mt-1 text-xs">
                    {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                </div>

                <!-- Progress bar for running jobs -->
                <div
                    v-if="job.state === 'running' && job.progress !== undefined && job.progress > 0"
                    class="mt-2">
                    <div class="h-1.5 overflow-hidden rounded-full bg-gray-700">
                        <div
                            class="h-full bg-blue-500 transition-all duration-300"
                            :style="{ width: `${job.progress}%` }"></div>
                    </div>
                    <div class="text-secondary-content mt-1 text-xs">{{ job.progress }}%</div>
                </div>

                <!-- Next run time for scheduled jobs -->
                <div
                    v-if="job.state === 'scheduled' && job.nextRun"
                    class="text-secondary-content mt-1 text-xs">
                    {{ i18n.translate('statistics.nextRun') }}: {{ job.nextRun }}
                </div>

                <!-- Last run time for completed jobs -->
                <div
                    v-if="job.state === 'completed' && job.lastRun"
                    class="text-secondary-content mt-1 text-xs">
                    {{ i18n.translate('statistics.lastRun') }}: {{ job.lastRun }}
                </div>

                <!-- Error message for failed jobs -->
                <div v-if="job.state === 'failed' && job.error" class="mt-1 text-xs text-red-400">
                    {{ job.error }}
                </div>
            </div>
        </div>
    </CardComponent>
</template>

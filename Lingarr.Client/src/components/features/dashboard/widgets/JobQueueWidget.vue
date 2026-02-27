<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
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
            return 'text-accent'
        case 'pending':
            return 'text-primary-content'
        case 'queued':
        case 'scheduled':
            return 'text-primary-content/80'
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
            return 'bg-accent/10'
        case 'pending':
            return 'bg-secondary'
        case 'queued':
        case 'scheduled':
            return 'bg-secondary/50'
        case 'failed':
            return 'bg-red-500/10'
        case 'completed':
            return 'bg-green-500/10'
        default:
            return 'bg-secondary/30'
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
    <div class="flex h-full flex-col">
        <div class="mb-4 flex items-center justify-between">
            <h3 class="text-primary-content/70 text-sm font-medium">{{ i18n.translate('statistics.jobQueue') }}</h3>
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

        <div v-else-if="jobs.length === 0" class="text-secondary-content py-8 text-center text-sm italic opacity-70">
            {{ i18n.translate('statistics.noJobs') }}
        </div>

        <div v-else class="flex-1 space-y-3 overflow-y-auto overflow-x-hidden pr-1">
            <div
                v-for="job in jobs"
                :key="job.id"
                class="border-secondary/20 border-b pb-3 last:border-0 last:pb-0">
                <div class="flex items-center justify-between">
                    <div class="flex min-w-0 flex-1 items-center gap-1 pr-2">
                        <span v-if="getQueueLabel(job.queue)" class="shrink-0 text-xs">
                            {{ getQueueLabel(job.queue) }}
                        </span>
                        <span
                            class="text-primary-content truncate text-sm font-medium">
                            {{ job.name }}
                        </span>
                    </div>
                    <span
                        class="shrink-0 rounded-full px-2 py-0.5 text-xs font-medium"
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
                    <div class="bg-secondary h-1.5 overflow-hidden rounded-full">
                        <div
                            class="bg-accent h-full transition-all duration-300"
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
    </div>
</template>

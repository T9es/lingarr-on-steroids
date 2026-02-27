<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import services from '@/services'

const i18n = useI18n()

interface JobInfo {
    id: string
    name: string
    state: string
    progress?: number
    cron?: string
    lastExecution?: string
    nextExecution?: string
    scheduledAt?: string
    failedAt?: string
    completedAt?: string
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
        const data = await services.dashboard.getJobs<{ Jobs: JobInfo[] } | JobInfo[]>()
        const rawJobs = Array.isArray(data) ? data : (data as any).Jobs || []

        jobs.value = rawJobs.map((job: any) => ({
            id: job.Id || job.id,
            name: job.Name || job.name,
            state: (job.State || job.state || '').toLowerCase(),
            progress: job.Progress || job.progress,
            cron: job.Cron || job.cron,
            lastExecution: job.LastExecution || job.lastExecution,
            nextExecution: job.NextExecution || job.nextExecution,
            scheduledAt: job.ScheduledAt || job.scheduledAt,
            failedAt: job.FailedAt || job.failedAt,
            completedAt: job.CompletedAt || job.completedAt,
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
    refreshInterval.value = window.setInterval(fetchJobs, 30000)
})

onUnmounted(() => {
    if (refreshInterval.value) {
        clearInterval(refreshInterval.value)
    }
})

const scheduledJobs = computed(() =>
    jobs.value.filter(
        (j) =>
            j.state === 'scheduled' ||
            j.state === 'pending' ||
            j.state === 'queued' ||
            j.queue === 'recurring'
    )
)
const completedJobs = computed(() => jobs.value.filter((j) => j.state === 'completed'))
const failedJobs = computed(() => jobs.value.filter((j) => j.state === 'failed'))

const getStateBadge = (state: string): { icon: string; color: string } => {
    switch (state) {
        case 'scheduled':
        case 'pending':
        case 'queued':
            return { icon: '⏳', color: 'text-yellow-400' }
        case 'failed':
            return { icon: '⚠', color: 'text-red-400' }
        case 'completed':
            return { icon: '✓', color: 'text-green-400' }
        default:
            return { icon: '○', color: 'text-primary-content/50' }
    }
}

const formatRelativeTime = (dateStr?: string): string => {
    if (!dateStr) return ''
    const diff = Date.now() - new Date(dateStr).getTime()
    const minutes = Math.floor(diff / 60000)
    const hours = Math.floor(diff / 3600000)
    const days = Math.floor(diff / 86400000)

    if (minutes < 1) return 'just now'
    if (minutes < 60) return `${minutes}m ago`
    if (hours < 24) return `${hours}h ago`
    return `${days}d ago`
}

const formatNextRun = (dateStr?: string): string => {
    if (!dateStr) return ''
    const date = new Date(dateStr)
    const now = new Date()
    const diff = date.getTime() - now.getTime()
    const minutes = Math.floor(diff / 60000)
    const hours = Math.floor(diff / 3600000)
    const days = Math.floor(diff / 86400000)

    if (diff < 0) return 'overdue'
    if (minutes < 60) return `in ${minutes}m`
    if (hours < 24) return `in ${hours}h`
    if (days < 7) return `in ${days}d`
    return date.toLocaleDateString()
}

const formatCron = (cron?: string): string => {
    if (!cron) return ''
    if (cron.includes('*/15 * * * *')) return 'Every 15m'
    if (cron.includes('0 * * * *')) return 'Hourly'
    if (cron.includes('0 0 * * *')) return 'Daily'
    if (cron.includes('0 0 * * 0')) return 'Weekly'
    return cron
}

const getJobDisplayName = (job: JobInfo): string => {
    if (job.name === 'AutomatedTranslationJob') return 'Auto Translation'
    if (job.name === 'CleanupJob') return 'Cleanup'
    if (job.name === 'RetryFailedRequestsJob') return 'Retry Failed'
    if (job.name === 'StatisticsJob') return 'Statistics'
    if (job.name === 'SyncMovieJob') return 'Sync Movies'
    if (job.name === 'SyncShowJob') return 'Sync Shows'
    return job.name
}
</script>

<template>
    <div class="flex h-full flex-col">
        <div class="mb-3 flex items-center justify-between">
            <h3 class="text-primary-content/70 text-sm font-medium">
                {{ i18n.translate('statistics.jobQueue') }}
            </h3>
            <button
                @click="fetchJobs"
                :disabled="isLoading"
                class="text-primary-content/50 hover:text-primary-content p-1 transition-colors"
                :title="i18n.translate('statistics.refresh')">
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="error" class="py-4 text-center text-sm text-red-400">
            {{ error }}
        </div>

        <div
            v-else-if="jobs.length === 0"
            class="text-primary-content/50 flex flex-1 items-center justify-center text-sm">
            <div class="text-center">
                <div class="mb-2 text-2xl">✓</div>
                <div>{{ i18n.translate('statistics.noJobs') }}</div>
            </div>
        </div>

        <div v-else class="min-h-0 flex-1 space-y-2 overflow-y-auto pr-1">
            <!-- Scheduled Jobs -->
            <div v-if="scheduledJobs.length > 0">
                <h4 class="text-primary-content/50 mb-1.5 text-xs font-medium">
                    Scheduled ({{ scheduledJobs.length }})
                </h4>
                <div class="space-y-1.5">
                    <div
                        v-for="job in scheduledJobs"
                        :key="job.id"
                        class="bg-secondary/30 rounded-md p-2">
                        <div class="flex items-center justify-between">
                            <div class="text-primary-content truncate text-xs font-medium">
                                {{ getJobDisplayName(job) }}
                            </div>
                            <span v-if="job.cron" class="text-primary-content/40 text-xs">
                                {{ formatCron(job.cron) }}
                            </span>
                        </div>
                        <div class="text-primary-content/50 flex items-center gap-2 text-xs">
                            <span v-if="job.sourceLanguage && job.targetLanguage">
                                {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                            </span>
                            <span v-if="job.nextExecution">
                                • {{ formatNextRun(job.nextExecution) }}
                            </span>
                            <span v-else-if="job.scheduledAt">
                                • {{ formatNextRun(job.scheduledAt) }}
                            </span>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Recently Completed -->
            <div v-if="completedJobs.length > 0">
                <h4 class="mb-1.5 text-xs font-medium text-green-400/70">
                    Recently Completed ({{ completedJobs.length }})
                </h4>
                <div class="space-y-1.5">
                    <div
                        v-for="job in completedJobs"
                        :key="job.id"
                        class="rounded-md bg-green-500/10 p-2">
                        <div class="text-primary-content truncate text-xs font-medium">
                            {{ job.name }}
                        </div>
                        <div class="text-primary-content/50 flex items-center gap-2 text-xs">
                            <span v-if="job.sourceLanguage && job.targetLanguage">
                                {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                            </span>
                            <span v-if="job.completedAt">
                                • {{ formatRelativeTime(job.completedAt) }}
                            </span>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Failed Jobs -->
            <div v-if="failedJobs.length > 0">
                <h4 class="mb-1.5 text-xs font-medium text-red-400/70">
                    Failed ({{ failedJobs.length }})
                </h4>
                <div class="space-y-1.5">
                    <div
                        v-for="job in failedJobs"
                        :key="job.id"
                        class="rounded-md bg-red-500/10 p-2">
                        <div class="text-primary-content truncate text-xs font-medium">
                            {{ job.name }}
                        </div>
                        <div class="text-primary-content/50 flex items-center gap-2 text-xs">
                            <span v-if="job.sourceLanguage && job.targetLanguage">
                                {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                            </span>
                        </div>
                        <div
                            v-if="job.error"
                            class="mt-1 truncate text-xs text-red-400"
                            :title="job.error">
                            {{ job.error }}
                        </div>
                        <div v-if="job.failedAt" class="text-primary-content/40 mt-1 text-xs">
                            {{ formatRelativeTime(job.failedAt) }}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>

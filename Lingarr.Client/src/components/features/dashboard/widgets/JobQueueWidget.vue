<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import TriggerJob from '@/components/common/TriggerJob.vue'
import { useScheduleStore } from '@/store/schedule'
import services from '@/services'

const i18n = useI18n()
const scheduleStore = useScheduleStore()

interface JobInfo {
    id: string
    name: string
    jobName?: string
    state: string
    progress?: number
    cron?: string
    lastExecution?: string
    nextExecution?: string
    scheduledAt?: string
    startedAt?: string
    failedAt?: string
    completedAt?: string
    error?: string
    queue?: string
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
            jobName: job.JobName || job.jobName,
            state: (job.State || job.state || '').toLowerCase(),
            progress: job.Progress || job.progress,
            cron: job.Cron || job.cron,
            lastExecution: job.LastExecution || job.lastExecution,
            nextExecution: job.NextExecution || job.nextExecution,
            scheduledAt: job.ScheduledAt || job.scheduledAt,
            failedAt: job.FailedAt || job.failedAt,
            completedAt: job.CompletedAt || job.completedAt,
            error: job.ErrorMessage || job.error,
            queue: job.Queue || job.queue
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
    refreshInterval.value = window.setInterval(fetchJobs, 10000)
})

onUnmounted(() => {
    if (refreshInterval.value) {
        clearInterval(refreshInterval.value)
    }
})

const runningJobs = computed(() =>
    jobs.value.filter((j) => j.state === 'running')
)

const scheduledJobs = computed(() =>
    jobs.value.filter((j) => j.state === 'scheduled' && j.jobName)
)

const failedJobs = computed(() => jobs.value.filter((j) => j.state === 'failed'))

const formatDuration = (dateStr?: string): string => {
    if (!dateStr) return ''
    const diff = Date.now() - new Date(dateStr).getTime()
    const minutes = Math.floor(diff / 60000)
    const hours = Math.floor(diff / 3600000)

    if (minutes < 1) return 'just started'
    if (minutes < 60) return `${minutes}m`
    return `${hours}h ${minutes % 60}m`
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
    if (minutes < 60) return `${minutes}m`
    if (hours < 24) return `${hours}h`
    if (days < 7) return `${days}d`
    return date.toLocaleDateString()
}

const formatCron = (cron?: string): string => {
    if (!cron) return ''
    if (cron.includes('*/15 * * * *')) return '15m'
    if (cron.includes('0 * * * *')) return '1h'
    if (cron.includes('0 0 * * *')) return '1d'
    if (cron.includes('0 0 * * 0')) return '1w'
    if (cron.includes('0 22 * * *')) return 'daily'
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

const triggerJob = async (jobName: string) => {
    await scheduleStore.startJob(jobName)
    setTimeout(fetchJobs, 1000)
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

        <div v-else class="min-h-0 flex-1 space-y-3 overflow-y-auto pr-1">
            <!-- Running Jobs -->
            <div v-if="runningJobs.length > 0">
                <h4 class="mb-1.5 flex items-center gap-1.5 text-xs font-medium text-green-400/80">
                    <span class="h-1.5 w-1.5 animate-pulse rounded-full bg-green-400"></span>
                    Running ({{ runningJobs.length }})
                </h4>
                <div class="space-y-1.5">
                    <div
                        v-for="job in runningJobs"
                        :key="job.id"
                        class="rounded-md border border-green-500/20 bg-green-500/5 p-2">
                        <div class="flex items-center justify-between">
                            <div class="text-primary-content truncate text-xs font-medium">
                                {{ getJobDisplayName(job) }}
                            </div>
                        </div>
                        <div class="text-primary-content/50 mt-0.5 text-xs">
                            Running for {{ formatDuration(job.startedAt) }}
                        </div>
                    </div>
                </div>
            </div>

            <!-- Scheduled Jobs -->
            <div v-if="scheduledJobs.length > 0">
                <h4 class="mb-1.5 text-xs font-medium text-primary-content/50">
                    Scheduled ({{ scheduledJobs.length }})
                </h4>
                <div class="space-y-1.5">
                    <div
                        v-for="job in scheduledJobs"
                        :key="job.id"
                        class="bg-secondary/30 hover:bg-secondary/50 rounded-md p-2 transition-colors">
                        <div class="flex items-center justify-between gap-2">
                            <div class="text-primary-content truncate text-xs font-medium">
                                {{ getJobDisplayName(job) }}
                            </div>
                            <div class="flex shrink-0 items-center gap-2">
                                <span v-if="job.cron" class="text-primary-content/40 text-xs">
                                    {{ formatCron(job.cron) }}
                                </span>
                                <TriggerJob
                                    @toggle:trigger="triggerJob(job.jobName!)"
                                    class="shrink-0" />
                            </div>
                        </div>
                        <div class="text-primary-content/50 mt-0.5 text-xs">
                            <span v-if="job.nextExecution">
                                Next: {{ formatNextRun(job.nextExecution) }}
                            </span>
                            <span v-else-if="job.lastExecution">
                                Last: {{ formatDuration(job.lastExecution) }} ago
                            </span>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Failed Jobs -->
            <div v-if="failedJobs.length > 0">
                <h4 class="mb-1.5 flex items-center gap-1.5 text-xs font-medium text-red-400/80">
                    <span class="text-red-400">⚠</span>
                    Failed ({{ failedJobs.length }})
                </h4>
                <div class="space-y-1.5">
                    <div
                        v-for="job in failedJobs"
                        :key="job.id"
                        class="rounded-md border border-red-500/20 bg-red-500/5 p-2">
                        <div class="text-primary-content truncate text-xs font-medium">
                            {{ getJobDisplayName(job) }}
                        </div>
                        <div
                            v-if="job.error"
                            class="mt-1 truncate text-xs text-red-400"
                            :title="job.error">
                            {{ job.error }}
                        </div>
                        <div v-if="job.failedAt" class="text-primary-content/40 mt-0.5 text-xs">
                            {{ formatDuration(job.failedAt) }} ago
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import services from '@/services'

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
const queuedExpanded = ref(false)

const fetchJobs = async () => {
    isLoading.value = true
    error.value = null

    try {
        const data = await services.dashboard.getJobs<{ Jobs: JobInfo[] } | JobInfo[]>()
        const rawJobs = Array.isArray(data) ? data : (data as any).Jobs || []

        jobs.value = rawJobs.map((job: any) => ({
            id: job.Id || job.id,
            name: job.Name || job.name,
            state: (job.State || job.state || '').toLowerCase() as JobInfo['state'],
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
    refreshInterval.value = window.setInterval(fetchJobs, 30000)
})

onUnmounted(() => {
    if (refreshInterval.value) {
        clearInterval(refreshInterval.value)
    }
})

const runningJobs = computed(() => jobs.value.filter((j) => j.state === 'running'))
const queuedJobs = computed(() =>
    jobs.value.filter((j) => j.state === 'queued' || j.state === 'pending' || j.state === 'scheduled')
)
const failedJobs = computed(() => jobs.value.filter((j) => j.state === 'failed'))

const getStateIcon = (state: string): string => {
    switch (state) {
        case 'running':
            return '▶'
        case 'pending':
        case 'queued':
        case 'scheduled':
            return '⏳'
        case 'failed':
            return '⚠'
        case 'completed':
            return '✓'
        default:
            return '○'
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

const formatScheduledTime = (dateStr?: string): string => {
    if (!dateStr) return ''
    const date = new Date(dateStr)
    const now = new Date()
    const diff = date.getTime() - now.getTime()
    const minutes = Math.floor(diff / 60000)
    const hours = Math.floor(diff / 3600000)

    if (diff < 0) return 'overdue'
    if (minutes < 60) return `in ${minutes}m`
    if (hours < 24) return `in ${hours}h`
    return date.toLocaleDateString()
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

        <div v-if="error" class="text-red-400 py-4 text-center text-sm">
            {{ error }}
        </div>

        <div v-else-if="jobs.length === 0" class="text-primary-content/50 flex flex-1 items-center justify-center text-sm">
            <div class="text-center">
                <div class="mb-2 text-2xl">✓</div>
                <div>{{ i18n.translate('statistics.noJobs') }}</div>
            </div>
        </div>

        <div v-else class="flex-1 space-y-2 overflow-y-auto pr-1">
            <!-- Running Jobs (Always Expanded) -->
            <div v-if="runningJobs.length > 0">
                <h4 class="text-accent mb-1.5 text-xs font-medium">
                    {{ getStateIcon('running') }} Running ({{ runningJobs.length }})
                </h4>
                <div class="space-y-2">
                    <div
                        v-for="job in runningJobs"
                        :key="job.id"
                        class="bg-accent/10 border-accent/30 rounded-md border p-2">
                        <div class="text-primary-content truncate text-sm font-medium">
                            {{ job.name }}
                        </div>
                        <div v-if="job.sourceLanguage && job.targetLanguage" class="text-primary-content/60 text-xs">
                            {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                        </div>
                        <div v-if="job.progress !== undefined && job.progress > 0" class="mt-2">
                            <div class="flex items-center justify-between text-xs">
                                <span class="text-primary-content/60">Progress</span>
                                <span class="text-accent font-medium">{{ job.progress }}%</span>
                            </div>
                            <div class="bg-secondary mt-1 h-1.5 overflow-hidden rounded-full">
                                <div
                                    class="bg-accent h-full rounded-full transition-all duration-300"
                                    :style="{ width: `${job.progress}%` }"></div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Queued Jobs (Collapsible) -->
            <div v-if="queuedJobs.length > 0">
                <button
                    @click="queuedExpanded = !queuedExpanded"
                    class="text-primary-content/70 hover:text-primary-content w-full text-left text-xs font-medium transition-colors">
                    <span class="mr-1">{{ queuedExpanded ? '▼' : '▶' }}</span>
                    {{ getStateIcon('queued') }} Queued ({{ queuedJobs.length }})
                </button>
                <div v-if="queuedExpanded" class="mt-1.5 space-y-1.5">
                    <div
                        v-for="job in queuedJobs"
                        :key="job.id"
                        class="bg-secondary/30 rounded-md p-2">
                        <div class="text-primary-content truncate text-xs font-medium">
                            {{ job.name }}
                        </div>
                        <div class="text-primary-content/50 flex items-center gap-2 text-xs">
                            <span v-if="job.sourceLanguage && job.targetLanguage">
                                {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                            </span>
                            <span v-if="job.nextRun">• {{ formatScheduledTime(job.nextRun) }}</span>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Failed Jobs -->
            <div v-if="failedJobs.length > 0">
                <h4 class="text-red-400 mb-1.5 text-xs font-medium">
                    {{ getStateIcon('failed') }} Failed ({{ failedJobs.length }})
                </h4>
                <div class="space-y-2">
                    <div
                        v-for="job in failedJobs"
                        :key="job.id"
                        class="bg-red-500/10 border-red-500/30 rounded-md border p-2">
                        <div class="text-primary-content truncate text-sm font-medium">
                            {{ job.name }}
                        </div>
                        <div v-if="job.sourceLanguage && job.targetLanguage" class="text-primary-content/60 text-xs">
                            {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                        </div>
                        <div v-if="job.error" class="text-red-400 mt-1 truncate text-xs" :title="job.error">
                            {{ job.error }}
                        </div>
                        <div v-if="job.lastRun" class="text-primary-content/40 mt-1 text-xs">
                            {{ formatRelativeTime(job.lastRun) }}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
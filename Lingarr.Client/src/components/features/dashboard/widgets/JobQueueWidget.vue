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
        const rawJobs = Array.isArray(data) ? data : (data.jobs || data.Jobs || [])
        
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
        case 'running': return 'text-blue-400'
        case 'pending': return 'text-purple-400'
        case 'queued': return 'text-yellow-400'
        case 'scheduled': return 'text-yellow-400'
        case 'failed': return 'text-red-400'
        case 'completed': return 'text-green-400'
        default: return 'text-secondary-content'
    }
}

const getStateBg = (state: string): string => {
    switch (state) {
        case 'running': return 'bg-blue-500/20'
        case 'pending': return 'bg-purple-500/20'
        case 'queued': return 'bg-yellow-500/20'
        case 'scheduled': return 'bg-yellow-500/20'
        case 'failed': return 'bg-red-500/20'
        case 'completed': return 'bg-green-500/20'
        default: return 'bg-gray-500/20'
    }
}

const getQueueLabel = (queue?: string): string => {
    if (!queue) return ''
    switch (queue) {
        case 'priority': return '⚡'
        case 'translation': return '🌐'
        default: return ''
    }
}
</script>

<template>
    <CardComponent :title="i18n.translate('statistics.jobQueue')" class="h-full">
        <div class="flex justify-end mb-2">
            <button 
                @click="fetchJobs" 
                :disabled="isLoading"
                class="p-1 text-secondary-content hover:text-primary-content transition-colors"
                :title="i18n.translate('statistics.refresh')"
            >
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="error" class="text-red-400 text-sm py-4 text-center">
            {{ error }}
        </div>

        <div v-else-if="jobs.length === 0" class="text-secondary-content text-sm py-4 text-center">
            {{ i18n.translate('statistics.noJobs') }}
        </div>

        <div v-else class="space-y-2 max-h-64 overflow-y-auto">
            <div 
                v-for="job in jobs" 
                :key="job.id"
                class="p-2 rounded-md bg-black/30 border border-gray-700"
            >
                <div class="flex items-center justify-between">
                    <div class="flex items-center gap-1">
                        <span v-if="getQueueLabel(job.queue)" class="text-xs">{{ getQueueLabel(job.queue) }}</span>
                        <span class="text-sm font-medium text-primary-content truncate max-w-[120px]">{{ job.name }}</span>
                    </div>
                    <span 
                        class="text-xs px-2 py-0.5 rounded-full"
                        :class="[getStateColor(job.state), getStateBg(job.state)]"
                    >
                        {{ job.state }}
                    </span>
                </div>
                
                <!-- Language info for translation jobs -->
                <div v-if="job.sourceLanguage && job.targetLanguage" class="text-xs text-secondary-content mt-1">
                    {{ job.sourceLanguage }} → {{ job.targetLanguage }}
                </div>
                
                <!-- Progress bar for running jobs -->
                <div v-if="job.state === 'running' && job.progress !== undefined && job.progress > 0" class="mt-2">
                    <div class="h-1.5 bg-gray-700 rounded-full overflow-hidden">
                        <div 
                            class="h-full bg-blue-500 transition-all duration-300"
                            :style="{ width: `${job.progress}%` }"
                        ></div>
                    </div>
                    <div class="text-xs text-secondary-content mt-1">{{ job.progress }}%</div>
                </div>

                <!-- Next run time for scheduled jobs -->
                <div v-if="job.state === 'scheduled' && job.nextRun" class="text-xs text-secondary-content mt-1">
                    {{ i18n.translate('statistics.nextRun') }}: {{ job.nextRun }}
                </div>

                <!-- Last run time for completed jobs -->
                <div v-if="job.state === 'completed' && job.lastRun" class="text-xs text-secondary-content mt-1">
                    {{ i18n.translate('statistics.lastRun') }}: {{ job.lastRun }}
                </div>

                <!-- Error message for failed jobs -->
                <div v-if="job.state === 'failed' && job.error" class="text-xs text-red-400 mt-1">
                    {{ job.error }}
                </div>
            </div>
        </div>
    </CardComponent>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CardComponent from '@/components/common/CardComponent.vue'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'

const i18n = useI18n()

interface JobInfo {
    id: string
    name: string
    state: 'running' | 'scheduled' | 'failed' | 'completed'
    progress?: number
    nextRun?: string
    lastRun?: string
    error?: string
}

const jobs = ref<JobInfo[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)

// TODO: Connect to backend endpoint in Phase 4
// For now, show placeholder data
const fetchJobs = async () => {
    isLoading.value = true
    error.value = null
    
    // Placeholder data - will be replaced with actual API call
    // GET /api/jobs/status
    jobs.value = [
        { id: '1', name: 'Sync Movies', state: 'scheduled', nextRun: 'In 2 hours' },
        { id: '2', name: 'Sync Shows', state: 'scheduled', nextRun: 'In 3 hours' },
        { id: '3', name: 'Translation Queue', state: 'running', progress: 45 },
        { id: '4', name: 'Cleanup Job', state: 'completed', lastRun: '2 hours ago' }
    ]
    
    isLoading.value = false
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
        case 'scheduled': return 'text-yellow-400'
        case 'failed': return 'text-red-400'
        case 'completed': return 'text-green-400'
        default: return 'text-gray-400'
    }
}

const getStateBg = (state: string): string => {
    switch (state) {
        case 'running': return 'bg-blue-500/20'
        case 'scheduled': return 'bg-yellow-500/20'
        case 'failed': return 'bg-red-500/20'
        case 'completed': return 'bg-green-500/20'
        default: return 'bg-gray-500/20'
    }
}
</script>

<template>
    <CardComponent :title="i18n.translate('statistics.jobQueue')" class="h-full">
        <div class="flex justify-end mb-2">
            <button 
                @click="fetchJobs" 
                :disabled="isLoading"
                class="p-1 text-gray-400 hover:text-white transition-colors"
                :title="i18n.translate('statistics.refresh')"
            >
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="error" class="text-red-400 text-sm py-4 text-center">
            {{ error }}
        </div>

        <div v-else-if="jobs.length === 0" class="text-gray-400 text-sm py-4 text-center">
            {{ i18n.translate('statistics.noJobs') }}
        </div>

        <div v-else class="space-y-2">
            <div 
                v-for="job in jobs" 
                :key="job.id"
                class="p-2 rounded-md bg-black/30 border border-gray-700"
            >
                <div class="flex items-center justify-between">
                    <span class="text-sm font-medium text-white">{{ job.name }}</span>
                    <span 
                        class="text-xs px-2 py-0.5 rounded-full"
                        :class="[getStateColor(job.state), getStateBg(job.state)]"
                    >
                        {{ job.state }}
                    </span>
                </div>
                
                <!-- Progress bar for running jobs -->
                <div v-if="job.state === 'running' && job.progress !== undefined" class="mt-2">
                    <div class="h-1.5 bg-gray-700 rounded-full overflow-hidden">
                        <div 
                            class="h-full bg-blue-500 transition-all duration-300"
                            :style="{ width: `${job.progress}%` }"
                        ></div>
                    </div>
                    <div class="text-xs text-gray-400 mt-1">{{ job.progress }}%</div>
                </div>

                <!-- Next run time for scheduled jobs -->
                <div v-if="job.state === 'scheduled' && job.nextRun" class="text-xs text-gray-400 mt-1">
                    {{ i18n.translate('statistics.nextRun') }}: {{ job.nextRun }}
                </div>

                <!-- Last run time for completed jobs -->
                <div v-if="job.state === 'completed' && job.lastRun" class="text-xs text-gray-400 mt-1">
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

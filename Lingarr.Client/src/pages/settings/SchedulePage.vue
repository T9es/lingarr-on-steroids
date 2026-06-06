<template>
    <div class="w-full">
        <div class="bg-tertiary flex flex-wrap items-center justify-end gap-2 p-4">
            <ReloadComponent @toggle:update="fetchJobs" />
        </div>

        <div class="w-full space-y-2 px-4">
            <div
                v-for="job in jobs"
                :key="job.id"
                class="border-accent rounded border p-4">
                <div class="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
                    <div class="flex flex-1 flex-col gap-1">
                        <div class="flex items-center gap-2">
                            <span class="font-semibold">{{ getJobDisplayName(job) }}</span>
                            <span
                                v-if="job.isCurrentlyRunning"
                                class="bg-accent/20 text-accent rounded px-2 py-0.5 text-xs">
                                {{ translate('schedule.running') }}
                            </span>
                        </div>
                        <div class="text-primary-content/60 flex flex-wrap gap-x-4 gap-y-1 text-sm">
                            <span>
                                {{ translate('schedule.state') }}:
                                {{ translate(`schedule.${job.currentState.toLowerCase()}`) }}
                            </span>
                            <span v-if="job.lastExecution">
                                {{ translate('schedule.lastExecution') }}:
                                {{ formatDateTime(job.lastExecution) }}
                            </span>
                            <span v-if="job.nextExecution">
                                {{ translate('schedule.nextExecution') }}:
                                {{ formatDateTime(job.nextExecution) }}
                            </span>
                        </div>
                    </div>

                    <div class="flex items-center gap-3">
                        <div class="flex items-center gap-2">
                            <ToggleButton
                                :model-value="getEnabledValue(job)"
                                @update:model-value="setEnabled(job, $event)">
                                <span class="text-primary-content text-sm font-medium">
                                    {{
                                        getEnabledValue(job)
                                            ? translate('common.enabled')
                                            : translate('common.disabled')
                                    }}
                                </span>
                            </ToggleButton>
                        </div>

                        <TriggerJob
                            :title="translate('schedule.run')"
                            @toggle:trigger="scheduleStore.startJob(job.id)" />
                    </div>
                </div>

                <div class="mt-3">
                    <ScheduleSelector
                        :key="job.id + '_cron_' + getScheduleValue(job)"
                        :model-value="getScheduleValue(job)"
                        :label="translate('schedule.cronLabel')"
                        @update:model-value="setSchedule(job, $event)"
                        @update:validation="() => void 0" />
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { Hub } from '@/ts'
import { formatDateTime } from '@/utils/date'
import { useSignalR } from '@/composables/useSignalR'
import { useScheduleStore } from '@/store/schedule'
import { SETTINGS } from '@/ts'
import services from '@/services'
import ReloadComponent from '@/components/common/ReloadComponent.vue'
import TriggerJob from '@/components/common/TriggerJob.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import ScheduleSelector from '@/components/common/ScheduleSelector.vue'
import { useI18n } from '@/plugins/i18n'
import type { IRecurringJob } from '@/ts'

const scheduleStore = useScheduleStore()
const signalR = useSignalR()
const hubConnection = ref<Hub>()
const { translate } = useI18n()

const jobs = ref<IRecurringJob[]>([])
const settingsCache = ref<Record<string, string>>({})

const jobDisplayNames: Record<string, string> = {
    AutomatedTranslationJob: 'schedule.jobDisplay.automatedTranslation',
    CustomSourceScanJob: 'schedule.jobDisplay.customSources',
    SyncMovieJob: 'schedule.jobDisplay.syncMovies',
    SyncShowJob: 'schedule.jobDisplay.syncShows',
    CleanupJob: 'schedule.jobDisplay.cleanup',
    UploadWorkspaceCleanupJob: 'schedule.jobDisplay.uploadCleanup',
    StatisticsJob: 'schedule.jobDisplay.statistics',
    RetryFailedRequestsJob: 'schedule.jobDisplay.retryFailed',
    UnknownLanguageDetectionJob: 'schedule.jobDisplay.languageDetection'
}

const enabledSettingMap: Record<string, string> = {
    AutomatedTranslationJob: SETTINGS.AUTOMATION_ENABLED,
    CustomSourceScanJob: SETTINGS.CUSTOM_SOURCE_SCAN_ENABLED,
    SyncMovieJob: SETTINGS.MOVIE_SYNC_ENABLED,
    SyncShowJob: SETTINGS.SHOW_SYNC_ENABLED,
    CleanupJob: SETTINGS.MAINTENANCE_CLEANUP_ENABLED,
    UploadWorkspaceCleanupJob: SETTINGS.MAINTENANCE_UPLOAD_CLEANUP_ENABLED,
    StatisticsJob: SETTINGS.MAINTENANCE_STATISTICS_ENABLED,
    RetryFailedRequestsJob: SETTINGS.MAINTENANCE_RETRY_FAILED_ENABLED,
    UnknownLanguageDetectionJob: SETTINGS.DETECT_UNKNOWN_LANGUAGES
}

const cronSettingMap: Record<string, string> = {
    AutomatedTranslationJob: SETTINGS.TRANSLATION_SCHEDULE,
    CustomSourceScanJob: SETTINGS.CUSTOM_SOURCE_SCAN_SCHEDULE,
    SyncMovieJob: SETTINGS.MOVIE_SCHEDULE,
    SyncShowJob: SETTINGS.SHOW_SCHEDULE,
    CleanupJob: SETTINGS.MAINTENANCE_CLEANUP_SCHEDULE,
    UploadWorkspaceCleanupJob: SETTINGS.MAINTENANCE_UPLOAD_CLEANUP_SCHEDULE,
    StatisticsJob: SETTINGS.MAINTENANCE_STATISTICS_SCHEDULE,
    RetryFailedRequestsJob: SETTINGS.MAINTENANCE_RETRY_FAILED_SCHEDULE,
    UnknownLanguageDetectionJob: SETTINGS.DETECT_UNKNOWN_LANGUAGES_SCHEDULE
}

function getJobDisplayName(job: IRecurringJob): string {
    const key = jobDisplayNames[job.id]
    return key ? translate(key) : job.id
}

function getEnabledValue(job: IRecurringJob): boolean {
    const key = enabledSettingMap[job.id]
    if (!key) return true
    return settingsCache.value[key] === 'true'
}

function getScheduleValue(job: IRecurringJob): string {
    const key = cronSettingMap[job.id]
    if (!key) return job.cron || ''
    return settingsCache.value[key] || job.cron || ''
}

async function setEnabled(job: IRecurringJob, value: string | boolean): Promise<void> {
    const key = enabledSettingMap[job.id]
    if (!key) return
    const strValue = value === true ? 'true' : value === false ? 'false' : (value as string)
    settingsCache.value[key] = strValue
    await services.setting.setSetting(key, strValue)
}

async function setSchedule(job: IRecurringJob, value: string): Promise<void> {
    const key = cronSettingMap[job.id]
    if (!key) return
    settingsCache.value[key] = value
    await services.setting.setSetting(key, value)
}

async function fetchJobs(): Promise<void> {
    const allKeys = Object.values(SETTINGS).filter((k) => typeof k === 'string')
    const result = await services.setting.getSettings<Record<string, string>>(allKeys)
    settingsCache.value = result || {}

    await scheduleStore.fetchRecurringJobs()
    jobs.value = scheduleStore.getRecurringJobs.slice()
}

onMounted(async () => {
    await fetchJobs()
    hubConnection.value = await signalR.connect('JobProgress', '/signalr/JobProgress')
    await hubConnection.value.joinGroup({ group: 'JobProgress' })

    hubConnection.value.on('JobStateUpdated', (jobId: string, state: string) => {
        const job = jobs.value.find((j) => j.id === jobId)
        if (job) {
            job.currentState = state
        }
    })
})

onUnmounted(async () => {
    hubConnection.value?.off('JobStateUpdated', () => {})
})
</script>

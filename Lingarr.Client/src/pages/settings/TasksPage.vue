<template>
    <div class="grid grid-flow-row auto-rows-max grid-cols-1 gap-4 p-4 md:grid-cols-2 xl:grid-cols-3">
        <!-- Translation Limits -->
        <CardComponent
            class="md:col-span-2 xl:col-span-3"
            :title="translate('settings.automation.limitsHeader')">
            <template #description>
                {{ translate('settings.automation.limitsDescription') }}
            </template>
            <template #actions>
                <ReloadComponent @toggle:update="fetchJobs" />
            </template>
            <template #content>
                <InputComponent
                    :model-value="maxTranslationsPerRun"
                    input-type="number"
                    validation-type="number"
                    :min-length="0"
                    :label="translate('settings.automation.scheduleLimitLabel')"
                    @update:model-value="setMaxTranslationsPerRun"
                    @update:validation="() => void 0" />

                <span class="font-semibold">
                    {{ translate('settings.automation.defaultAgeThresholdLabel') }}
                </span>
                <InputComponent
                    :model-value="movieAgeThreshold"
                    input-type="number"
                    validation-type="number"
                    :min-length="0"
                    :label="translate('settings.automation.movieAgeThresholdLabel')"
                    @update:value="setMovieAgeThreshold"
                    @update:validation="() => void 0" />
                <InputComponent
                    :model-value="showAgeThreshold"
                    input-type="number"
                    validation-type="number"
                    :min-length="0"
                    :label="translate('settings.automation.showAgeThresholdLabel')"
                    @update:value="setShowAgeThreshold"
                    @update:validation="() => void 0" />
            </template>
        </CardComponent>

        <!-- Loading state -->
        <CardComponent v-if="!loaded" class="md:col-span-2 xl:col-span-3">
            <template #content>
                <div class="text-primary-content/60 py-4 text-center text-sm">
                    {{ translate('common.loading') }}
                </div>
            </template>
        </CardComponent>

        <!-- Empty state -->
        <CardComponent
            v-else-if="jobs.length === 0"
            class="md:col-span-2 xl:col-span-3"
            :title="translate('navigation.tasks')">
            <template #description>
                {{ translate('schedule.noJobs') }}
            </template>
        </CardComponent>

        <!-- Job Cards -->
        <CardComponent v-for="job in jobs" v-else :key="job.id">
            <template #actions>
                <div class="flex items-center gap-2">
                    <TriggerJob
                        :title="translate('schedule.run')"
                        @toggle:trigger="scheduleStore.startJob(job.id)" />
                </div>
            </template>
            <template #icon>
                <TaskIcon class="h-5 w-5" />
            </template>
            <template #header>
                <span class="flex items-center gap-2">
                    <span class="font-semibold">{{ getJobDisplayName(job) }}</span>
                    <span
                        v-if="job.isCurrentlyRunning"
                        class="bg-accent/20 text-accent rounded px-2 py-0.5 text-xs">
                        {{ translate('schedule.running') }}
                    </span>
                </span>
            </template>
            <template #description>
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
            </template>
            <template #content>
                <div class="flex items-center justify-between gap-3">
                    <span class="text-primary-content text-sm font-medium">
                        {{
                            getEnabledValue(job)
                                ? translate('common.enabled')
                                : translate('common.disabled')
                        }}
                    </span>
                    <ToggleButton
                        :model-value="getEnabledValue(job)"
                        @update:model-value="setEnabled(job, $event)" />
                </div>
                <ScheduleSelector
                    :key="job.id + '_cron_' + getScheduleValue(job)"
                    :model-value="getScheduleValue(job)"
                    :label="translate('schedule.cronLabel')"
                    @update:model-value="setSchedule(job, $event)"
                    @update:validation="() => void 0" />
            </template>
        </CardComponent>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { Hub } from '@/ts'
import { formatDateTime } from '@/utils/date'
import { useSignalR } from '@/composables/useSignalR'
import { useScheduleStore } from '@/store/schedule'
import { SETTINGS } from '@/ts'
import services from '@/services'
import CardComponent from '@/components/common/CardComponent.vue'
import ReloadComponent from '@/components/common/ReloadComponent.vue'
import TriggerJob from '@/components/common/TriggerJob.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import ScheduleSelector from '@/components/common/ScheduleSelector.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import TaskIcon from '@/components/icons/TaskIcon.vue'
import { useI18n } from '@/plugins/i18n'
import type { IRecurringJob } from '@/ts'

const scheduleStore = useScheduleStore()
const signalR = useSignalR()
const hubConnection = ref<Hub>()
const { translate } = useI18n()

const jobs = ref<IRecurringJob[]>([])
const settingsCache = ref<Record<string, string>>({})
const loaded = ref(false)
type JobStateUpdatedCallback = (jobId: string, state: string) => void
let jobStateUpdatedHandler: JobStateUpdatedCallback | undefined

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

// Computed settings for limits card
const maxTranslationsPerRun = computed((): string => settingsCache.value[SETTINGS.MAX_TRANSLATIONS_PER_RUN] || '10')
const movieAgeThreshold = computed((): string => settingsCache.value[SETTINGS.MOVIE_AGE_THRESHOLD] || '172800')
const showAgeThreshold = computed((): string => settingsCache.value[SETTINGS.SHOW_AGE_THRESHOLD] || '172800')

function getJobDisplayName(job: IRecurringJob): string {
    const key = jobDisplayNames[job.id]
    return key ? translate(key) : job.id
}

function getEnabledValue(job: IRecurringJob): boolean {
    const key = enabledSettingMap[job.id]
    if (!key) return true
    // Prefer the live settings cache (e.g., after toggling). Fall back to API-provided isEnabled.
    if (key in settingsCache.value) {
        return settingsCache.value[key] === 'true'
    }
    return job.isEnabled ?? true
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

async function setMaxTranslationsPerRun(value: string): Promise<void> {
    settingsCache.value[SETTINGS.MAX_TRANSLATIONS_PER_RUN] = value
    await services.setting.setSetting(SETTINGS.MAX_TRANSLATIONS_PER_RUN, value)
}

async function setMovieAgeThreshold(value: string): Promise<void> {
    settingsCache.value[SETTINGS.MOVIE_AGE_THRESHOLD] = value
    await services.setting.setSetting(SETTINGS.MOVIE_AGE_THRESHOLD, value)
}

async function setShowAgeThreshold(value: string): Promise<void> {
    settingsCache.value[SETTINGS.SHOW_AGE_THRESHOLD] = value
    await services.setting.setSetting(SETTINGS.SHOW_AGE_THRESHOLD, value)
}

async function fetchJobs(): Promise<void> {
    const allKeys = Object.values(SETTINGS).filter((k) => typeof k === 'string')
    const result = await services.setting.getSettings<Record<string, string>>(allKeys)
    settingsCache.value = result || {}

    await scheduleStore.fetchRecurringJobs()
    jobs.value = scheduleStore.getRecurringJobs.slice()
    loaded.value = true
}

onMounted(async () => {
    await fetchJobs()
    hubConnection.value = await signalR.connect('JobProgress', '/signalr/JobProgress')
    await hubConnection.value.joinGroup({ group: 'JobProgress' })

    jobStateUpdatedHandler = (jobId: string, state: string) => {
        const job = jobs.value.find((j) => j.id === jobId)
        if (job) {
            job.currentState = state
        }
    }
    hubConnection.value.on('JobStateUpdated', jobStateUpdatedHandler)
})

onUnmounted(() => {
    if (hubConnection.value && jobStateUpdatedHandler) {
        hubConnection.value.off('JobStateUpdated', jobStateUpdatedHandler)
        jobStateUpdatedHandler = undefined
    }
})
</script>

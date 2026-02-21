<template>
    <div class="space-y-4">
        <!-- Configuration Header -->
        <div class="flex items-center justify-between">
            <h1 class="text-primary-content text-xl font-semibold">
                {{ translate('statistics.dashboard') }}
            </h1>
            <div class="flex items-center gap-2">
                <button
                    v-if="!isConfigMode"
                    @click="toggleConfigMode"
                    class="text-primary-content/60 hover:text-primary-content rounded-md px-3 py-1.5 text-sm transition-colors hover:bg-white/5">
                    {{ translate('statistics.configure') }}
                </button>
                <template v-else>
                    <button
                        @click="resetLayout"
                        class="text-primary-content/60 hover:text-primary-content rounded-md px-3 py-1.5 text-sm transition-colors hover:bg-white/5">
                        {{ translate('statistics.resetLayout') }}
                    </button>
                    <button
                        @click="toggleConfigMode"
                        class="bg-accent text-primary-content rounded-md px-3 py-1.5 text-sm font-medium transition-colors hover:opacity-90">
                        {{ translate('statistics.saveLayout') }}
                    </button>
                </template>
            </div>
        </div>

        <!-- Widget Grid -->
        <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <!-- Active Translations Widget -->
            <DashboardWidget
                v-if="isWidgetVisible('active-translations')"
                widget-id="active-translations"
                :title="translate('statistics.activeTranslations')"
                :is-config-mode="isConfigMode"
                :is-visible="isWidgetVisible('active-translations')"
                @toggle-visibility="toggleWidgetVisibility('active-translations')"
                @drag-start="handleDragStart"
                @drag-over="handleDragOver"
                @drop="handleDrop"
                class="lg:col-span-2">
                <template #header-extra>
                    <div class="flex items-center gap-2">
                        <span
                            :class="[
                                'h-2 w-2 rounded-full',
                                realtimeState.isConnected ? 'bg-green-500' : 'bg-red-500'
                            ]" />
                        <span class="text-primary-content/60 text-xs">
                            {{ realtimeState.isConnected ? translate('statistics.connected') : translate('statistics.disconnected') }}
                        </span>
                    </div>
                </template>
                <div v-if="activeTranslations.length === 0" class="flex h-24 items-center justify-center">
                    <p class="text-primary-content/60">{{ translate('statistics.noActiveTranslations') }}</p>
                </div>
                <div v-else class="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                    <div
                        v-for="translation in activeTranslations"
                        :key="translation.id"
                        class="bg-primary rounded-md p-3">
                        <div class="flex items-start justify-between">
                            <div class="min-w-0 flex-1">
                                <h4 class="text-primary-content truncate text-sm font-medium">
                                    {{ translate('statistics.jobId') }}: {{ translation.jobId.slice(0, 8) }}
                                </h4>
                                <p class="text-primary-content/60 text-xs">
                                    ID: {{ translation.id }}
                                </p>
                            </div>
                            <span
                                :class="[
                                    'rounded px-2 py-0.5 text-xs font-medium',
                                    translation.status === 'InProgress' ? 'bg-blue-500/20 text-blue-400' :
                                    translation.status === 'Pending' ? 'bg-yellow-500/20 text-yellow-400' :
                                    'bg-gray-500/20 text-gray-400'
                                ]">
                                {{ translation.status }}
                            </span>
                        </div>
                        <div class="mt-2">
                            <div class="flex items-center justify-between text-xs">
                                <span class="text-primary-content/60">{{ translate('statistics.progress') }}</span>
                                <span class="text-primary-content font-medium">{{ Math.round(translation.progress) }}%</span>
                            </div>
                            <div class="bg-primary-content/10 mt-1 h-1.5 w-full overflow-hidden rounded-full">
                                <div
                                    class="h-full rounded-full bg-accent transition-all duration-300"
                                    :style="{ width: `${translation.progress}%` }" />
                            </div>
                        </div>
                    </div>
                </div>
            </DashboardWidget>

            <!-- Media Overview Widget -->
            <DashboardWidget
                v-if="isWidgetVisible('media-overview')"
                widget-id="media-overview"
                :title="translate('statistics.mediaOverview')"
                :is-config-mode="isConfigMode"
                :is-visible="isWidgetVisible('media-overview')"
                @toggle-visibility="toggleWidgetVisibility('media-overview')"
                @drag-start="handleDragStart"
                @drag-over="handleDragOver"
                @drop="handleDrop"
                class="lg:col-span-2">
                <template v-if="loading">
                    <div class="flex h-64 items-center justify-center">
                        <LoaderCircleIcon class="h-8 w-8 animate-spin" />
                    </div>
                </template>

                <template v-else-if="error">
                    <div class="flex h-64 items-center justify-center text-red-500">
                        {{ error }}
                    </div>
                </template>

                <template v-else-if="!statistics">
                    <div class="text-primary-content flex h-64 items-center justify-center">
                        {{ translate('statistics.notAvailable') }}
                    </div>
                </template>

                <template v-else>
                    <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                        <StatCard
                            :title="translate('statistics.movies')"
                            :total="statistics.totalMovies"
                            :translated="getTranslationCount(MEDIA_TYPE.MOVIE)" />

                        <StatCard
                            :title="translate('statistics.tvShows')"
                            :total="statistics.totalEpisodes"
                            :translated="getTranslationCount(MEDIA_TYPE.EPISODE)" />
                    </div>
                </template>
            </DashboardWidget>

            <!-- Translation Activity Widget -->
            <DashboardWidget
                v-if="isWidgetVisible('translation-activity')"
                widget-id="translation-activity"
                :title="translate('statistics.translationActivity')"
                :is-config-mode="isConfigMode"
                :is-visible="isWidgetVisible('translation-activity')"
                @toggle-visibility="toggleWidgetVisibility('translation-activity')"
                @drag-start="handleDragStart"
                @drag-over="handleDragOver"
                @drop="handleDrop">
                <div class="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2">
                    <MetricCard
                        :title="translate('statistics.linesTranslated')"
                        :value="statistics?.totalLinesTranslated ?? 0" />

                    <MetricCard
                        :title="translate('statistics.filesProcessed')"
                        :value="statistics?.totalFilesTranslated ?? 0" />

                    <MetricCard
                        :title="translate('statistics.charactersTranslated')"
                        :value="statistics?.totalCharactersTranslated ?? 0"
                        class="xl:col-span-2" />
                </div>

                <div v-if="translationServices.length" class="mt-4">
                    <h3 class="text-primary-content mb-4 text-sm font-medium">
                        {{ translate('statistics.translationServices') }}
                    </h3>
                    <div class="grid grid-cols-2 gap-2 xl:grid-cols-3">
                        <div
                            v-for="[service, count] in translationServices"
                            :key="service"
                            class="bg-primary rounded-sm p-2">
                            <h4 class="text-primary-content/70 text-xs font-medium">
                                {{ formatServiceName(service) }}
                            </h4>
                            <p class="text-primary-content text-lg font-bold">
                                {{ formatNumber(count) }}
                            </p>
                        </div>
                    </div>
                </div>
            </DashboardWidget>

            <!-- Language Statistics Widget -->
            <DashboardWidget
                v-if="isWidgetVisible('language-statistics')"
                widget-id="language-statistics"
                :title="translate('statistics.languageStatistics')"
                :is-config-mode="isConfigMode"
                :is-visible="isWidgetVisible('language-statistics')"
                @toggle-visibility="toggleWidgetVisibility('language-statistics')"
                @drag-start="handleDragStart"
                @drag-over="handleDragOver"
                @drop="handleDrop">
                <div class="h-80">
                    <LanguageChart v-if="dailyStats?.length" :daily-stats="dailyStats" />
                    <div v-else class="flex h-full w-full items-center justify-center">
                        <LoaderCircleIcon class="h-8 w-8 animate-spin" />
                    </div>
                </div>

                <div v-if="subtitleLanguages.length" class="mt-4">
                    <h3 class="text-primary-content mb-2 text-sm font-medium">
                        {{ translate('statistics.availableSubtitles') }}
                    </h3>
                    <div class="grid grid-cols-3 gap-2 md:grid-cols-4 xl:grid-cols-6">
                        <div
                            v-for="[language, count] in subtitleLanguages"
                            :key="language"
                            class="bg-primary rounded-sm p-2">
                            <h4 class="text-primary-content/70 text-xs font-medium">
                                {{ language.toUpperCase() }}
                            </h4>
                            <p class="text-primary-content text-lg font-bold">
                                {{ formatNumber(count) }}
                            </p>
                        </div>
                    </div>
                </div>
            </DashboardWidget>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { DailyStatistic, MEDIA_TYPE, Statistics } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import LanguageChart from './LanguageChart.vue'
import StatCard from './StatCard.vue'
import MetricCard from './MetricCard.vue'
import DashboardWidget from './DashboardWidget.vue'
import { useDashboardSignalR } from '@/composables/useDashboardSignalR'
import { useDashboardLayout } from '@/composables/useDashboardLayout'

const { translate } = useI18n()
const { state: realtimeState, connect: connectSignalR, disconnect: disconnectSignalR, getActiveTranslations } = useDashboardSignalR()
const { 
    isConfigMode, 
    toggleConfigMode, 
    isWidgetVisible, 
    toggleWidgetVisibility, 
    resetLayout,
    handleDragStart,
    handleDragOver,
    handleDrop
} = useDashboardLayout()

const loading = ref(true)
const error = ref<string | null>(null)
const statistics = ref<Statistics>()
const dailyStats = ref<DailyStatistic[]>()

const translationServices = computed(() => {
    if (!statistics.value?.translationsByService) return []
    return Object.entries(statistics.value.translationsByService)
})

const subtitleLanguages = computed(() => {
    if (!statistics.value?.subtitlesByLanguage) return []
    return Object.entries(statistics.value.subtitlesByLanguage)
})

const activeTranslations = computed(() => getActiveTranslations())

const formatNumber = (num: number): string => {
    return num ? new Intl.NumberFormat().format(num) : '0'
}

const formatServiceName = (service: string): string => {
    return service.charAt(0).toUpperCase() + service.slice(1)
}

const getTranslationCount = (type: string): number => {
    return statistics.value?.translationsByMediaType?.[type] || 0
}

const fetchStatistics = async () => {
    try {
        error.value = null
        loading.value = true
        statistics.value = await services.statistics.getStatistics()
    } catch (err: unknown) {
        if (err instanceof Error) {
            error.value = err?.message || translate('statistics.failedFetch')
        }
        console.error('Error fetching statistics:', err)
    } finally {
        loading.value = false
    }
}

const fetchDailyStats = async () => {
    loading.value = true
    try {
        dailyStats.value = await services.statistics.getDailyStatistics<DailyStatistic[]>()
    } catch (error) {
        console.error('Error fetching daily statistics:', error)
    } finally {
        loading.value = false
    }
}

onMounted(async () => {
    await connectSignalR()
    await fetchDailyStats()
    await fetchStatistics()
})

onUnmounted(() => {
    disconnectSignalR()
})
</script>

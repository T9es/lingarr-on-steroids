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

        <!-- Config Mode Instructions -->
        <div v-if="isConfigMode" class="bg-accent/10 border-accent/30 rounded-md border p-3">
            <p class="text-primary-content/80 text-sm">
                <span class="font-medium">
                    {{ translate('statistics.configMode') || 'Configuration Mode' }}:
                </span>
                {{
                    translate('statistics.configModeHint') ||
                    'Drag widgets to rearrange. Click the eye icon to show/hide widgets. Changes are saved automatically.'
                }}
            </p>
        </div>

        <!-- Widget Grid -->
        <GridLayout
            v-model:layout="currentLayout"
            :col-num="gridCols"
            :row-height="rowHeight"
            :margin="margin"
            :is-draggable="isConfigMode"
            :is-resizable="isConfigMode"
            :vertical-compact="true"
            :use-css-transforms="true"
            :responsive="true"
            :breakpoints="{ lg: 1200, md: 996, sm: 768, xs: 480, xxs: 0 }"
            :cols="{ lg: 12, md: 8, sm: 4, xs: 2, xxs: 1 }"
            class="min-h-[200px]">
            <GridItem
                v-for="item in visibleLayout"
                :key="item.i"
                :i="item.i"
                :x="item.x"
                :y="item.y"
                :w="item.w"
                :h="item.h"
                :min-w="item.minW"
                :min-h="item.minH"
                :max-w="item.maxW"
                :max-h="item.maxH"
                :is-draggable="isConfigMode"
                :is-resizable="isConfigMode"
                class="transition-shadow duration-200">
                <DashboardWidget
                    :widget-id="item.i"
                    :is-config-mode="isConfigMode"
                    :is-visible="isWidgetVisible(item.i)"
                    @toggle-visibility="toggleWidgetVisibility(item.i)">
                    <!-- Active Translations Widget -->
                    <template v-if="item.i === 'active-translations'">
                        <ActiveTranslationsContent
                            :is-connected="realtimeState.isConnected"
                            :translations="activeTranslations" />
                    </template>

                    <!-- Media Overview Widget -->
                    <template v-else-if="item.i === 'media-overview'">
                        <MediaOverviewContent
                            :loading="loading"
                            :error="error ?? undefined"
                            :statistics="statistics" />
                    </template>

                    <!-- Translation Activity Widget -->
                    <template v-else-if="item.i === 'translation-activity'">
                        <TranslationActivityContent
                            :statistics="statistics"
                            :translation-services="translationServices" />
                    </template>

                    <!-- Language Statistics Widget -->
                    <template v-else-if="item.i === 'language-statistics'">
                        <LanguageStatisticsContent
                            :daily-stats="dailyStats"
                            :subtitle-languages="subtitleLanguages" />
                    </template>

                    <!-- Translation History Widget -->
                    <template v-else-if="item.i === 'translation-history'">
                        <TranslationHistoryWidget :daily-statistics="dailyStats || []" />
                    </template>

                    <!-- Job Queue Widget -->
                    <template v-else-if="item.i === 'job-queue'">
                        <JobQueueWidget />
                    </template>

                    <!-- API Usage Widget -->
                    <template v-else-if="item.i === 'api-usage'">
                        <ApiUsageWidget />
                    </template>

                    <!-- Error Log Widget -->
                    <template v-else-if="item.i === 'error-log'">
                        <ErrorLogWidget />
                    </template>
                </DashboardWidget>
            </GridItem>
        </GridLayout>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, defineComponent, h } from 'vue'
import { GridLayout, GridItem } from 'grid-layout-plus'
import { DailyStatistic, MEDIA_TYPE, Statistics } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import LanguageChart from './LanguageChart.vue'
import StatCard from './StatCard.vue'
import MetricCard from './MetricCard.vue'
import DashboardWidget from './DashboardWidget.vue'
import TranslationHistoryWidget from './widgets/TranslationHistoryWidget.vue'
import JobQueueWidget from './widgets/JobQueueWidget.vue'
import ApiUsageWidget from './widgets/ApiUsageWidget.vue'
import ErrorLogWidget from './widgets/ErrorLogWidget.vue'
import { useDashboardSignalR } from '@/composables/useDashboardSignalR'
import { useDashboardLayout, type LayoutItem } from '@/composables/useDashboardLayout'

const { translate } = useI18n()
const {
    state: realtimeState,
    connect: connectSignalR,
    disconnect: disconnectSignalR,
    getActiveTranslations
} = useDashboardSignalR()
const {
    visibleLayout,
    isConfigMode,
    toggleConfigMode,
    isWidgetVisible,
    toggleWidgetVisibility,
    resetLayout,
    updateLayout,
    gridCols,
    rowHeight,
    margin
} = useDashboardLayout()

// Local reactive layout for grid-layout-plus
const currentLayout = ref<LayoutItem[]>([])

// Sync visibleLayout to currentLayout
watch(
    visibleLayout,
    (newLayout) => {
        const currentStr = JSON.stringify(currentLayout.value)
        const newStr = JSON.stringify(newLayout)
        if (currentStr !== newStr) {
            currentLayout.value = JSON.parse(newStr)
        }
    },
    { immediate: true, deep: true }
)

// Sync changes back to store
watch(
    currentLayout,
    (newLayout) => {
        const visibleStr = JSON.stringify(visibleLayout.value)
        const newStr = JSON.stringify(newLayout)
        if (visibleStr !== newStr) {
            updateLayout(JSON.parse(newStr))
        }
    },
    { deep: true }
)

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

// Inline components for widget content
const ActiveTranslationsContent = defineComponent({
    props: {
        isConnected: Boolean,
        translations: Array as () => Array<{
            id: number
            jobId: string
            status: string
            progress: number
        }>
    },
    setup(props) {
        return () => [
            h('div', { class: 'flex items-center gap-2 mb-4' }, [
                h('span', {
                    class: [
                        'h-2 w-2 rounded-full',
                        props.isConnected ? 'bg-green-500' : 'bg-red-500'
                    ]
                }),
                h(
                    'span',
                    { class: 'text-primary-content/60 text-xs' },
                    props.isConnected
                        ? translate('statistics.connected')
                        : translate('statistics.disconnected')
                )
            ]),
            props.translations?.length === 0
                ? h(
                      'div',
                      { class: 'flex h-24 items-center justify-center' },
                      h(
                          'p',
                          { class: 'text-primary-content/60' },
                          translate('statistics.noActiveTranslations')
                      )
                  )
                : h(
                      'div',
                      { class: 'grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3' },
                      props.translations?.map((t) =>
                          h('div', { key: t.id, class: 'bg-primary rounded-md p-3' }, [
                              h('div', { class: 'flex items-start justify-between' }, [
                                  h('div', { class: 'min-w-0 flex-1' }, [
                                      h(
                                          'h4',
                                          {
                                              class: 'text-primary-content truncate text-sm font-medium'
                                          },
                                          `${translate('statistics.jobId')}: ${t.jobId.slice(0, 8)}`
                                      ),
                                      h(
                                          'p',
                                          { class: 'text-primary-content/60 text-xs' },
                                          `ID: ${t.id}`
                                      )
                                  ]),
                                  h(
                                      'span',
                                      {
                                          class: [
                                              'rounded px-2 py-0.5 text-xs font-medium',
                                              t.status === 'InProgress'
                                                  ? 'bg-blue-500/20 text-blue-400'
                                                  : t.status === 'Pending'
                                                    ? 'bg-yellow-500/20 text-yellow-400'
                                                    : 'bg-gray-500/20 text-gray-400'
                                          ]
                                      },
                                      t.status
                                  )
                              ]),
                              h('div', { class: 'mt-2' }, [
                                  h('div', { class: 'flex items-center justify-between text-xs' }, [
                                      h(
                                          'span',
                                          { class: 'text-primary-content/60' },
                                          translate('statistics.progress')
                                      ),
                                      h(
                                          'span',
                                          { class: 'text-primary-content font-medium' },
                                          `${Math.round(t.progress)}%`
                                      )
                                  ]),
                                  h(
                                      'div',
                                      {
                                          class: 'bg-primary-content/10 mt-1 h-1.5 w-full overflow-hidden rounded-full'
                                      },
                                      h('div', {
                                          class: 'h-full rounded-full bg-accent transition-all duration-300',
                                          style: { width: `${t.progress}%` }
                                      })
                                  )
                              ])
                          ])
                      )
                  )
        ]
    }
})

const MediaOverviewContent = defineComponent({
    props: {
        loading: Boolean,
        error: { type: String, default: undefined },
        statistics: Object as () => Statistics
    },
    setup(props) {
        return () => {
            if (props.loading) {
                return h(
                    'div',
                    { class: 'flex h-64 items-center justify-center' },
                    h(LoaderCircleIcon, { class: 'h-8 w-8 animate-spin' })
                )
            }
            if (props.error) {
                return h(
                    'div',
                    { class: 'flex h-64 items-center justify-center text-red-500' },
                    props.error
                )
            }
            if (!props.statistics) {
                return h(
                    'div',
                    { class: 'text-primary-content flex h-64 items-center justify-center' },
                    translate('statistics.notAvailable')
                )
            }
            return h('div', { class: 'grid grid-cols-1 gap-4 md:grid-cols-2' }, [
                h(StatCard, {
                    title: translate('statistics.movies'),
                    total: props.statistics!.totalMovies,
                    translated: props.statistics!.translationsByMediaType?.[MEDIA_TYPE.MOVIE] || 0
                }),
                h(StatCard, {
                    title: translate('statistics.tvShows'),
                    total: props.statistics!.totalEpisodes,
                    translated: props.statistics!.translationsByMediaType?.[MEDIA_TYPE.EPISODE] || 0
                })
            ])
        }
    }
})

const TranslationActivityContent = defineComponent({
    props: {
        statistics: Object as () => Statistics,
        translationServices: Array as () => [string, number][]
    },
    setup(props) {
        return () => [
            h(
                'div',
                { class: 'grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-1 xl:grid-cols-2' },
                [
                    h(MetricCard, {
                        title: translate('statistics.linesTranslated'),
                        value: props.statistics?.totalLinesTranslated ?? 0
                    }),
                    h(MetricCard, {
                        title: translate('statistics.filesProcessed'),
                        value: props.statistics?.totalFilesTranslated ?? 0
                    }),
                    h(MetricCard, {
                        title: translate('statistics.charactersTranslated'),
                        value: props.statistics?.totalCharactersTranslated ?? 0,
                        class: 'xl:col-span-2'
                    })
                ]
            ),
            props.translationServices?.length
                ? h('div', { class: 'mt-4' }, [
                      h(
                          'h3',
                          { class: 'text-primary-content mb-4 text-sm font-medium' },
                          translate('statistics.translationServices')
                      ),
                      h(
                          'div',
                          { class: 'grid grid-cols-2 gap-2 xl:grid-cols-3' },
                          props.translationServices.map(([service, count]) =>
                              h('div', { key: service, class: 'bg-primary rounded-sm p-2' }, [
                                  h(
                                      'h4',
                                      { class: 'text-primary-content/70 text-xs font-medium' },
                                      service.charAt(0).toUpperCase() + service.slice(1)
                                  ),
                                  h(
                                      'p',
                                      { class: 'text-primary-content text-lg font-bold' },
                                      count ? new Intl.NumberFormat().format(count) : '0'
                                  )
                              ])
                          )
                      )
                  ])
                : null
        ]
    }
})

const LanguageStatisticsContent = defineComponent({
    props: {
        dailyStats: Array as () => DailyStatistic[],
        subtitleLanguages: Array as () => [string, number][]
    },
    setup(props) {
        return () => [
            h(
                'div',
                { class: 'h-80' },
                props.dailyStats?.length
                    ? h(LanguageChart, { dailyStats: props.dailyStats })
                    : h(
                          'div',
                          { class: 'flex h-full w-full items-center justify-center' },
                          h(LoaderCircleIcon, { class: 'h-8 w-8 animate-spin' })
                      )
            ),
            props.subtitleLanguages?.length
                ? h('div', { class: 'mt-4' }, [
                      h(
                          'h3',
                          { class: 'text-primary-content mb-2 text-sm font-medium' },
                          translate('statistics.availableSubtitles')
                      ),
                      h(
                          'div',
                          { class: 'grid grid-cols-3 gap-2 md:grid-cols-4 xl:grid-cols-6' },
                          props.subtitleLanguages.map(([language, count]) =>
                              h('div', { key: language, class: 'bg-primary rounded-sm p-2' }, [
                                  h(
                                      'h4',
                                      { class: 'text-primary-content/70 text-xs font-medium' },
                                      language.toUpperCase()
                                  ),
                                  h(
                                      'p',
                                      { class: 'text-primary-content text-lg font-bold' },
                                      count ? new Intl.NumberFormat().format(count) : '0'
                                  )
                              ])
                          )
                      )
                  ])
                : null
        ]
    }
})
</script>

<style>
/* Grid layout styles */
.vue-grid-layout {
    min-height: 200px;
}

.vue-grid-item {
    touch-action: none;
    transition: box-shadow 0.2s ease;
}

.vue-grid-item.vue-grid-placeholder {
    background: rgba(100, 100, 100, 0.2);
    border: 2px dashed rgba(100, 100, 100, 0.5);
    border-radius: 0.375rem;
    opacity: 0.8;
    transition: all 0.2s ease;
}

.vue-grid-item.vue-draggable-dragging {
    opacity: 0.8;
    z-index: 3;
    box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
}

.vue-grid-item.resizing {
    opacity: 0.9;
    z-index: 3;
}

.vue-grid-item .vue-resizable-handle {
    width: 20px;
    height: 20px;
    bottom: 0;
    right: 0;
    background: none;
    cursor: se-resize;
}

.vue-grid-item .vue-resizable-handle::after {
    content: '';
    position: absolute;
    right: 4px;
    bottom: 4px;
    width: 8px;
    height: 8px;
    border-right: 2px solid rgba(150, 150, 150, 0.5);
    border-bottom: 2px solid rgba(150, 150, 150, 0.5);
}

/* Responsive adjustments */
@media (max-width: 768px) {
    .vue-grid-item {
        touch-action: auto;
    }
}
</style>

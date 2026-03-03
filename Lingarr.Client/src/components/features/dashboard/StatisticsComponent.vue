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
                        @click="showResetConfirmation = true"
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

        <!-- Loading State -->
        <div v-if="isLayoutLoading" class="flex min-h-[400px] items-center justify-center">
            <LoaderCircleIcon class="text-accent h-8 w-8 animate-spin" />
        </div>

        <!-- Widget Grid -->
        <div v-else ref="gridContainerRef" class="w-full">
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
                    <ActiveTranslationsContent
                        v-if="item.i === 'active-translations'"
                        :is-connected="realtimeState.isConnected"
                        :translations="activeTranslations" />

                    <!-- Media Overview Widget -->
                    <MediaOverviewContent
                        v-if="item.i === 'media-overview'"
                        :loading="loading"
                        :error="error ?? undefined"
                        :statistics="statistics" />

                    <!-- Translation History Widget -->
                    <TranslationHistoryWidget
                        v-if="item.i === 'translation-history'"
                        :daily-statistics="dailyStats || []"
                        :statistics="statistics"
                        :is-loading="loading" />

                    <!-- Job Queue Widget -->
                    <JobQueueWidget v-if="item.i === 'job-queue'" />

                    <!-- API Usage Widget -->
                    <ApiUsageWidget v-if="item.i === 'api-usage'" />

                    <!-- Error Log Widget -->
                    <ErrorLogWidget v-if="item.i === 'error-log'" />
                </DashboardWidget>
            </GridItem>
        </GridLayout>
    </div>

    <!-- Reset Confirmation Modal -->
        <div
            v-if="showResetConfirmation"
            class="bg-secondary/80 fixed inset-0 z-50 flex items-center justify-center backdrop-blur-sm">
            <div class="bg-secondary border-secondary mx-4 max-w-sm rounded-lg border p-6">
                <h3 class="text-primary-content mb-2 text-lg font-semibold">
                    {{
                        translate('statistics.resetLayoutConfirmTitle') || 'Reset Dashboard Layout'
                    }}
                </h3>
                <p class="text-primary-content/70 mb-4 text-sm">
                    {{
                        translate('statistics.resetLayoutConfirmMessage') ||
                        'Are you sure you want to reset the dashboard layout to defaults? This cannot be undone.'
                    }}
                </p>
                <div class="flex justify-end gap-2">
                    <button
                        @click="showResetConfirmation = false"
                        class="text-primary-content/60 hover:text-primary-content rounded-md px-4 py-2 text-sm transition-colors hover:bg-white/5">
                        {{ translate('common.cancel') || 'Cancel' }}
                    </button>
                    <button
                        @click="confirmReset"
                        class="rounded-md bg-red-500/80 px-4 py-2 text-sm font-medium text-primary-content transition-colors hover:bg-red-500">
                        {{ translate('statistics.resetLayout') || 'Reset' }}
                    </button>
                </div>
            </div>
        </div>

        <!-- Hidden Widgets Drawer (only visible in config mode) -->
        <div
            v-if="isConfigMode && hiddenWidgets.length > 0"
            class="border-secondary/50 bg-secondary/30 mt-4 rounded-lg border border-dashed p-4">
            <h4 class="text-primary-content/70 mb-3 text-sm font-medium">
                {{ translate('statistics.hiddenWidgets') || 'Hidden Widgets' }} ({{
                    hiddenWidgets.length
                }})
            </h4>
            <div class="flex flex-wrap gap-3">
                <button
                    v-for="widget in hiddenWidgets"
                    :key="widget.id"
                    @click="showWidget(widget.id)"
                    class="bg-primary hover:bg-accent/20 rounded-md p-3 text-left transition-colors">
                    <div class="text-primary-content text-sm font-medium">
                        {{ translate(widget.title) || widget.title }}
                    </div>
                    <div class="text-primary-content/60 mt-1 text-xs">
                        {{ translate('statistics.clickToShow') || 'Click to show' }}
                    </div>
                </button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, watch, defineComponent, h } from 'vue'
import { GridLayout, GridItem } from 'grid-layout-plus'
import { DailyStatistic, MEDIA_TYPE, Statistics } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
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
    getActiveTranslations,
    loadInitialTranslations
} = useDashboardSignalR()
const {
    visibleLayout,
    hiddenWidgets,
    isConfigMode,
    isLoading: isLayoutLoading,
    toggleConfigMode,
    isWidgetVisible,
    toggleWidgetVisibility,
    showWidget,
    resetLayout,
    updateLayout,
    gridCols,
    rowHeight,
    margin
} = useDashboardLayout()

const gridContainerRef = ref<HTMLElement | null>(null)
const containerWidth = ref(1200)
let resizeObserver: ResizeObserver | null = null

const showResetConfirmation = ref(false)

const confirmReset = async () => {
    showResetConfirmation.value = false
    await resetLayout()
}

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
    await loadInitialTranslations()
    await fetchDailyStats()
    await fetchStatistics()
    
    if (gridContainerRef.value) {
        resizeObserver = new ResizeObserver((entries) => {
            for (const entry of entries) {
                containerWidth.value = entry.contentRect.width
            }
        })
        resizeObserver.observe(gridContainerRef.value)
    }
})

onUnmounted(() => {
    disconnectSignalR()
    if (resizeObserver) {
        resizeObserver.disconnect()
        resizeObserver = null
    }
})

const ActiveTranslationsContent = defineComponent({
    props: {
        isConnected: Boolean,
        translations: Array as () => Array<{
            id: number
            jobId: string
            title: string
            mediaType: string
            sourceLanguage: string
            targetLanguage: string
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
                                          t.title || `Translation #${t.id}`
                                      ),
                                      h(
                                          'p',
                                          { class: 'text-primary-content/60 text-xs' },
                                          t.sourceLanguage && t.targetLanguage
                                              ? `${t.sourceLanguage} → ${t.targetLanguage}`
                                              : t.mediaType || 'Movie'
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
                                                    : 'bg-secondary/50 text-primary-content/60'
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
        const formatNumber = (num: number): string => {
            return new Intl.NumberFormat().format(num)
        }

        const getPercentage = (translated: number, total: number): number => {
            if (total === 0) return 0
            return Math.round((translated / total) * 100)
        }

        return () => {
            if (props.loading) {
                return h(
                    'div',
                    { class: 'flex h-20 items-center justify-center' },
                    h(LoaderCircleIcon, { class: 'h-6 w-6 animate-spin' })
                )
            }
            if (props.error) {
                return h(
                    'div',
                    { class: 'flex h-20 items-center justify-center text-red-500 text-sm' },
                    props.error
                )
            }
            if (!props.statistics) {
                return h(
                    'div',
                    {
                        class: 'text-primary-content/60 flex h-20 items-center justify-center text-sm'
                    },
                    translate('statistics.notAvailable')
                )
            }

            const moviesTranslated =
                props.statistics!.translationsByMediaType?.[MEDIA_TYPE.MOVIE] || 0
            const episodesTranslated =
                props.statistics!.translationsByMediaType?.[MEDIA_TYPE.EPISODE] || 0
            const moviesPercentage = getPercentage(moviesTranslated, props.statistics!.totalMovies)
            const episodesPercentage = getPercentage(
                episodesTranslated,
                props.statistics!.totalEpisodes
            )

            return h('div', { class: 'bg-primary rounded-lg p-4 shadow-sm' }, [
                h('div', { class: 'flex gap-6' }, [
                    h('div', { class: 'flex-1 bg-secondary/30 rounded-md p-3' }, [
                        h('div', { class: 'text-primary-content/60 text-xs font-medium' }, [
                            translate('statistics.movies')
                        ]),
                        h('div', { class: 'flex items-baseline gap-2 mt-1' }, [
                            h(
                                'span',
                                { class: 'text-primary-content text-xl font-bold' },
                                formatNumber(props.statistics!.totalMovies)
                            ),
                            h(
                                'span',
                                { class: 'text-accent text-sm' },
                                `${formatNumber(moviesTranslated)} translated`
                            )
                        ]),
                        h(
                            'div',
                            {
                                class: 'bg-primary-content/10 mt-2 h-1.5 w-full overflow-hidden rounded-full'
                            },
                            [
                                h('div', {
                                    class: 'bg-accent h-full rounded-full transition-all duration-500',
                                    style: { width: `${moviesPercentage}%` }
                                })
                            ]
                        )
                    ]),
                    h('div', { class: 'w-px bg-secondary/50' }),
                    h('div', { class: 'flex-1 bg-secondary/30 rounded-md p-3' }, [
                        h('div', { class: 'text-primary-content/60 text-xs font-medium' }, [
                            translate('statistics.tvShows')
                        ]),
                        h('div', { class: 'flex items-baseline gap-2 mt-1' }, [
                            h(
                                'span',
                                { class: 'text-primary-content text-xl font-bold' },
                                formatNumber(props.statistics!.totalEpisodes)
                            ),
                            h(
                                'span',
                                { class: 'text-accent text-sm' },
                                `${formatNumber(episodesTranslated)} translated`
                            )
                        ]),
                        h(
                            'div',
                            {
                                class: 'bg-primary-content/10 mt-2 h-1.5 w-full overflow-hidden rounded-full'
                            },
                            [
                                h('div', {
                                    class: 'bg-accent h-full rounded-full transition-all duration-500',
                                    style: { width: `${episodesPercentage}%` }
                                })
                            ]
                        )
                    ])
                ])
            ])
        }
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
    width: 24px;
    height: 24px;
    bottom: 0;
    right: 0;
    background: rgba(var(--accent-rgb, 147, 51, 234), 0.1);
    border-radius: 6px 0 6px 0;
    cursor: se-resize;
    transition: background 0.2s ease;
}

.vue-grid-item .vue-resizable-handle:hover {
    background: rgba(var(--accent-rgb, 147, 51, 234), 0.25);
}

.vue-grid-item .vue-resizable-handle::after {
    content: '';
    position: absolute;
    right: 6px;
    bottom: 6px;
    width: 10px;
    height: 10px;
    border-right: 2px solid var(--accent, #9333ea);
    border-bottom: 2px solid var(--accent, #9333ea);
    border-radius: 0 0 2px 0;
}

/* Responsive adjustments */
@media (max-width: 768px) {
    .vue-grid-item {
        touch-action: auto;
    }
}
</style>

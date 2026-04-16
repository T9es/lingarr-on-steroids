<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from '@/plugins/i18n'
import { useInstanceStore } from '@/store/instance'
import services from '@/services'
import TrendUpIcon from '@/components/icons/TrendUpIcon.vue'
import TrendDownIcon from '@/components/icons/TrendDownIcon.vue'
import TrendFlatIcon from '@/components/icons/TrendFlatIcon.vue'
import type { DailyStatistic, FilteredStatistics } from '@/ts/statistics'
import {
    Chart as ChartJS,
    CategoryScale,
    LinearScale,
    BarElement,
    Title,
    Tooltip
} from 'chart.js/auto'
import type { TooltipItem } from 'chart.js'
import { Bar } from 'vue-chartjs'
import { VueDatePicker } from '@vuepic/vue-datepicker'
import '@vuepic/vue-datepicker/dist/main.css'

ChartJS.register(CategoryScale, LinearScale, BarElement, Title, Tooltip)

const i18n = useI18n()
const instanceStore = useInstanceStore()

const props = defineProps<{
    dailyStatistics: DailyStatistic[]
    statistics?: {
        totalLinesTranslated: number
        totalFilesTranslated: number
        totalCharactersTranslated: number
    }
    isLoading?: boolean
}>()

interface RecentTranslation {
    id: number
    title: string
    sourceLanguage: string
    targetLanguage: string
    completedAt: string | null
    mediaType: string
}

interface RecentTranslationRaw {
    id?: number
    Id?: number
    title?: string
    Title?: string
    sourceLanguage?: string
    SourceLanguage?: string
    targetLanguage?: string
    TargetLanguage?: string
    completedAt?: string | null
    CompletedAt?: string | null
    mediaType?: string
    MediaType?: string
}

interface RecentTranslationsResponseRaw {
    items?: RecentTranslationRaw[]
    Items?: RecentTranslationRaw[]
    totalCount?: number
    TotalCount?: number
    hasMore?: boolean
    HasMore?: boolean
}

interface HourlyStatistic {
    hour: number
    translationCount: number
}

type TimeFilter = '24h' | '7d' | '30d' | '1y' | 'custom' | 'all'
type ChartLabelContext = TooltipItem<'bar'>

const recentTranslations = ref<RecentTranslation[]>([])
const recentLoading = ref(false)
const recentLoadingMore = ref(false)
const recentHasMore = ref(true)
const recentPageSize = 10
const recentScrollContainer = ref<HTMLElement | null>(null)
const recentSentinel = ref<HTMLElement | null>(null)
let recentObserver: IntersectionObserver | null = null
const chartKey = ref(0)
const selectedFilter = ref<TimeFilter>('30d')
const customDateRange = ref<Date[]>([])
const hourlyStatistics = ref<HourlyStatistic[]>([])
const hourlyLoading = ref(false)
const filteredStats = ref<FilteredStatistics | null>(null)
const filteredLoading = ref(false)

watch(
    () => instanceStore.getTheme,
    () => {
        chartKey.value++
    }
)

watch(selectedFilter, async (newFilter) => {
    if (newFilter === '24h') {
        await fetchHourlyStatistics()
    }
    await fetchFilteredStatistics()
    chartKey.value++
})

watch(customDateRange, async () => {
    if (selectedFilter.value === 'custom' && customDateRange.value?.length === 2) {
        await fetchFilteredStatistics()
        chartKey.value++
    }
})

watch(
    () => [props.isLoading, hourlyLoading.value, filteredLoading.value],
    async ([isPageLoading, isHourlyLoading, isFilteredLoading]) => {
        if (!isPageLoading && !isHourlyLoading && !isFilteredLoading) {
            await nextTick()
            setupRecentObserver()
        }
    }
)

onMounted(async () => {
    await fetchRecentTranslations(true)
    await fetchFilteredStatistics()
    await nextTick()
    setupRecentObserver()
})

onUnmounted(() => {
    if (recentObserver) {
        recentObserver.disconnect()
    }
})

const fetchRecentTranslations = async (reset = false) => {
    if (reset) {
        recentLoading.value = true
        recentTranslations.value = []
        recentHasMore.value = true
    } else if (recentLoading.value || recentLoadingMore.value || !recentHasMore.value) {
        return
    } else {
        recentLoadingMore.value = true
    }

    try {
        const offset = reset ? 0 : recentTranslations.value.length
        const response =
            await services.translationRequest.getRecentCompleted<RecentTranslationsResponseRaw>(
                offset,
                recentPageSize
            )
        const rawItems = response?.items || response?.Items || []
        const mappedItems = rawItems.map((item) => ({
            id: item.id || item.Id || 0,
            title: item.title || item.Title || 'Unknown',
            sourceLanguage: item.sourceLanguage || item.SourceLanguage || '',
            targetLanguage: item.targetLanguage || item.TargetLanguage || '',
            completedAt: item.completedAt || item.CompletedAt || null,
            mediaType: item.mediaType || item.MediaType || 'Movie'
        }))

        recentTranslations.value = reset
            ? mappedItems
            : [...recentTranslations.value, ...mappedItems]

        recentHasMore.value =
            response?.hasMore ?? response?.HasMore ?? mappedItems.length === recentPageSize
    } catch (error) {
        console.warn('Failed to fetch recent translations:', error)
    } finally {
        recentLoading.value = false
        recentLoadingMore.value = false
    }
}

const loadMoreRecentTranslations = async () => {
    if (recentLoading.value || recentLoadingMore.value || !recentHasMore.value) {
        return
    }

    await fetchRecentTranslations(false)
}

const setupRecentObserver = () => {
    if (recentObserver) {
        recentObserver.disconnect()
    }

    recentObserver = new IntersectionObserver(
        (entries) => {
            if (
                entries[0].isIntersecting &&
                recentHasMore.value &&
                !recentLoading.value &&
                !recentLoadingMore.value
            ) {
                loadMoreRecentTranslations()
            }
        },
        { root: recentScrollContainer.value, threshold: 0.1 }
    )

    if (recentSentinel.value) {
        recentObserver.observe(recentSentinel.value)
    }
}

const fetchHourlyStatistics = async () => {
    hourlyLoading.value = true
    try {
        const response = await fetch('/api/statistics/hourly')
        if (response.ok) {
            hourlyStatistics.value = await response.json()
        }
    } catch (error) {
        console.warn('Failed to fetch hourly statistics:', error)
    } finally {
        hourlyLoading.value = false
    }
}

const getDateRange = (): { startDate?: Date; endDate?: Date } => {
    const now = new Date()

    switch (selectedFilter.value) {
        case '24h': {
            const start = new Date(now)
            start.setHours(0, 0, 0, 0)
            return { startDate: start, endDate: now }
        }
        case '7d': {
            const start = new Date(now)
            start.setDate(start.getDate() - 7)
            return { startDate: start, endDate: now }
        }
        case '30d': {
            const start = new Date(now)
            start.setDate(start.getDate() - 30)
            return { startDate: start, endDate: now }
        }
        case '1y': {
            const start = new Date(now)
            start.setFullYear(start.getFullYear() - 1)
            return { startDate: start, endDate: now }
        }
        case 'custom':
            if (customDateRange.value?.length === 2) {
                return { startDate: customDateRange.value[0], endDate: customDateRange.value[1] }
            }
            return {}
        case 'all':
        default:
            return {}
    }
}

const fetchFilteredStatistics = async () => {
    const { startDate, endDate } = getDateRange()

    if (selectedFilter.value === 'all') {
        filteredStats.value = null
        return
    }

    filteredLoading.value = true
    try {
        const stats = await services.statistics.getFilteredStatistics<FilteredStatistics>(
            startDate,
            endDate
        )
        filteredStats.value = stats
    } catch (error) {
        console.warn('Failed to fetch filtered statistics:', error)
    } finally {
        filteredLoading.value = false
    }
}

const filteredCount = computed(() => {
    const daily = props.dailyStatistics || []

    switch (selectedFilter.value) {
        case '24h':
            return hourlyStatistics.value.reduce((acc, h) => acc + h.translationCount, 0)
        case '7d': {
            const weekAgo = new Date()
            weekAgo.setDate(weekAgo.getDate() - 7)
            return daily
                .filter((s) => new Date(s.date) >= weekAgo)
                .reduce((acc, s) => acc + (s.translationCount || 0), 0)
        }
        case '30d': {
            const monthAgo = new Date()
            monthAgo.setDate(monthAgo.getDate() - 30)
            return daily
                .filter((s) => new Date(s.date) >= monthAgo)
                .reduce((acc, s) => acc + (s.translationCount || 0), 0)
        }
        case '1y': {
            const yearAgo = new Date()
            yearAgo.setFullYear(yearAgo.getFullYear() - 1)
            return daily
                .filter((s) => new Date(s.date) >= yearAgo)
                .reduce((acc, s) => acc + (s.translationCount || 0), 0)
        }
        case 'custom':
            if (customDateRange.value?.length === 2) {
                const [start, end] = customDateRange.value
                return daily
                    .filter((s) => {
                        const d = new Date(s.date)
                        return d >= start && d <= end
                    })
                    .reduce((acc, s) => acc + (s.translationCount || 0), 0)
            }
            return 0
        case 'all':
        default:
            return daily.reduce((acc, s) => acc + (s.translationCount || 0), 0)
    }
})

const trend = computed(() => {
    const daily = props.dailyStatistics || []
    const now = new Date()
    let periodDays = 7

    switch (selectedFilter.value) {
        case '24h':
            periodDays = 1
            break
        case '7d':
            periodDays = 7
            break
        case '30d':
            periodDays = 30
            break
        case '1y':
            periodDays = 365
            break
        case 'custom':
            if (customDateRange.value?.length === 2) {
                const [start, end] = customDateRange.value
                periodDays = Math.ceil((end.getTime() - start.getTime()) / 86400000) || 1
            }
            break
        case 'all':
            return { direction: 'flat' as const, percentage: 0 }
    }

    const currentStart = new Date(now)
    currentStart.setDate(currentStart.getDate() - periodDays)
    const prevStart = new Date(currentStart)
    prevStart.setDate(prevStart.getDate() - periodDays)

    const currentPeriod = daily
        .filter((s) => {
            const d = new Date(s.date)
            return d >= currentStart && d <= now
        })
        .reduce((acc, s) => acc + (s.translationCount || 0), 0)

    const prevPeriod = daily
        .filter((s) => {
            const d = new Date(s.date)
            return d >= prevStart && d < currentStart
        })
        .reduce((acc, s) => acc + (s.translationCount || 0), 0)

    if (prevPeriod === 0) return { direction: 'flat' as const, percentage: 0 }
    const percentage = Math.round(((currentPeriod - prevPeriod) / prevPeriod) * 100)
    return {
        direction:
            percentage > 5
                ? ('up' as const)
                : percentage < -5
                  ? ('down' as const)
                  : ('flat' as const),
        percentage: Math.abs(percentage)
    }
})

const isDarkTheme = computed(() => {
    const darkThemes = [
        'solarized-dark',
        'dracula',
        'nord',
        'monokai',
        'material-dark',
        'gotham',
        'gruvbox',
        'cyberpunk-neon',
        'horizon',
        'lingarr'
    ]
    return darkThemes.includes(instanceStore.getTheme)
})

const getCssVariable = (variableName: string): string => {
    return getComputedStyle(document.documentElement).getPropertyValue(variableName).trim()
}

const chartData = computed(() => {
    const daily = props.dailyStatistics || []

    if (selectedFilter.value === '24h') {
        if (!hourlyStatistics.value.length) return null
        return {
            labels: hourlyStatistics.value.map((h) => `${h.hour.toString().padStart(2, '0')}:00`),
            datasets: [
                {
                    label: 'Translations',
                    data: hourlyStatistics.value.map((h) => h.translationCount),
                    backgroundColor: getCssVariable('--accent') + '80',
                    borderColor: getCssVariable('--accent'),
                    borderRadius: 4,
                    barThickness: 8
                }
            ]
        }
    }

    let data = daily
    let maxItems = 14

    switch (selectedFilter.value) {
        case '7d':
            maxItems = 7
            break
        case '30d':
            maxItems = 30
            break
        case '1y':
            maxItems = 52
            break
        case 'custom':
            if (customDateRange.value?.length === 2) {
                const [start, end] = customDateRange.value
                data = daily.filter((s) => {
                    const d = new Date(s.date)
                    return d >= start && d <= end
                })
            }
            maxItems = data.length
            break
        case 'all':
            maxItems = daily.length
            break
    }

    const sliced = data.slice(-maxItems)
    if (!sliced.length) return null

    return {
        labels: sliced.map((s) =>
            new Date(s.date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
        ),
        datasets: [
            {
                label: 'Translations',
                data: sliced.map((s) => s.translationCount),
                backgroundColor: getCssVariable('--accent') + '80',
                borderColor: getCssVariable('--accent'),
                borderRadius: 4,
                barThickness: 8
            }
        ]
    }
})

const chartOptions = computed(() => ({
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
        legend: { display: false },
        tooltip: {
            backgroundColor: getCssVariable('--secondary'),
            titleColor: '#c0c8d2',
            bodyColor: '#c0c8d2',
            padding: 8,
            displayColors: false,
            callbacks: {
                label: (context: ChartLabelContext) => `${context.parsed.y ?? 0} translations`
            }
        }
    },
    scales: {
        x: {
            display: true,
            grid: { display: false },
            ticks: {
                color: '#c0c8d2',
                font: { size: 10 },
                maxRotation: 45,
                maxTicksLimit: selectedFilter.value === '24h' ? 12 : 7
            },
            border: { display: false }
        },
        y: {
            display: true,
            beginAtZero: true,
            grid: { color: '#466e8c20' },
            ticks: {
                color: '#c0c8d2',
                font: { size: 10 },
                maxTicksLimit: 4
            },
            border: { display: false }
        }
    }
}))

const formatNumber = (num: number): string => {
    if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`
    if (num >= 1000) return `${(num / 1000).toFixed(1)}K`
    return num.toString()
}

const formatRelativeTime = (dateStr: string | null): string => {
    if (!dateStr) return ''
    const diff = Date.now() - new Date(dateStr).getTime()
    const minutes = Math.floor(diff / 60000)
    const hours = Math.floor(diff / 3600000)
    const days = Math.floor(diff / 86400000)

    if (minutes < 60) return `${minutes}m ago`
    if (hours < 24) return `${hours}h ago`
    if (days < 7) return `${days}d ago`
    return new Date(dateStr).toLocaleDateString()
}

const timeFilterOptions = [
    { value: '24h', label: '24h' },
    { value: '7d', label: '7 days' },
    { value: '30d', label: '30 days' },
    { value: '1y', label: '1 year' },
    { value: 'custom', label: 'Custom' },
    { value: 'all', label: 'All time' }
]
</script>

<template>
    <div class="flex h-full flex-col">
        <div class="mb-3 flex items-center justify-between">
            <h3 class="text-primary-content/70 text-sm font-medium">
                {{ i18n.translate('statistics.translationHistory') }}
            </h3>
            <div class="flex items-center gap-2">
                <select
                    v-model="selectedFilter"
                    class="bg-secondary text-primary-content focus:ring-accent rounded-md border-0 px-2 py-1 text-xs outline-none focus:ring-1">
                    <option v-for="opt in timeFilterOptions" :key="opt.value" :value="opt.value">
                        {{ opt.label }}
                    </option>
                </select>
                <button
                    v-if="selectedFilter !== 'all'"
                    @click="selectedFilter = 'all'"
                    class="text-primary-content/60 hover:text-accent rounded px-2 py-1 text-xs transition-colors">
                    All
                </button>
            </div>
        </div>

        <div v-if="selectedFilter === 'custom'" class="mb-3">
            <VueDatePicker
                v-model="customDateRange"
                range
                :dark="isDarkTheme"
                :enable-time-picker="false"
                placeholder="Select date range"
                class="!bg-secondary" />
        </div>

        <div
            v-if="isLoading || hourlyLoading || filteredLoading"
            class="flex flex-1 items-center justify-center">
            <div
                class="border-accent h-6 w-6 animate-spin rounded-full border-2 border-t-transparent"></div>
        </div>

        <div v-else class="flex flex-1 flex-col gap-3">
            <div class="grid grid-cols-4 gap-2">
                <div class="bg-primary/50 rounded-md p-2 text-center">
                    <div class="text-primary-content/50 text-xs">Translations</div>
                    <div class="text-primary-content text-lg font-bold">
                        {{ formatNumber(filteredCount) }}
                    </div>
                </div>
                <div class="bg-primary/50 rounded-md p-2 text-center">
                    <div class="text-primary-content/50 text-xs">Lines</div>
                    <div class="text-primary-content text-lg font-bold">
                        {{
                            formatNumber(
                                filteredStats?.linesCount ??
                                    props.statistics?.totalLinesTranslated ??
                                    0
                            )
                        }}
                    </div>
                </div>
                <div class="bg-primary/50 rounded-md p-2 text-center">
                    <div class="text-primary-content/50 text-xs">Files</div>
                    <div class="text-primary-content text-lg font-bold">
                        {{
                            formatNumber(
                                filteredStats?.filesCount ??
                                    props.statistics?.totalFilesTranslated ??
                                    0
                            )
                        }}
                    </div>
                </div>
                <div class="bg-primary/50 rounded-md p-2 text-center">
                    <div class="text-primary-content/50 text-xs">Trend</div>
                    <div
                        class="flex items-center justify-center gap-1 text-sm font-medium"
                        :class="
                            trend.direction === 'up'
                                ? 'text-green-500'
                                : trend.direction === 'down'
                                  ? 'text-red-500'
                                  : 'text-primary-content/50'
                        ">
                        <TrendUpIcon v-if="trend.direction === 'up'" class="h-3 w-3 shrink-0" />
                        <TrendDownIcon v-if="trend.direction === 'down'" class="h-3 w-3 shrink-0" />
                        <TrendFlatIcon v-if="trend.direction === 'flat'" class="h-3 w-3 shrink-0" />
                        <span v-if="trend.direction !== 'flat'">{{ trend.percentage }}%</span>
                    </div>
                </div>
            </div>

            <div class="h-28 min-h-[100px]">
                <Bar
                    v-if="chartData"
                    :key="chartKey"
                    :data="chartData"
                    :options="chartOptions"
                    class="h-full w-full" />
                <div
                    v-else
                    class="text-primary-content/50 flex h-full items-center justify-center text-xs">
                    {{ i18n.translate('statistics.noDataAvailable') }}
                </div>
            </div>

            <div class="min-h-0 flex-1 overflow-hidden">
                <h4 class="text-primary-content/50 mb-1 text-xs font-medium">
                    {{ i18n.translate('statistics.recentTranslations') || 'Recent' }}
                </h4>
                <div
                    ref="recentScrollContainer"
                    class="scrollbar-thin h-full space-y-1.5 overflow-y-auto pr-1">
                    <div
                        v-if="recentLoading"
                        class="text-primary-content/50 py-2 text-center text-xs">
                        {{ i18n.translate('common.loading') }}
                    </div>
                    <div
                        v-else-if="recentTranslations.length === 0"
                        class="text-primary-content/50 py-2 text-center text-xs">
                        {{
                            i18n.translate('statistics.noRecentTranslations') ||
                            'No recent translations'
                        }}
                    </div>
                    <div
                        v-for="item in recentTranslations"
                        :key="item.id"
                        class="bg-primary/30 rounded-md p-2">
                        <div class="text-primary-content truncate text-xs font-medium">
                            {{ item.title }}
                        </div>
                        <div class="text-primary-content/50 flex items-center gap-1 text-xs">
                            <span>{{ item.sourceLanguage }} → {{ item.targetLanguage }}</span>
                            <span v-if="item.completedAt">
                                • {{ formatRelativeTime(item.completedAt) }}
                            </span>
                        </div>
                    </div>
                    <div ref="recentSentinel" class="h-1"></div>
                    <div
                        v-if="recentLoadingMore"
                        class="text-primary-content/50 py-1 text-center text-xs">
                        {{ i18n.translate('common.loading') }}
                    </div>
                </div>
            </div>
        </div>

    </div>
</template>

<style scoped>
:deep(.dp__theme_dark) {
    --dp-background-color: var(--secondary);
    --dp-text-color: var(--primary-content);
    --dp-border-color: var(--accent);
    --dp-border-color-hover: var(--accent);
    --dp-primary-color: var(--accent);
}

:deep(.dp__input) {
    background-color: var(--secondary) !important;
    border-color: var(--accent) !important;
    color: var(--primary-content) !important;
    font-size: 12px;
    padding: 4px 8px;
}
</style>

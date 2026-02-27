<script setup lang="ts">
import { computed, ref, onMounted, watch } from 'vue'
import { useI18n } from '@/plugins/i18n'
import { useInstanceStore } from '@/store/instance'
import services from '@/services'
import TrendUpIcon from '@/components/icons/TrendUpIcon.vue'
import TrendDownIcon from '@/components/icons/TrendDownIcon.vue'
import TrendFlatIcon from '@/components/icons/TrendFlatIcon.vue'
import type { DailyStatistic } from '@/ts/statistics'
import {
    Chart as ChartJS,
    CategoryScale,
    LinearScale,
    BarElement,
    Title,
    Tooltip
} from 'chart.js/auto'
import { Bar } from 'vue-chartjs'

ChartJS.register(CategoryScale, LinearScale, BarElement, Title, Tooltip)

const i18n = useI18n()
const instanceStore = useInstanceStore()

const props = defineProps<{
    dailyStatistics: DailyStatistic[]
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

const recentTranslations = ref<RecentTranslation[]>([])
const recentLoading = ref(false)
const chartKey = ref(0)

watch(
    () => instanceStore.getTheme,
    () => {
        chartKey.value++
    }
)

onMounted(async () => {
    await fetchRecentTranslations()
})

const fetchRecentTranslations = async () => {
    recentLoading.value = true
    try {
        const response = await services.translationRequest.requests<any>(
            1,
            '',
            'CompletedAt',
            false
        )
        const items = response.items || response.Items || []
        recentTranslations.value = items
            .filter((item: any) => item.status === 'Completed' || item.Status === 'Completed')
            .slice(0, 5)
            .map((item: any) => ({
                id: item.id || item.Id,
                title: item.title || item.Title || 'Unknown',
                sourceLanguage: item.sourceLanguage || item.SourceLanguage || '',
                targetLanguage: item.targetLanguage || item.TargetLanguage || '',
                completedAt: item.completedAt || item.CompletedAt,
                mediaType: item.mediaType || item.MediaType || 'Movie'
            }))
    } catch (error) {
        console.warn('Failed to fetch recent translations:', error)
    } finally {
        recentLoading.value = false
    }
}

const todayCount = computed(() => {
    const today = props.dailyStatistics?.find((s) => {
        const statDate = new Date(s.date)
        const now = new Date()
        return statDate.toDateString() === now.toDateString()
    })
    return today?.translationCount || 0
})

const weekCount = computed(() => {
    const weekAgo = new Date()
    weekAgo.setDate(weekAgo.getDate() - 7)
    return (props.dailyStatistics || [])
        .filter((s) => new Date(s.date) >= weekAgo)
        .reduce((acc, s) => acc + (s.translationCount || 0), 0)
})

const monthCount = computed(() => {
    const monthAgo = new Date()
    monthAgo.setDate(monthAgo.getDate() - 30)
    return (props.dailyStatistics || [])
        .filter((s) => new Date(s.date) >= monthAgo)
        .reduce((acc, s) => acc + (s.translationCount || 0), 0)
})

const trend = computed(() => {
    const now = new Date()
    const last7DaysStart = new Date(now)
    last7DaysStart.setDate(last7DaysStart.getDate() - 7)
    const prev7DaysStart = new Date(last7DaysStart)
    prev7DaysStart.setDate(prev7DaysStart.getDate() - 7)

    const last7Days = (props.dailyStatistics || [])
        .filter((s) => {
            const d = new Date(s.date)
            return d >= last7DaysStart && d < now
        })
        .reduce((acc, s) => acc + (s.translationCount || 0), 0)

    const prev7Days = (props.dailyStatistics || [])
        .filter((s) => {
            const d = new Date(s.date)
            return d >= prev7DaysStart && d < last7DaysStart
        })
        .reduce((acc, s) => acc + (s.translationCount || 0), 0)

    if (prev7Days === 0) return { direction: 'flat', percentage: 0 }
    const percentage = Math.round(((last7Days - prev7Days) / prev7Days) * 100)
    return {
        direction: percentage > 5 ? 'up' : percentage < -5 ? 'down' : 'flat',
        percentage: Math.abs(percentage)
    }
})

const getCssVariable = (variableName: string): string => {
    return getComputedStyle(document.documentElement).getPropertyValue(variableName).trim()
}

const chartData = computed(() => {
    const last14Days = (props.dailyStatistics || []).slice(-14)
    if (!last14Days.length) return null

    return {
        labels: last14Days.map((s) =>
            new Date(s.date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
        ),
        datasets: [
            {
                label: 'Translations',
                data: last14Days.map((s) => s.translationCount),
                backgroundColor: getCssVariable('--accent') + '80',
                borderColor: getCssVariable('--accent'),
                borderRadius: 4,
                barThickness: 8
            }
        ]
    }
})

const chartOptions = {
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
                label: (context: any) => `${context.parsed.y} translations`
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
                maxRotation: 0,
                maxTicksLimit: 7
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
}

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
</script>

<template>
    <div class="flex h-full flex-col">
        <h3 class="text-primary-content/70 mb-3 text-sm font-medium">
            {{ i18n.translate('statistics.translationHistory') }}
        </h3>

        <div v-if="isLoading" class="flex flex-1 items-center justify-center">
            <div class="border-accent h-6 w-6 animate-spin rounded-full border-2 border-t-transparent"></div>
        </div>

        <div v-else class="flex flex-1 flex-col gap-3">
            <div class="flex items-end justify-between">
                <div>
                    <div class="text-primary-content text-2xl font-bold">
                        {{ formatNumber(monthCount) }}
                    </div>
                    <div class="text-primary-content/50 text-xs uppercase tracking-wider">
                        {{ i18n.translate('statistics.thisMonth') }}
                    </div>
                </div>
                <div
                    class="flex items-center gap-1 text-sm font-medium"
                    :class="
                        trend.direction === 'up'
                            ? 'text-green-500'
                            : trend.direction === 'down'
                              ? 'text-red-500'
                              : 'text-primary-content/50'
                    ">
                    <TrendUpIcon v-if="trend.direction === 'up'" class="h-4 w-4 shrink-0" />
                    <TrendDownIcon v-if="trend.direction === 'down'" class="h-4 w-4 shrink-0" />
                    <TrendFlatIcon v-if="trend.direction === 'flat'" class="h-4 w-4 shrink-0" />
                    <span>
                        {{
                            trend.direction === 'flat'
                                ? i18n.translate('statistics.stable')
                                : `${trend.direction === 'up' ? '+' : '-'}${trend.percentage}%`
                        }}
                    </span>
                </div>
            </div>

            <div class="grid grid-cols-3 gap-2">
                <div class="bg-primary/50 rounded-md p-2 text-center">
                    <div class="text-primary-content/50 text-xs">{{ i18n.translate('statistics.today') }}</div>
                    <div class="text-primary-content text-lg font-bold">{{ formatNumber(todayCount) }}</div>
                </div>
                <div class="bg-primary/50 rounded-md p-2 text-center">
                    <div class="text-primary-content/50 text-xs">{{ i18n.translate('statistics.thisWeek') }}</div>
                    <div class="text-primary-content text-lg font-bold">{{ formatNumber(weekCount) }}</div>
                </div>
                <div class="bg-primary/50 rounded-md p-2 text-center">
                    <div class="text-primary-content/50 text-xs">Total</div>
                    <div class="text-primary-content text-lg font-bold">
                        {{ formatNumber((dailyStatistics || []).reduce((acc, s) => acc + s.translationCount, 0)) }}
                    </div>
                </div>
            </div>

            <div class="h-24 min-h-[80px]">
                <Bar v-if="chartData" :key="chartKey" :data="chartData" :options="chartOptions" class="h-full w-full" />
                <div v-else class="text-primary-content/50 flex h-full items-center justify-center text-xs">
                    {{ i18n.translate('statistics.noDataAvailable') }}
                </div>
            </div>

            <div class="flex-1 min-h-0 overflow-hidden">
                <h4 class="text-primary-content/50 mb-1 text-xs font-medium">
                    {{ i18n.translate('statistics.recentTranslations') || 'Recent' }}
                </h4>
                <div class="scrollbar-thin h-full space-y-1.5 overflow-y-auto pr-1">
                    <div v-if="recentLoading" class="text-primary-content/50 py-2 text-center text-xs">
                        {{ i18n.translate('common.loading') }}
                    </div>
                    <div
                        v-else-if="recentTranslations.length === 0"
                        class="text-primary-content/50 py-2 text-center text-xs">
                        {{ i18n.translate('statistics.noRecentTranslations') || 'No recent translations' }}
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
                            <span v-if="item.completedAt">• {{ formatRelativeTime(item.completedAt) }}</span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>
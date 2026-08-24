<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, defineComponent, h } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import ProviderIcon from '@/components/icons/ProviderIcon.vue'
import { getProviderMeta, normalizeServiceKey } from '@/utils/providerMetadata'
import axios from 'axios'
import {
    Chart as ChartJS,
    CategoryScale,
    LinearScale,
    LineElement,
    PointElement,
    Filler,
    Tooltip
} from 'chart.js/auto'
import type { TooltipItem } from 'chart.js'
import { Line } from 'vue-chartjs'

ChartJS.register(CategoryScale, LinearScale, LineElement, PointElement, Filler, Tooltip)

const i18n = useI18n()

interface ApiUsageInfo {
    service: string
    callsToday: number
    callsWeek: number
    callsMonth: number
    limit?: number
    remaining?: number
    tokensUsed?: number
    totalTokens: number
    averageResponseTime: number
    errorCount: number
    successRate: number
    dailyBreakdown: {
        date: string
        callCount: number
        tokenCount: number
    }[]
}

interface ApiUsageServiceRaw {
    CallsToday?: number
    callsToday?: number
    CallsWeek?: number
    callsWeek?: number
    CallsMonth?: number
    callsMonth?: number
    TotalTokens?: number
    totalTokens?: number
    AverageResponseTime?: number
    averageResponseTime?: number
    ErrorCount?: number
    errorCount?: number
    SuccessRate?: number
    successRate?: number
    DailyBreakdown?: ApiUsageInfo['dailyBreakdown']
    dailyBreakdown?: ApiUsageInfo['dailyBreakdown']
}

type ChartLabelContext = TooltipItem<'line'>

const apiUsage = ref<ApiUsageInfo[]>([])
const isLoading = ref(false)
const localLoading = ref(false)
const error = ref<string | null>(null)
const expandedService = ref<string | null>(null)
const summaryStats = ref({
    totalCallsToday: 0,
    totalCallsWeek: 0,
    averageResponseTime: 0,
    successRate: 0
})

const fetchApiUsage = async () => {
    if (isLoading.value) return

    isLoading.value = true
    localLoading.value = true
    error.value = null

    try {
        const response = await axios.get('/api/dashboard/api-usage')
        const data = response.data

        summaryStats.value = {
            totalCallsToday: data.TotalCallsToday || data.totalCallsToday || 0,
            totalCallsWeek: data.TotalCallsWeek || data.totalCallsWeek || 0,
            averageResponseTime: Math.round(
                data.AverageResponseTime || data.averageResponseTime || 0
            ),
            successRate: data.SuccessRate || data.successRate || 100
        }

        const byService = data.ByService || data.byService || {}
        apiUsage.value = Object.entries(byService)
            .map(([service, usage]) => {
                const normalizedUsage = usage as ApiUsageServiceRaw
                return {
                    service,
                    callsToday: normalizedUsage.CallsToday || normalizedUsage.callsToday || 0,
                    callsWeek: normalizedUsage.CallsWeek || normalizedUsage.callsWeek || 0,
                    callsMonth: normalizedUsage.CallsMonth || normalizedUsage.callsMonth || 0,
                    tokensUsed: normalizedUsage.TotalTokens || normalizedUsage.totalTokens || 0,
                    totalTokens: normalizedUsage.TotalTokens || normalizedUsage.totalTokens || 0,
                    averageResponseTime: Math.round(
                        normalizedUsage.AverageResponseTime ||
                            normalizedUsage.averageResponseTime ||
                            0
                    ),
                    errorCount: normalizedUsage.ErrorCount || normalizedUsage.errorCount || 0,
                    successRate: normalizedUsage.SuccessRate || normalizedUsage.successRate || 100,
                    dailyBreakdown:
                        normalizedUsage.DailyBreakdown || normalizedUsage.dailyBreakdown || []
                }
            })
            .sort((left, right) => {
                if (right.callsWeek !== left.callsWeek) {
                    return right.callsWeek - left.callsWeek
                }

                return left.service.localeCompare(right.service)
            })
    } catch (e) {
        error.value = 'Failed to fetch API usage'
        console.error('Failed to fetch API usage:', e)
    } finally {
        isLoading.value = false
        setTimeout(() => {
            localLoading.value = false
        }, 500)
    }
}

const toggleService = (service: string) => {
    expandedService.value = expandedService.value === service ? null : service
}

const formatNumber = (num: number): string => {
    if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`
    if (num >= 1000) return `${(num / 1000).toFixed(1)}K`
    return num.toString()
}

const formatResponseTime = (ms: number): string => {
    if (ms < 1000) return `${Math.round(ms)}ms`
    return `${(ms / 1000).toFixed(1)}s`
}

const getProviderLabel = (service: string): string => {
    return getProviderMeta(service).label
}

const getProviderColor = (service: string): string => {
    return getProviderMeta(service).color
}

const getServiceSubtitle = (usage: ApiUsageInfo): string => {
    const normalizedService = normalizeServiceKey(usage.service).toUpperCase()
    return `${normalizedService} · ${formatNumber(usage.callsMonth)} ${i18n.translate('statistics.month')}`
}

const getSuccessToneClass = (successRate: number): string => {
    if (successRate >= 95) {
        return 'text-green-400'
    }

    if (successRate >= 80) {
        return 'text-yellow-400'
    }

    return 'text-red-400'
}

const getStatusDotClass = (usage: ApiUsageInfo): string => {
    if (usage.errorCount > 0 && usage.successRate < 80) {
        return 'bg-red-500'
    }

    if (usage.successRate >= 95) {
        return 'bg-green-500'
    }

    return 'bg-yellow-500'
}

const summaryCards = computed(() => [
    {
        key: 'today',
        label: i18n.translate('statistics.today'),
        value: formatNumber(summaryStats.value.totalCallsToday),
        hint: i18n.translate('statistics.calls')
    },
    {
        key: 'week',
        label: i18n.translate('statistics.week'),
        value: formatNumber(summaryStats.value.totalCallsWeek),
        hint: i18n.translate('statistics.calls')
    },
    {
        key: 'avg',
        label: i18n.translate('statistics.avgResponse'),
        value: formatResponseTime(summaryStats.value.averageResponseTime),
        hint: i18n.translate('statistics.week')
    },
    {
        key: 'success',
        label: i18n.translate('statistics.success'),
        value: `${summaryStats.value.successRate}%`,
        hint: i18n.translate('statistics.week')
    }
])

const getChartOptions = () => ({
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
        legend: { display: false },
        tooltip: {
            enabled: true,
            backgroundColor: 'rgba(0, 0, 0, 0.8)',
            titleColor: '#fff',
            bodyColor: '#fff',
            padding: 8,
            displayColors: false,
            callbacks: {
                label: (context: ChartLabelContext) => `${context.parsed.y ?? 0} calls`
            }
        }
    },
    scales: {
        x: { display: false },
        y: { display: false }
    },
    elements: {
        point: {
            radius: 0,
            hoverRadius: 4
        },
        line: {
            borderWidth: 2,
            tension: 0.4
        }
    }
})

const getChartData = (dailyBreakdown: ApiUsageInfo['dailyBreakdown'], color: string) => {
    const labels = dailyBreakdown.map((entry) => {
        const date = new Date(entry.date)
        return date.toLocaleDateString('en-US', { weekday: 'short' })
    })

    return {
        labels,
        datasets: [
            {
                data: dailyBreakdown.map((entry) => entry.callCount),
                borderColor: color,
                backgroundColor: `${color}20`,
                fill: true,
                tension: 0.4
            }
        ]
    }
}

const SparklineChart = defineComponent({
    props: {
        data: { type: Array as () => ApiUsageInfo['dailyBreakdown'], required: true },
        color: { type: String, default: '#466e8c' }
    },
    setup(props) {
        const chartData = computed(() => getChartData(props.data, props.color))
        const chartOptions = getChartOptions()

        return () =>
            h('div', { class: 'h-16 w-full' }, [
                h(Line, {
                    data: chartData.value,
                    options: chartOptions
                })
            ])
    }
})

const refreshInterval = ref<number | null>(null)

onMounted(() => {
    fetchApiUsage()
    refreshInterval.value = window.setInterval(fetchApiUsage, 60000)
})

onUnmounted(() => {
    if (refreshInterval.value) {
        clearInterval(refreshInterval.value)
    }
})
</script>

<template>
    <div class="flex h-full flex-col">
        <div class="mb-3 flex items-center justify-between">
            <h3 class="text-primary-content/70 text-sm font-medium">
                {{ i18n.translate('statistics.apiUsage') }}
            </h3>
            <button
                @click="fetchApiUsage"
                :disabled="isLoading"
                class="text-primary-content/50 hover:text-primary-content p-1 transition-colors"
                :title="i18n.translate('statistics.refresh')">
                <RefreshIcon
                    class="h-4 w-4"
                    :class="{ 'animate-spin': localLoading || isLoading }" />
            </button>
        </div>

        <div v-if="error" class="py-4 text-center text-sm text-red-400">
            {{ error }}
        </div>

        <div
            v-else-if="apiUsage.length === 0"
            class="text-primary-content/50 flex flex-1 items-center justify-center text-sm italic">
            {{ i18n.translate('statistics.noApiUsage') }}
        </div>

        <div v-else class="min-h-0 flex-1 space-y-3 overflow-y-auto pr-1">
            <div class="grid grid-cols-2 gap-2">
                <div
                    v-for="card in summaryCards"
                    :key="card.key"
                    class="bg-primary/50 rounded-lg p-3 shadow-sm">
                    <div class="text-primary-content/50 text-xs font-medium">
                        {{ card.label }}
                    </div>
                    <div class="text-primary-content mt-2 text-2xl font-bold">
                        {{ card.value }}
                    </div>
                    <div class="text-primary-content/40 mt-1 text-xs">
                        {{ card.hint }}
                    </div>
                </div>
            </div>

            <div class="space-y-2">
                <div
                    v-for="usage in apiUsage"
                    :key="usage.service"
                    class="bg-primary/35 border-secondary/30 hover:bg-primary/50 rounded-md border transition-colors">
                    <button
                        class="w-full cursor-pointer px-3 py-3 text-left"
                        @click="toggleService(usage.service)">
                        <div class="flex items-start justify-between gap-3">
                            <div class="flex min-w-0 flex-1 items-center gap-3">
                                <div
                                    class="bg-secondary/40 border-secondary/30 flex h-10 w-10 shrink-0 items-center justify-center rounded-md border">
                                    <ProviderIcon
                                        :service="normalizeServiceKey(usage.service)"
                                        class="h-5 w-5"
                                        :style="{ color: getProviderColor(usage.service) }" />
                                </div>

                                <div class="min-w-0 flex-1">
                                    <div class="flex items-center gap-2">
                                        <span
                                            class="text-primary-content truncate text-sm font-medium">
                                            {{ getProviderLabel(usage.service) }}
                                        </span>
                                        <span
                                            class="h-2 w-2 shrink-0 rounded-full"
                                            :class="getStatusDotClass(usage)"></span>
                                    </div>
                                    <div class="text-primary-content/45 mt-1 truncate text-xs">
                                        {{ getServiceSubtitle(usage) }}
                                    </div>
                                </div>
                            </div>

                            <div class="flex shrink-0 items-center gap-3">
                                <div class="text-right">
                                    <div class="text-primary-content text-lg font-bold">
                                        {{ formatNumber(usage.callsWeek) }}
                                    </div>
                                    <div class="text-primary-content/40 text-xs">
                                        {{ i18n.translate('statistics.week') }}
                                    </div>
                                </div>
                                <div
                                    class="text-primary-content/40 transition-transform duration-200"
                                    :class="expandedService === usage.service && 'rotate-90'">
                                    <svg
                                        class="h-4 w-4"
                                        fill="none"
                                        viewBox="0 0 24 24"
                                        stroke="currentColor">
                                        <path
                                            stroke-linecap="round"
                                            stroke-linejoin="round"
                                            stroke-width="2"
                                            d="M9 5l7 7-7 7" />
                                    </svg>
                                </div>
                            </div>
                        </div>
                    </button>

                    <transition
                        enter-active-class="transition-all duration-200 ease-out"
                        enter-from-class="max-h-0 opacity-0"
                        enter-to-class="max-h-64 opacity-100"
                        leave-active-class="transition-all duration-200 ease-in"
                        leave-from-class="max-h-64 opacity-100"
                        leave-to-class="max-h-0 opacity-0">
                        <div
                            v-if="expandedService === usage.service"
                            class="border-secondary/20 space-y-3 border-t px-3 py-3">
                            <div
                                v-if="usage.dailyBreakdown?.length > 0"
                                class="bg-secondary/25 rounded-md p-2">
                                <SparklineChart
                                    :data="usage.dailyBreakdown"
                                    :color="getProviderColor(usage.service)" />
                            </div>

                            <div class="grid grid-cols-2 gap-2 md:grid-cols-4">
                                <div class="bg-secondary/25 rounded-md p-2">
                                    <div class="text-primary-content/45 text-xs font-medium">
                                        {{ i18n.translate('statistics.today') }}
                                    </div>
                                    <div class="text-primary-content mt-1 text-sm font-semibold">
                                        {{ formatNumber(usage.callsToday) }}
                                    </div>
                                </div>

                                <div class="bg-secondary/25 rounded-md p-2">
                                    <div class="text-primary-content/45 text-xs font-medium">
                                        {{ i18n.translate('statistics.avgResponse') }}
                                    </div>
                                    <div class="text-primary-content mt-1 text-sm font-semibold">
                                        {{ formatResponseTime(usage.averageResponseTime) }}
                                    </div>
                                </div>

                                <div class="bg-secondary/25 rounded-md p-2">
                                    <div class="text-primary-content/45 text-xs font-medium">
                                        {{ i18n.translate('statistics.month') }}
                                    </div>
                                    <div class="text-primary-content mt-1 text-sm font-semibold">
                                        {{ formatNumber(usage.callsMonth) }}
                                    </div>
                                </div>

                                <div class="bg-secondary/25 rounded-md p-2">
                                    <div class="text-primary-content/45 text-xs font-medium">
                                        {{ i18n.translate('statistics.success') }}
                                    </div>
                                    <div
                                        class="mt-1 text-sm font-semibold"
                                        :class="getSuccessToneClass(usage.successRate)">
                                        {{ usage.successRate }}%
                                    </div>
                                </div>

                                <div
                                    v-if="usage.totalTokens > 0"
                                    class="bg-secondary/25 rounded-md p-2 md:col-span-2">
                                    <div class="text-primary-content/45 text-xs font-medium">
                                        {{ i18n.translate('statistics.tokens') }}
                                    </div>
                                    <div class="text-primary-content mt-1 text-sm font-semibold">
                                        {{ formatNumber(usage.totalTokens) }}
                                    </div>
                                </div>

                                <div class="bg-secondary/25 rounded-md p-2 md:col-span-2">
                                    <div class="text-primary-content/45 text-xs font-medium">
                                        {{ i18n.translate('statistics.errors') }}
                                    </div>
                                    <div
                                        class="mt-1 text-sm font-semibold"
                                        :class="
                                            usage.errorCount > 0
                                                ? 'text-red-400'
                                                : 'text-primary-content'
                                        ">
                                        {{ usage.errorCount }}
                                    </div>
                                </div>
                            </div>
                        </div>
                    </transition>
                </div>
            </div>
        </div>
    </div>
</template>

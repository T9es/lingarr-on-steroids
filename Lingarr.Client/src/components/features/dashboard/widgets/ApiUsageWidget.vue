<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, defineComponent, h } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import ProviderIcon from '@/components/icons/ProviderIcon.vue'
import { getProviderMeta, normalizeServiceKey, toRgba } from '@/utils/providerMetadata'
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
            .map(([service, usage]: [string, any]) => ({
                service,
                callsToday: usage.CallsToday || usage.callsToday || 0,
                callsWeek: usage.CallsWeek || usage.callsWeek || 0,
                callsMonth: usage.CallsMonth || usage.callsMonth || 0,
                tokensUsed: usage.TotalTokens || usage.totalTokens || 0,
                totalTokens: usage.TotalTokens || usage.totalTokens || 0,
                averageResponseTime: Math.round(
                    usage.AverageResponseTime || usage.averageResponseTime || 0
                ),
                errorCount: usage.ErrorCount || usage.errorCount || 0,
                successRate: usage.SuccessRate || usage.successRate || 100,
                dailyBreakdown: usage.DailyBreakdown || usage.dailyBreakdown || []
            }))
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

const getServiceColor = (usage: ApiUsageInfo): string => {
    return getProviderMeta(usage.service).color
}

const getProviderCardStyle = (service: string) => {
    const { color } = getProviderMeta(service)

    return {
        borderColor: toRgba(color, 0.18),
        backgroundImage: `linear-gradient(135deg, ${toRgba(color, 0.14)} 0%, ${toRgba(color, 0.04)} 30%, transparent 62%)`,
        boxShadow: `inset 3px 0 0 ${toRgba(color, 0.95)}`
    }
}

const getProviderBadgeStyle = (service: string) => {
    const { color } = getProviderMeta(service)

    return {
        color,
        backgroundColor: toRgba(color, 0.14),
        borderColor: toRgba(color, 0.3),
        boxShadow: `0 10px 30px ${toRgba(color, 0.12)}`
    }
}

const getProviderChipStyle = (service: string) => {
    const { color } = getProviderMeta(service)

    return {
        borderColor: toRgba(color, 0.2),
        backgroundColor: toRgba(color, 0.1)
    }
}

const getTrendToneClass = (usage: ApiUsageInfo): string => {
    if (usage.errorCount > 0 && usage.successRate < 80) {
        return 'text-red-400'
    }

    if (usage.successRate >= 95) {
        return 'text-green-400'
    }

    if (usage.successRate >= 80) {
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

const getSummaryCardClass = (key: string): string => {
    if (key === 'today') {
        return 'border-accent/25 bg-secondary/65'
    }

    if (key === 'week') {
        return 'border-secondary/80 bg-primary/80'
    }

    if (key === 'avg') {
        return 'border-secondary/70 bg-tertiary/80'
    }

    return 'border-accent/20 bg-primary/85'
}

const getServiceSubtitle = (usage: ApiUsageInfo): string => {
    const providerLabel = getProviderLabel(usage.service)

    if (usage.service !== providerLabel) {
        return usage.service
    }

    return `${formatNumber(usage.callsMonth)} ${i18n.translate('statistics.month')}`
}

const getSuccessToneCardClass = (usage: ApiUsageInfo): string => {
    if (usage.errorCount > 0 && usage.successRate < 80) {
        return 'border-red-500/20 bg-red-500/10'
    }

    if (usage.successRate >= 95) {
        return 'border-green-500/20 bg-green-500/10'
    }

    return 'border-yellow-500/20 bg-yellow-500/10'
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
                label: (context: any) => `${context.parsed.y} calls`
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

const getChartData = (dailyBreakdown: any[], color: string) => {
    const labels = dailyBreakdown.map((d) => {
        const date = new Date(d.date)
        return date.toLocaleDateString('en-US', { weekday: 'short' })
    })

    const data = dailyBreakdown.map((d) => d.callCount)

    return {
        labels,
        datasets: [
            {
                data,
                borderColor: color,
                backgroundColor: color + '20',
                fill: true,
                tension: 0.4
            }
        ]
    }
}

const SparklineChart = defineComponent({
    props: {
        data: { type: Array as () => any[], required: true },
        color: { type: String, default: '#9333ea' }
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

        <div v-if="error" class="py-4 text-center text-sm text-red-500">
            {{ error }}
        </div>

        <div
            v-else-if="apiUsage.length === 0"
            class="text-primary-content/50 flex flex-1 items-center justify-center text-sm italic">
            {{ i18n.translate('statistics.noApiUsage') }}
        </div>

        <div v-else class="min-h-0 flex-1 space-y-3 overflow-y-auto pr-1">
            <div
                class="from-secondary/95 to-tertiary/95 border-secondary/60 rounded-xl border bg-linear-to-br p-3 shadow-md">
                <div class="grid grid-cols-2 gap-2 md:grid-cols-4">
                    <div
                        v-for="card in summaryCards"
                        :key="card.key"
                        class="rounded-xl border p-3 shadow-sm"
                        :class="getSummaryCardClass(card.key)">
                        <div class="text-primary-content/55 text-[11px] font-medium tracking-[0.16em] uppercase">
                            {{ card.label }}
                        </div>
                        <div class="text-primary-content mt-2 text-2xl font-semibold tracking-tight">
                            {{ card.value }}
                        </div>
                        <div class="text-primary-content/45 mt-2 text-xs">
                            {{ card.hint }}
                        </div>
                    </div>
                </div>
            </div>

            <div class="space-y-2.5">
                <div
                    v-for="usage in apiUsage"
                    :key="usage.service"
                    class="rounded-xl border bg-primary/85 shadow-md transition-all duration-200 hover:bg-primary/90"
                    :style="getProviderCardStyle(usage.service)"
                    :class="expandedService === usage.service && 'ring-accent/30 ring-1'">
                    <div
                        @click="toggleService(usage.service)"
                        class="cursor-pointer p-3.5">
                        <div class="flex items-start justify-between gap-3">
                            <div class="flex min-w-0 flex-1 items-center gap-3">
                                <div
                                    class="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border"
                                    :style="getProviderBadgeStyle(usage.service)">
                                    <ProviderIcon
                                        :service="normalizeServiceKey(usage.service)"
                                        class="h-6 w-6" />
                                </div>

                                <div class="min-w-0 flex-1">
                                    <div class="flex items-center gap-2.5">
                                        <span class="text-primary-content truncate text-base font-semibold">
                                            {{ getProviderLabel(usage.service) }}
                                        </span>
                                        <span
                                            class="inline-flex h-2.5 w-2.5 shrink-0 rounded-full"
                                            :class="getStatusDotClass(usage)"></span>
                                    </div>
                                    <div class="mt-1 flex items-center gap-2">
                                        <span class="text-primary-content/50 truncate text-xs">
                                            {{ getServiceSubtitle(usage) }}
                                        </span>
                                        <span
                                            class="rounded-full border px-2 py-0.5 text-[10px] font-medium tracking-[0.18em] uppercase"
                                            :style="getProviderChipStyle(usage.service)">
                                            {{ normalizeServiceKey(usage.service) }}
                                        </span>
                                    </div>
                                </div>
                            </div>

                            <div class="shrink-0 text-right">
                                <div
                                    class="border-secondary/60 bg-secondary/55 rounded-xl border px-3 py-2 shadow-sm">
                                    <div class="text-primary-content text-xl font-semibold leading-none">
                                        {{ formatNumber(usage.callsWeek) }}
                                    </div>
                                    <div class="text-primary-content/50 mt-1 text-[11px] uppercase">
                                        {{ i18n.translate('statistics.week') }}
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div class="mt-3.5 grid grid-cols-3 gap-2.5">
                            <div class="border-secondary/70 bg-secondary/60 rounded-xl border p-3 text-center">
                                <div class="text-primary-content/50 text-[11px] font-medium tracking-[0.12em] uppercase">
                                    {{ i18n.translate('statistics.today') }}
                                </div>
                                <div class="text-primary-content mt-2 text-lg font-semibold tracking-tight">
                                    {{ formatNumber(usage.callsToday) }}
                                </div>
                            </div>

                            <div class="border-secondary/70 bg-secondary/60 rounded-xl border p-3 text-center">
                                <div class="text-primary-content/50 text-[11px] font-medium tracking-[0.12em] uppercase">
                                    {{ i18n.translate('statistics.avgResponse') }}
                                </div>
                                <div class="text-primary-content mt-2 text-lg font-semibold tracking-tight">
                                    {{ formatResponseTime(usage.averageResponseTime) }}
                                </div>
                            </div>

                            <div
                                class="rounded-xl border p-3 text-center"
                                :class="getSuccessToneCardClass(usage)"
                                :style="getProviderChipStyle(usage.service)">
                                <div class="text-primary-content/55 text-[11px] font-medium tracking-[0.12em] uppercase">
                                    {{ i18n.translate('statistics.success') }}
                                </div>
                                <div
                                    class="mt-2 text-lg font-semibold tracking-tight"
                                    :class="getTrendToneClass(usage)">
                                    {{ usage.successRate }}%
                                </div>
                            </div>
                        </div>

                        <div class="text-primary-content/40 mt-3 flex items-center justify-between text-xs">
                            <span>
                                {{ i18n.translate('statistics.calls') }} ·
                                {{ formatNumber(usage.callsMonth) }} {{ i18n.translate('statistics.month') }}
                            </span>
                            <div
                                class="text-primary-content/50 transition-transform duration-200"
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

                    <transition
                        enter-active-class="transition-all duration-200 ease-out"
                        enter-from-class="max-h-0 opacity-0"
                        enter-to-class="max-h-48 opacity-100"
                        leave-active-class="transition-all duration-200 ease-in"
                        leave-from-class="max-h-48 opacity-100"
                        leave-to-class="max-h-0 opacity-0">
                        <div
                            v-if="expandedService === usage.service"
                            class="border-secondary/25 border-t p-3.5">
                            <div v-if="usage.dailyBreakdown?.length > 0" class="mb-3.5">
                                <SparklineChart
                                    :data="usage.dailyBreakdown"
                                    :color="getServiceColor(usage)" />
                            </div>

                            <div class="grid grid-cols-2 gap-3 text-xs md:grid-cols-4">
                                <div
                                    v-if="usage.totalTokens > 0"
                                    class="border-secondary/60 bg-secondary/55 rounded-xl border p-3">
                                    <div class="text-primary-content/50 flex items-center gap-2">
                                        <svg
                                            class="h-4 w-4"
                                            viewBox="0 0 24 24"
                                            fill="none"
                                            stroke="currentColor"
                                            stroke-width="1.8"
                                            stroke-linecap="round"
                                            stroke-linejoin="round">
                                            <path d="M7 8h10" />
                                            <path d="M7 12h7" />
                                            <path d="M7 16h5" />
                                            <path d="M5 5h14v14H5z" />
                                        </svg>
                                        <span>{{ i18n.translate('statistics.tokens') }}</span>
                                    </div>
                                    <div class="text-primary-content mt-2 text-sm font-semibold">
                                        {{ formatNumber(usage.totalTokens) }}
                                    </div>
                                </div>

                                <div
                                    class="rounded-xl border p-3"
                                    :class="getSuccessToneCardClass(usage)">
                                    <div class="text-primary-content/50 flex items-center gap-2">
                                        <svg
                                            class="h-4 w-4"
                                            viewBox="0 0 24 24"
                                            fill="none"
                                            stroke="currentColor"
                                            stroke-width="1.8"
                                            stroke-linecap="round"
                                            stroke-linejoin="round">
                                            <path d="M20 6L9 17l-5-5" />
                                        </svg>
                                        <span>{{ i18n.translate('statistics.success') }}</span>
                                    </div>
                                    <div
                                        class="mt-2 text-sm font-semibold"
                                        :class="
                                            usage.successRate >= 95
                                                ? 'text-green-500'
                                                : usage.successRate >= 80
                                                  ? 'text-yellow-500'
                                                  : 'text-red-500'
                                        ">
                                        {{ usage.successRate }}%
                                    </div>
                                </div>

                                <div class="rounded-xl border border-red-500/20 bg-red-500/10 p-3">
                                    <div class="text-primary-content/50 flex items-center gap-2">
                                        <svg
                                            class="h-4 w-4"
                                            viewBox="0 0 24 24"
                                            fill="none"
                                            stroke="currentColor"
                                            stroke-width="1.8"
                                            stroke-linecap="round"
                                            stroke-linejoin="round">
                                            <path d="M12 9v4" />
                                            <path d="M12 17h.01" />
                                            <path
                                                d="M10.29 3.86l-8 14A2 2 0 004 21h16a2 2 0 001.71-3.14l-8-14a2 2 0 00-3.42 0z" />
                                        </svg>
                                        <span>{{ i18n.translate('statistics.errors') }}</span>
                                    </div>
                                    <div class="mt-2 text-sm font-semibold text-red-500">
                                        {{ usage.errorCount }}
                                    </div>
                                </div>

                                <div class="border-secondary/60 bg-secondary/55 rounded-xl border p-3">
                                    <div class="text-primary-content/50 flex items-center gap-2">
                                        <svg
                                            class="h-4 w-4"
                                            viewBox="0 0 24 24"
                                            fill="none"
                                            stroke="currentColor"
                                            stroke-width="1.8"
                                            stroke-linecap="round"
                                            stroke-linejoin="round">
                                            <rect x="3" y="4" width="18" height="18" rx="2" />
                                            <path d="M16 2v4M8 2v4M3 10h18" />
                                        </svg>
                                        <span>{{ i18n.translate('statistics.month') }}</span>
                                    </div>
                                    <div class="text-primary-content mt-2 text-sm font-semibold">
                                        {{ formatNumber(usage.callsMonth) }}
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

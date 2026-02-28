<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, defineComponent, h } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
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
            averageResponseTime: Math.round(data.AverageResponseTime || data.averageResponseTime || 0),
            successRate: data.SuccessRate || data.successRate || 100
        }

        const byService = data.ByService || data.byService || {}
        apiUsage.value = Object.entries(byService).map(([service, usage]: [string, any]) => ({
            service,
            callsToday: usage.TotalCalls || usage.totalCalls || 0,
            callsWeek: usage.TotalCalls || usage.totalCalls || 0,
            callsMonth: usage.TotalCalls || usage.totalCalls || 0,
            tokensUsed: usage.TotalTokens || usage.totalTokens || 0,
            totalTokens: usage.TotalTokens || usage.totalTokens || 0,
            averageResponseTime: Math.round(usage.AverageResponseTime || usage.averageResponseTime || 0),
            errorCount: usage.ErrorCount || usage.errorCount || 0,
            successRate: usage.SuccessRate || usage.successRate || 100,
            dailyBreakdown: usage.DailyBreakdown || usage.dailyBreakdown || []
        }))
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

const getServiceColor = (usage: ApiUsageInfo): string => {
    if (usage.errorCount > 0) return '#ef4444'
    if (usage.successRate >= 95) return '#22c55e'
    return '#eab308'
}

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
    const labels = dailyBreakdown.map(d => {
        const date = new Date(d.date)
        return date.toLocaleDateString('en-US', { weekday: 'short' })
    })
    
    const data = dailyBreakdown.map(d => d.callCount)
    
    return {
        labels,
        datasets: [{
            data,
            borderColor: color,
            backgroundColor: color + '20',
            fill: true,
            tension: 0.4
        }]
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
        
        return () => h('div', { class: 'h-16 w-full' }, [
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
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': localLoading || isLoading }" />
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
            <div class="bg-secondary/30 rounded-md p-3">
                <div class="grid grid-cols-3 gap-2 text-xs">
                    <div class="text-center">
                        <div class="text-primary-content/50 tracking-wider uppercase">Today</div>
                        <div class="text-primary-content mt-0.5 text-lg font-bold">
                            {{ formatNumber(summaryStats.totalCallsToday) }}
                        </div>
                    </div>
                    <div class="text-center">
                        <div class="text-primary-content/50 tracking-wider uppercase">Week</div>
                        <div class="text-primary-content mt-0.5 text-lg font-bold">
                            {{ formatNumber(summaryStats.totalCallsWeek) }}
                        </div>
                    </div>
                    <div class="text-center">
                        <div class="text-primary-content/50 tracking-wider uppercase">Avg</div>
                        <div class="text-primary-content mt-0.5 text-lg font-bold">
                            {{ formatResponseTime(summaryStats.averageResponseTime) }}
                        </div>
                    </div>
                </div>
            </div>

            <div class="space-y-2">
                <div
                    v-for="usage in apiUsage"
                    :key="usage.service"
                    class="bg-primary rounded-lg border border-secondary/20 shadow-sm transition-all duration-200 hover:bg-primary/80"
                    :class="expandedService === usage.service && 'ring-1 ring-accent/30'">
                    
                    <div
                        @click="toggleService(usage.service)"
                        class="flex cursor-pointer items-center justify-between p-3">
                        
                        <div class="flex items-center gap-2 min-w-0 flex-1">
                            <span
                                class="h-2 w-2 rounded-full"
                                :class="
                                    usage.errorCount > 0 ? 'bg-red-500' :
                                    usage.successRate >= 95 ? 'bg-green-500' :
                                    'bg-yellow-500'
                                "></span>
                            
                            <span class="text-primary-content truncate text-sm font-medium">
                                {{ usage.service }}
                            </span>
                        </div>

                        <div class="mx-3 text-primary-content text-sm font-semibold">
                            {{ formatNumber(usage.callsWeek) }} calls
                        </div>

                        <div
                            class="text-primary-content/50 transition-transform duration-200"
                            :class="expandedService === usage.service && 'rotate-90'">
                            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7" />
                            </svg>
                        </div>
                    </div>

                    <div class="border-secondary/20 border-t px-3 pb-3 pt-1">
                        <div class="text-primary-content/60 flex items-center gap-2 text-xs">
                            <span>Today: <span class="text-primary-content font-medium">{{ formatNumber(usage.callsToday) }}</span></span>
                            <span>•</span>
                            <span>Week: <span class="text-primary-content font-medium">{{ formatNumber(usage.callsWeek) }}</span></span>
                            <span>•</span>
                            <span>⚡ {{ formatResponseTime(usage.averageResponseTime) }}</span>
                        </div>
                    </div>

                    <transition
                        enter-active-class="transition-all duration-200 ease-out"
                        enter-from-class="max-h-0 opacity-0"
                        enter-to-class="max-h-48 opacity-100"
                        leave-active-class="transition-all duration-200 ease-in"
                        leave-from-class="max-h-48 opacity-100"
                        leave-to-class="max-h-0 opacity-0">
                        <div v-if="expandedService === usage.service" class="border-secondary/20 border-t p-3">
                            <div v-if="usage.dailyBreakdown?.length > 0" class="mb-3">
                                <SparklineChart
                                    :data="usage.dailyBreakdown"
                                    :color="getServiceColor(usage)" />
                            </div>

                            <div class="grid grid-cols-2 gap-3 text-xs">
                                <div v-if="usage.totalTokens > 0" class="bg-secondary/30 rounded-md p-2">
                                    <div class="text-primary-content/50 flex items-center gap-1">
                                        <span>💬</span>
                                        <span>Tokens</span>
                                    </div>
                                    <div class="text-primary-content mt-1 font-semibold">
                                        {{ formatNumber(usage.totalTokens) }}
                                    </div>
                                </div>

                                <div class="bg-secondary/30 rounded-md p-2">
                                    <div class="text-primary-content/50 flex items-center gap-1">
                                        <span>✓</span>
                                        <span>Success</span>
                                    </div>
                                    <div
                                        class="mt-1 font-semibold"
                                        :class="
                                            usage.successRate >= 95 ? 'text-green-500' :
                                            usage.successRate >= 80 ? 'text-yellow-500' :
                                            'text-red-500'
                                        ">
                                        {{ usage.successRate }}%
                                    </div>
                                </div>

                                <div
                                    v-if="usage.errorCount > 0"
                                    class="bg-red-500/10 rounded-md p-2">
                                    <div class="text-primary-content/50 flex items-center gap-1">
                                        <span>⚠</span>
                                        <span>Errors</span>
                                    </div>
                                    <div class="text-red-500 mt-1 font-semibold">
                                        {{ usage.errorCount }}
                                    </div>
                                </div>

                                <div class="bg-secondary/30 rounded-md p-2">
                                    <div class="text-primary-content/50 flex items-center gap-1">
                                        <span>📅</span>
                                        <span>Month</span>
                                    </div>
                                    <div class="text-primary-content mt-1 font-semibold">
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

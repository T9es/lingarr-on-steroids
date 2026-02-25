<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CardComponent from '@/components/common/CardComponent.vue'
import TrendUpIcon from '@/components/icons/TrendUpIcon.vue'
import TrendDownIcon from '@/components/icons/TrendDownIcon.vue'
import TrendFlatIcon from '@/components/icons/TrendFlatIcon.vue'
import type { DailyStatistic } from '@/ts/statistics'

const i18n = useI18n()

// Props - receive data from parent
const props = defineProps<{
    dailyStatistics: DailyStatistic[]
    isLoading?: boolean
}>()

// Calculate totals for different time periods
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

// Calculate trends (compare last 7 days to previous 7 days)
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

const formatNumber = (num: number): string => {
    if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`
    if (num >= 1000) return `${(num / 1000).toFixed(1)}K`
    return num.toString()
}
</script>

<template>
    <CardComponent :title="i18n.translate('statistics.translationHistory')" class="h-full">
        <template #content>
        <div v-if="isLoading" class="flex items-center justify-center py-8">
            <div
                class="border-accent h-6 w-6 animate-spin rounded-full border-2 border-t-transparent"></div>
        </div>
        <div v-else class="space-y-4">
            <!-- Trend indicator -->
            <div class="text-secondary-content flex items-center gap-2 text-sm">
                <TrendUpIcon v-if="trend.direction === 'up'" class="h-4 w-4 text-green-500" />
                <TrendDownIcon v-if="trend.direction === 'down'" class="h-4 w-4 text-red-500" />
                <TrendFlatIcon
                    v-if="trend.direction === 'flat'"
                    class="text-secondary-content h-4 w-4" />
                <span v-if="trend.direction === 'up'" class="text-green-500">
                    +{{ trend.percentage }}% {{ i18n.translate('statistics.vsLastWeek') }}
                </span>
                <span v-else-if="trend.direction === 'down'" class="text-red-500">
                    -{{ trend.percentage }}% {{ i18n.translate('statistics.vsLastWeek') }}
                </span>
                <span v-else class="text-secondary-content">
                    {{ i18n.translate('statistics.stable') }}
                </span>
            </div>

            <!-- Time period stats -->
            <div class="grid grid-cols-3 gap-4">
                <!-- Today -->
                <div class="text-center">
                    <div class="text-secondary-content mb-1 text-xs tracking-wider uppercase">
                        {{ i18n.translate('statistics.today') }}
                    </div>
                    <div class="text-primary-content text-2xl font-bold">
                        {{ formatNumber(todayCount) }}
                    </div>
                    <div class="text-secondary-content text-xs">
                        {{ i18n.translate('statistics.translations') }}
                    </div>
                </div>

                <!-- This Week -->
                <div class="text-center">
                    <div class="text-secondary-content mb-1 text-xs tracking-wider uppercase">
                        {{ i18n.translate('statistics.thisWeek') }}
                    </div>
                    <div class="text-primary-content text-2xl font-bold">
                        {{ formatNumber(weekCount) }}
                    </div>
                    <div class="text-secondary-content text-xs">
                        {{ i18n.translate('statistics.translations') }}
                    </div>
                </div>

                <!-- This Month -->
                <div class="text-center">
                    <div class="text-secondary-content mb-1 text-xs tracking-wider uppercase">
                        {{ i18n.translate('statistics.thisMonth') }}
                    </div>
                    <div class="text-primary-content text-2xl font-bold">
                        {{ formatNumber(monthCount) }}
                    </div>
                    <div class="text-secondary-content text-xs">
                        {{ i18n.translate('statistics.translations') }}
                    </div>
                </div>
            </div>
        </div>
        </template>
    </CardComponent>
</template>

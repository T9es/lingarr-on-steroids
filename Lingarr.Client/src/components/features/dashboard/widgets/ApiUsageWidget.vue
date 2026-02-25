<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CardComponent from '@/components/common/CardComponent.vue'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import axios from 'axios'

const i18n = useI18n()

interface ApiUsageInfo {
    service: string
    callsToday: number
    callsWeek: number
    callsMonth: number
    limit?: number
    remaining?: number
    tokensUsed?: number
}

const apiUsage = ref<ApiUsageInfo[]>([])
const isLoading = ref(false)
const error = ref<string | null>(null)

const fetchApiUsage = async () => {
    isLoading.value = true
    error.value = null

    try {
        const response = await axios.get('/api/dashboard/api-usage')
        const data = response.data

        // Backend returns ApiUsageStatus with ByService dictionary
        // Map ByService dictionary to our interface
        const byService = data.ByService || data.byService || {}
        apiUsage.value = Object.entries(byService).map(([service, usage]: [string, any]) => ({
            service,
            callsToday: usage.TotalCalls || usage.totalCalls || 0,
            callsWeek: usage.TotalCalls || usage.totalCalls || 0,
            callsMonth: usage.TotalCalls || usage.totalCalls || 0,
            tokensUsed: usage.TotalTokens || usage.totalTokens || 0
        }))
    } catch (e) {
        error.value = 'Failed to fetch API usage'
        console.error('Failed to fetch API usage:', e)
    } finally {
        isLoading.value = false
    }
}

const refreshInterval = ref<number | null>(null)

onMounted(() => {
    fetchApiUsage()
    // Refresh every minute
    refreshInterval.value = window.setInterval(fetchApiUsage, 60000)
})

onUnmounted(() => {
    if (refreshInterval.value) {
        clearInterval(refreshInterval.value)
    }
})

const formatNumber = (num: number): string => {
    if (num >= 1000000) return `${(num / 1000000).toFixed(1)}M`
    if (num >= 1000) return `${(num / 1000).toFixed(1)}K`
    return num.toString()
}

const getUsagePercentage = (usage: ApiUsageInfo): number => {
    if (!usage.limit) return 0
    return Math.round((usage.callsMonth / usage.limit) * 100)
}
</script>

<template>
    <CardComponent :title="i18n.translate('statistics.apiUsage')" class="h-full">
        <template #content>
        <div class="mb-2 flex justify-end">
            <button
                @click="fetchApiUsage"
                :disabled="isLoading"
                class="text-secondary-content hover:text-primary-content p-1 transition-colors"
                :title="i18n.translate('statistics.refresh')">
                <RefreshIcon class="h-4 w-4" :class="{ 'animate-spin': isLoading }" />
            </button>
        </div>

        <div v-if="error" class="py-4 text-center text-sm text-red-400">
            {{ error }}
        </div>

        <div
            v-else-if="apiUsage.length === 0"
            class="text-secondary-content py-4 text-center text-sm">
            {{ i18n.translate('statistics.noApiUsage') }}
        </div>

        <div v-else class="max-h-64 space-y-4 overflow-y-auto">
            <div
                v-for="usage in apiUsage"
                :key="usage.service"
                class="rounded-md border border-gray-700 bg-black/30 p-3">
                <div class="mb-2 flex items-center justify-between">
                    <span class="text-primary-content text-sm font-medium">
                        {{ usage.service }}
                    </span>
                    <span v-if="usage.limit" class="text-secondary-content text-xs">
                        {{ getUsagePercentage(usage) }}% {{ i18n.translate('statistics.used') }}
                    </span>
                </div>

                <!-- Usage limit bar -->
                <div v-if="usage.limit" class="mb-2 h-1.5 overflow-hidden rounded-full bg-gray-700">
                    <div
                        class="h-full transition-all duration-300"
                        :class="
                            getUsagePercentage(usage) > 80
                                ? 'bg-red-500'
                                : getUsagePercentage(usage) > 50
                                  ? 'bg-yellow-500'
                                  : 'bg-green-500'
                        "
                        :style="{ width: `${getUsagePercentage(usage)}%` }"></div>
                </div>

                <!-- Stats grid -->
                <div class="grid grid-cols-3 gap-2 text-xs">
                    <div class="text-center">
                        <div class="text-secondary-content">
                            {{ i18n.translate('statistics.today') }}
                        </div>
                        <div class="text-primary-content font-medium">
                            {{ formatNumber(usage.callsToday) }}
                        </div>
                    </div>
                    <div class="text-center">
                        <div class="text-secondary-content">
                            {{ i18n.translate('statistics.week') }}
                        </div>
                        <div class="text-primary-content font-medium">
                            {{ formatNumber(usage.callsWeek) }}
                        </div>
                    </div>
                    <div class="text-center">
                        <div class="text-secondary-content">
                            {{ i18n.translate('statistics.month') }}
                        </div>
                        <div class="text-primary-content font-medium">
                            {{ formatNumber(usage.callsMonth) }}
                        </div>
                    </div>
                </div>

                <!-- Token usage -->
                <div
                    v-if="usage.tokensUsed"
                    class="text-secondary-content mt-2 border-t border-gray-700 pt-2 text-xs">
                    {{ i18n.translate('statistics.tokensUsed') }}:
                    {{ formatNumber(usage.tokensUsed) }}
                </div>

                <!-- Remaining quota -->
                <div v-if="usage.remaining" class="text-secondary-content mt-1 text-xs">
                    {{ i18n.translate('statistics.remaining') }}:
                    {{ formatNumber(usage.remaining) }}
                </div>
            </div>
        </div>
        </template>
    </CardComponent>
</template>

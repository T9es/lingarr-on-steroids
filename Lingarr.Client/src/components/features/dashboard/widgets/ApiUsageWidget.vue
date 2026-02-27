<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
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
    <div class="flex h-full flex-col">
        <div class="mb-4 flex items-center justify-between">
            <h3 class="text-primary-content/70 text-sm font-medium">
                {{ i18n.translate('statistics.apiUsage') }}
            </h3>
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
            class="text-secondary-content py-8 text-center text-sm italic opacity-70">
            {{ i18n.translate('statistics.noApiUsage') }}
        </div>

        <div v-else class="min-h-0 flex-1 space-y-4 overflow-x-hidden overflow-y-auto pr-1">
            <div
                v-for="usage in apiUsage"
                :key="usage.service"
                class="border-secondary/20 border-b pb-4 last:border-0 last:pb-0">
                <div class="mb-2 flex items-center justify-between">
                    <span class="text-primary-content max-w-[60%] truncate text-sm font-medium">
                        {{ usage.service }}
                    </span>
                    <span v-if="usage.limit" class="text-secondary-content text-xs font-medium">
                        {{ getUsagePercentage(usage) }}% {{ i18n.translate('statistics.used') }}
                    </span>
                </div>

                <!-- Usage limit bar -->
                <div
                    v-if="usage.limit"
                    class="bg-secondary mb-3 h-1.5 overflow-hidden rounded-full">
                    <div
                        class="h-full transition-all duration-300"
                        :class="
                            getUsagePercentage(usage) > 80
                                ? 'bg-red-500'
                                : getUsagePercentage(usage) > 50
                                  ? 'bg-yellow-500'
                                  : 'bg-accent'
                        "
                        :style="{ width: `${getUsagePercentage(usage)}%` }"></div>
                </div>

                <!-- Stats grid -->
                <div class="grid grid-cols-3 gap-2 text-xs">
                    <div class="text-center">
                        <div class="text-secondary-content tracking-wider uppercase opacity-70">
                            {{ i18n.translate('statistics.today') }}
                        </div>
                        <div class="text-primary-content mt-0.5 text-sm font-bold">
                            {{ formatNumber(usage.callsToday) }}
                        </div>
                    </div>
                    <div class="text-center">
                        <div class="text-secondary-content tracking-wider uppercase opacity-70">
                            {{ i18n.translate('statistics.week') }}
                        </div>
                        <div class="text-primary-content mt-0.5 text-sm font-bold">
                            {{ formatNumber(usage.callsWeek) }}
                        </div>
                    </div>
                    <div class="text-center">
                        <div class="text-secondary-content tracking-wider uppercase opacity-70">
                            {{ i18n.translate('statistics.month') }}
                        </div>
                        <div class="text-primary-content mt-0.5 text-sm font-bold">
                            {{ formatNumber(usage.callsMonth) }}
                        </div>
                    </div>
                </div>

                <!-- Token usage -->
                <div
                    v-if="usage.tokensUsed"
                    class="text-secondary-content border-secondary/20 mt-3 flex justify-between border-t pt-2 text-xs">
                    <span>{{ i18n.translate('statistics.tokensUsed') }}</span>
                    <span class="text-primary-content font-medium">
                        {{ formatNumber(usage.tokensUsed) }}
                    </span>
                </div>

                <!-- Remaining quota -->
                <div
                    v-if="usage.remaining"
                    class="text-secondary-content mt-1 flex justify-between text-xs">
                    <span>{{ i18n.translate('statistics.remaining') }}</span>
                    <span class="text-primary-content font-medium">
                        {{ formatNumber(usage.remaining) }}
                    </span>
                </div>
            </div>
        </div>
    </div>
</template>

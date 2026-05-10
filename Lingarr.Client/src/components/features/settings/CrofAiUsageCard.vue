<template>
    <div class="border-secondary bg-primary mt-6 flex flex-col rounded-md border p-4 shadow-sm">
        <div class="mb-4 flex items-center justify-between">
            <div>
                <p class="font-semibold">
                    {{ translate('settings.services.crofaiUsageTitle') }}
                </p>
                <p class="text-secondary-content/80 text-sm">
                    {{ translate('settings.services.crofaiUsageDescription') }}
                </p>
            </div>
            <button
                class="bg-primary text-primary-content rounded px-3 py-1 text-sm"
                :class="{ 'opacity-50': loading }"
                :disabled="loading"
                @click="loadUsage(true)">
                {{ translate('settings.services.refreshUsage') }}
            </button>
        </div>

        <div v-if="errorMessage" class="mt-2 rounded-md bg-red-500/10 p-3 text-sm text-red-300">
            {{ errorMessage }}
        </div>

        <div v-else class="mt-4 grid gap-4 md:grid-cols-2">
            <div class="border-secondary flex flex-col rounded-md border p-3">
                <span class="text-secondary-content text-xs">
                    {{ translate('settings.services.requestsToday') }}
                </span>
                <div class="mt-1">
                    <span v-if="usage?.usableRequests !== undefined" class="text-xl font-bold">
                        {{ usage.usableRequests }}
                    </span>
                    <span v-else class="text-xl font-bold">
                        {{ translate('common.loading') }}
                    </span>
                </div>
            </div>
            <div class="border-secondary flex flex-col rounded-md border p-3">
                <span class="text-secondary-content text-xs">
                    {{ translate('settings.services.credits') }}
                </span>
                <div class="mt-1">
                    <span v-if="usage?.credits !== undefined" class="text-xl font-bold">
                        {{ formatCredits(usage.credits) }}
                    </span>
                    <span v-else class="text-xl font-bold">
                        {{ translate('common.loading') }}
                    </span>
                </div>
            </div>
        </div>

        <div v-if="usage" class="mt-4 text-xs">
            <p class="text-secondary-content/70">
                {{ translate('settings.services.lastSynced') }}: {{ lastSyncedDisplay }}
            </p>
        </div>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import services from '@/services'
import type { CrofAiUsageSnapshot } from '@/ts/models'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

const usage = ref<CrofAiUsageSnapshot | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)

onMounted(() => {
    loadUsage()
})

const lastSyncedDisplay = computed(() => {
    if (!usage.value) return translate('common.loading')
    const date = new Date(usage.value.lastSyncedUtc)
    return date.toLocaleString()
})

const formatCredits = (credits: number): string => {
    return new Intl.NumberFormat(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 4
    }).format(credits)
}

const loadUsage = async (forceRefresh = false) => {
    loading.value = true
    errorMessage.value = null
    try {
        usage.value = await services.crofAi.getUsage<CrofAiUsageSnapshot>(forceRefresh)
        if (!usage.value?.hasApiKey) {
            errorMessage.value = translate('settings.services.crofaiMissingApiKey')
        } else if (usage.value?.message) {
            errorMessage.value = usage.value.message
        }
    } catch (error) {
        console.error('Failed to load CrofAI usage', error)
        errorMessage.value = translate('settings.services.usageLoadError')
    } finally {
        loading.value = false
    }
}
</script>

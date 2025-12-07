<template>
    <div class="border-border mt-6 flex flex-col rounded-md border p-4">
        <div class="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
            <div>
                <p class="font-semibold">
                    {{ translate('settings.services.chutesUsageTitle') }}
                </p>
                <p class="text-sm text-gray-400">
                    <span v-if="usage?.plan">
                        {{ translate('settings.services.chutesPlan', { plan: usage.plan }) }}
                    </span>
                    <span v-else>
                        {{ translate('settings.services.chutesPlanUnknown') }}
                    </span>
                </p>
            </div>
            <div class="flex gap-2">
                <button
                    class="bg-primary text-primary-content rounded px-3 py-1 text-sm"
                    :class="{ 'opacity-50': loading }"
                    :disabled="loading"
                    @click="loadUsage(true)">
                    {{ translate('settings.services.refreshUsage') }}
                </button>
            </div>
        </div>

        <div v-if="errorMessage" class="mt-3 rounded-md bg-red-500/10 p-3 text-sm text-red-300">
            {{ errorMessage }}
        </div>

        <div v-else class="mt-4">
            <div class="flex justify-between text-sm font-semibold">
                <span>
                    {{ translate('settings.services.requestsToday') }}
                </span>
                <span>
                    {{
                        usage
                            ? `${usage.requestsUsed} / ${
                                  usage.allowedRequestsPerDay > 0
                                      ? usage.allowedRequestsPerDay
                                      : translate('settings.services.unlimited')
                              }`
                            : translate('common.loading')
                    }}
                </span>
            </div>
            <div class="bg-border relative mt-2 h-2 overflow-hidden rounded-full">
                <div
                    class="bg-primary absolute left-0 top-0 h-full transition-all"
                    :style="{ width: progress + '%' }"></div>
            </div>
            <div class="mt-3 grid gap-2 text-sm md:grid-cols-2">
                <div>
                    <p class="text-gray-400">
                        {{ translate('settings.services.overrideLimitLabel') }}
                    </p>
                    <p class="font-semibold">
                        {{
                            usage?.overrideRequestsPerDay
                                ? usage.overrideRequestsPerDay
                                : translate('settings.services.noOverride')
                        }}
                    </p>
                </div>
                <div>
                    <p class="text-gray-400">
                        {{ translate('settings.services.lastSynced') }}
                    </p>
                    <p class="font-semibold">
                        {{ lastSyncedDisplay }}
                    </p>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import services from '@/services'
import { ChutesUsageSnapshot } from '@/ts'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

const usage = ref<ChutesUsageSnapshot | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)

const progress = computed(() => {
    if (!usage.value || usage.value.allowedRequestsPerDay <= 0) {
        return 0
    }
    const ratio = usage.value.requestsUsed / usage.value.allowedRequestsPerDay
    return Math.min(Math.max(ratio * 100, 0), 100)
})

const lastSyncedDisplay = computed(() => {
    if (!usage.value) return translate('common.loading')
    const date = new Date(usage.value.lastSyncedUtc)
    return date.toLocaleString()
})

const loadUsage = async (forceRefresh = false) => {
    loading.value = true
    errorMessage.value = null
    try {
        usage.value = await services.chutes.getUsage<ChutesUsageSnapshot>(forceRefresh)
        if (!usage.value?.hasApiKey) {
            errorMessage.value = translate('settings.services.chutesMissingApiKey')
        } else if (usage.value?.message) {
            errorMessage.value = usage.value.message
        }
    } catch (error) {
        console.error('Failed to load Chutes usage', error)
        errorMessage.value = translate('settings.services.usageLoadError')
    } finally {
        loading.value = false
    }
}

onMounted(() => {
    loadUsage()
})
</script>

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
                              }${
                                  usage.overrideRequestsPerDay
                                      ? ` (${translate('settings.services.overrideLimitLabel')})`
                                      : ''
                              }`
                            : translate('common.loading')
                    }}
                </span>
            </div>
            <div class="bg-border relative mt-2 h-2 overflow-hidden rounded-full">
                <div
                    class="bg-primary absolute top-0 left-0 h-full transition-all"
                    :style="{ width: progress + '%' }"></div>
            </div>
            <div class="mt-3 grid gap-2 text-sm md:grid-cols-2">
                <div>
                    <InputComponent
                        v-model="limitOverride"
                        validation-type="number"
                        type="number"
                        :label="translate('settings.services.overrideUsageLimit')"
                        :error-message="translate('settings.services.overrideUsageLimitError')"
                        @update:validation="(val) => (limitOverrideIsValid = val)"
                        class="w-full"
                        @blur="saveLimitOverride"
                        @keydown.enter.prevent="saveLimitOverride" />
                </div>
                <div>
                    <InputComponent
                        v-model="requestBuffer"
                        validation-type="number"
                        type="number"
                        placeholder="50"
                        :label="translate('settings.services.chutesRequestBuffer')"
                        :error-message="translate('settings.services.chutesRequestBufferError')"
                        @update:validation="(val) => (requestBufferIsValid = val)"
                        class="w-full"
                        @blur="saveRequestBuffer"
                        @keydown.enter.prevent="saveRequestBuffer" />
                </div>
            </div>
            <div class="mt-2 text-sm">
                <p class="text-gray-400">
                    {{ translate('settings.services.lastSynced') }}
                </p>
                <p class="font-semibold">
                    {{ lastSyncedDisplay }}
                </p>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import services from '@/services'
import { ChutesUsageSnapshot } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import InputComponent from '@/components/common/InputComponent.vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS } from '@/ts'

const { translate } = useI18n()
const settingsStore = useSettingStore()

const usage = ref<ChutesUsageSnapshot | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)
const limitOverrideIsValid = ref(true)
const limitOverride = ref('')
const requestBufferIsValid = ref(true)
const requestBuffer = ref('')

onMounted(() => {
    loadUsage()
    limitOverride.value =
        (settingsStore.getSetting(SETTINGS.CHUTES_USAGE_LIMIT_OVERRIDE) as string) || ''
    requestBuffer.value =
        (settingsStore.getSetting(SETTINGS.CHUTES_REQUEST_BUFFER) as string) || '50'
})

const saveLimitOverride = async () => {
    if (limitOverrideIsValid.value) {
        await settingsStore.updateSetting(
            SETTINGS.CHUTES_USAGE_LIMIT_OVERRIDE,
            limitOverride.value,
            limitOverrideIsValid.value
        )
        loadUsage()
    }
}

const saveRequestBuffer = async () => {
    if (requestBufferIsValid.value) {
        await settingsStore.updateSetting(
            SETTINGS.CHUTES_REQUEST_BUFFER,
            requestBuffer.value,
            requestBufferIsValid.value
        )
    }
}

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
</script>

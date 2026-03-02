<template>
    <div class="border-secondary bg-primary mt-6 flex flex-col rounded-md border p-4 shadow-sm">
        <div class="mb-4 flex items-center justify-between">
            <span class="text-sm font-medium">
                {{ translate('settings.chutes.mode.label') }}
            </span>
            <div class="flex rounded-lg bg-secondary p-1">
                <button
                    class="rounded-md px-3 py-1 text-sm transition-colors"
                    :class="chutesMode === 'subscription' ? 'bg-accent text-primary-content' : 'text-secondary-content'"
                    @click="setMode('subscription')">
                    {{ translate('settings.chutes.mode.subscription') }}
                </button>
                <button
                    class="rounded-md px-3 py-1 text-sm transition-colors"
                    :class="chutesMode === 'payg' ? 'bg-accent text-primary-content' : 'text-secondary-content'"
                    @click="setMode('payg')">
                    {{ translate('settings.chutes.mode.payg') }}
                </button>
            </div>
        </div>

        <template v-if="chutesMode === 'subscription'">
            <div class="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
                <div>
                    <p class="font-semibold">
                        {{ translate('settings.services.chutesUsageTitle') }}
                    </p>
                    <p class="text-sm text-secondary-content/80">
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
        </template>

        <template v-else>
            <div class="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
                <div>
                    <p class="font-semibold">
                        {{ translate('settings.tokenUsage.title') }}
                    </p>
                    <p class="text-sm text-secondary-content/80">
                        {{ translate('settings.tokenUsage.description') }}
                    </p>
                </div>
            <button
                class="bg-primary text-primary-content rounded px-3 py-1 text-sm"
                :class="{ 'opacity-50': tokenUsageLoading }"
                :disabled="tokenUsageLoading"
                @click="loadTokenUsage">
                {{ translate('settings.services.refreshUsage') }}
            </button>
            </div>
        </template>

        <template v-if="chutesMode === 'subscription'">
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
                <div class="bg-secondary-content/20 relative mt-2 h-2 overflow-hidden rounded-full">
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
                    <p class="text-secondary-content/80">
                        {{ translate('settings.services.lastSynced') }}
                    </p>
                    <p class="font-semibold">
                        {{ lastSyncedDisplay }}
                    </p>
                </div>
            </div>
        </template>

        <template v-else>
            <div v-if="tokenUsage" class="mt-4">
                <div class="flex justify-between text-sm font-semibold">
                    <span>{{ translate('settings.tokenUsage.outputTokensToday') }}</span>
                    <span>
                        {{ formatNumber(tokenUsage.tokensUsedToday) }} / 
                        {{ tokenUsage.tokenLimit ? formatNumber(tokenUsage.tokenLimit) : translate('settings.services.unlimited') }}
                    </span>
                </div>
                
                <div v-if="tokenUsage.tokenLimit" class="bg-secondary-content/20 relative mt-2 h-2 overflow-hidden rounded-full">
                    <div
                        class="bg-accent absolute top-0 left-0 h-full transition-all"
                        :style="{ width: Math.min(tokenUsage.percentUsed, 100) + '%' }"
                        :class="{ 'bg-yellow-500': tokenUsage.percentUsed > 80, 'bg-red-500': tokenUsage.percentUsed >= 100 }">
                    </div>
                </div>

                <div class="mt-4 grid gap-2 text-sm md:grid-cols-2">
                    <div>
                        <InputComponent
                            v-model="tokenLimit"
                            :validation-type="'number'"
                            type="number"
                            :label="translate('settings.tokenUsage.dailyLimit')"
                            :placeholder="translate('settings.tokenUsage.unlimitedPlaceholder')"
                            class="w-full"
                            @blur="saveTokenLimit"
                            @keydown.enter.prevent="saveTokenLimit" />
                    </div>
                    <div>
                        <InputComponent
                            v-model="resetTime"
                            :validation-type="'string'"
                            type="time"
                            :label="translate('settings.tokenUsage.resetTime')"
                            class="w-full"
                            @blur="saveResetTime"
                            @keydown.enter.prevent="saveResetTime" />
                    </div>
                </div>

                <div class="mt-2 flex flex-wrap gap-2">
                    <button
                        v-for="preset in presets"
                        :key="preset.value"
                        class="bg-secondary text-secondary-content rounded px-2 py-1 text-xs transition-colors hover:bg-accent hover:text-primary-content"
                        @click="applyPreset(preset.value)">
                        {{ preset.label }}
                    </button>
                </div>

                <p class="mt-3 text-xs text-secondary-content/70">
                    {{ translate('settings.tokenUsage.pricingWarning') }}
                </p>
            </div>
        </template>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import services from '@/services'
import { ChutesUsageSnapshot } from '@/ts'
import type { TokenUsageResponse } from '@/ts/tokenUsage'
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

const chutesMode = ref<'subscription' | 'payg'>('subscription')
const tokenUsage = ref<TokenUsageResponse | null>(null)
const tokenUsageLoading = ref(false)
const tokenLimit = ref('')
const resetTime = ref('00:00')

const presets = [
    { label: '500K', value: 500000 },
    { label: '5M', value: 5000000 },
    { label: '50M', value: 50000000 }
]

onMounted(async () => {
    await loadChutesMode()
    loadUsage()
    limitOverride.value =
        (settingsStore.getSetting(SETTINGS.CHUTES_USAGE_LIMIT_OVERRIDE) as string) || ''
    requestBuffer.value =
        (settingsStore.getSetting(SETTINGS.CHUTES_REQUEST_BUFFER) as string) || '50'
    
    if (chutesMode.value === 'payg') {
        await loadTokenUsage()
        tokenLimit.value = (settingsStore.getSetting(SETTINGS.CHUTES_TOKEN_LIMIT) as string) || ''
        resetTime.value = (settingsStore.getSetting(SETTINGS.TOKEN_LIMIT_RESET_TIME) as string) || '00:00'
    }
})

const loadChutesMode = async () => {
    try {
        const response = await services.tokenUsage.getChutesMode<{ mode: 'subscription' | 'payg' }>()
        chutesMode.value = response.mode
    } catch (error) {
        console.error('Failed to load Chutes mode', error)
    }
}

const setMode = async (mode: 'subscription' | 'payg') => {
    chutesMode.value = mode
    await services.tokenUsage.setChutesMode(mode)
    if (mode === 'payg') {
        await loadTokenUsage()
        tokenLimit.value = (settingsStore.getSetting(SETTINGS.CHUTES_TOKEN_LIMIT) as string) || ''
        resetTime.value = (settingsStore.getSetting(SETTINGS.TOKEN_LIMIT_RESET_TIME) as string) || '00:00'
    }
}

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

const loadTokenUsage = async () => {
    tokenUsageLoading.value = true
    try {
        tokenUsage.value = await services.tokenUsage.getUsage<TokenUsageResponse>('chutes')
    } catch (error) {
        console.error('Failed to load token usage', error)
    } finally {
        tokenUsageLoading.value = false
    }
}

const saveTokenLimit = async () => {
    await settingsStore.updateSetting(SETTINGS.CHUTES_TOKEN_LIMIT, tokenLimit.value, true)
    await loadTokenUsage()
}

const saveResetTime = async () => {
    await settingsStore.updateSetting(SETTINGS.TOKEN_LIMIT_RESET_TIME, resetTime.value, true)
}

const applyPreset = (value: number) => {
    tokenLimit.value = value.toString()
    saveTokenLimit()
}

const formatNumber = (num: number): string => {
    return new Intl.NumberFormat().format(num)
}
</script>

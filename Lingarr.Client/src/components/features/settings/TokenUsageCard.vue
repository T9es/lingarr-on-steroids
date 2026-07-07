<template>
    <div class="border-secondary bg-primary mt-4 flex flex-col rounded-md border p-4 shadow-sm">
        <div class="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
            <div>
                <p class="font-semibold">
                    {{ translate('settings.tokenUsage.title') }}
                </p>
                <p class="text-secondary-content/80 text-sm">
                    {{ translate('settings.tokenUsage.description') }}
                </p>
            </div>
            <button
                class="bg-primary text-primary-content rounded px-3 py-1 text-sm"
                :class="{ 'opacity-50': loading }"
                :disabled="loading"
                @click="loadUsage">
                {{ translate('settings.services.refreshUsage') }}
            </button>
        </div>

        <div class="mt-4">
            <div v-if="usage" class="flex justify-between text-sm font-semibold">
                <span>{{ translate('settings.tokenUsage.outputTokensToday') }}</span>
                <span>
                    {{ formatNumber(usage.tokensUsedToday) }} /
                    {{
                        usage.tokenLimit
                            ? formatNumber(usage.tokenLimit)
                            : translate('settings.services.unlimited')
                    }}
                </span>
            </div>

            <div
                v-if="usage?.tokenLimit"
                class="bg-secondary-content/20 relative mt-2 h-2 overflow-hidden rounded-full">
                <div
                    class="bg-accent absolute top-0 left-0 h-full transition-all"
                    :style="{ width: Math.min(usage.percentUsed, 100) + '%' }"
                    :class="{
                        'bg-yellow-500': usage.percentUsed > 80,
                        'bg-red-500': usage.percentUsed >= 100
                    }"></div>
            </div>

            <div v-else-if="loading" class="text-secondary-content/70 mt-2 text-sm">
                {{ translate('common.loading') }}
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
                    class="bg-secondary text-secondary-content hover:bg-accent hover:text-primary-content rounded px-2 py-1 text-xs transition-colors"
                    @click="applyPreset(preset.value)">
                    {{ preset.label }}
                </button>
            </div>

            <p class="text-secondary-content/70 mt-3 text-xs">
                {{ translate('settings.tokenUsage.pricingWarning') }}
            </p>
        </div>
    </div>
</template>

<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useI18n } from '@/plugins/i18n'
import { useSettingStore } from '@/store/setting'
import type { ISettings } from '@/ts'
import type { TokenUsageResponse } from '@/ts/tokenUsage'
import InputComponent from '@/components/common/InputComponent.vue'
import services from '@/services'

const { translate } = useI18n()
const settingsStore = useSettingStore()

type TokenUsageService = 'openai' | 'anthropic' | 'gemini' | 'deepseek' | 'localai' | 'crofai'
type TokenLimitSettingKey =
    | 'openai_token_limit'
    | 'anthropic_token_limit'
    | 'gemini_token_limit'
    | 'deepseek_token_limit'
    | 'localai_token_limit'
    | 'crofai_token_limit'

const props = defineProps<{
    service: TokenUsageService
}>()

const usage = ref<TokenUsageResponse | null>(null)
const loading = ref(false)
const tokenLimit = ref('')
const resetTime = ref('00:00')

const presets = [
    { label: '500K', value: 500000 },
    { label: '5M', value: 5000000 },
    { label: '50M', value: 50000000 }
]

const tokenLimitSettingKey = computed<TokenLimitSettingKey>(() => {
    const keyMap: Record<TokenUsageService, TokenLimitSettingKey> = {
        openai: 'openai_token_limit',
        anthropic: 'anthropic_token_limit',
        gemini: 'gemini_token_limit',
        deepseek: 'deepseek_token_limit',
        localai: 'localai_token_limit',
        crofai: 'crofai_token_limit'
    }
    return keyMap[props.service]
})

onMounted(async () => {
    await loadUsage()
    const storedTokenLimit = settingsStore.getSetting(tokenLimitSettingKey.value as keyof ISettings)
    const storedResetTime = settingsStore.getSetting('token_limit_reset_time')

    tokenLimit.value = typeof storedTokenLimit === 'string' ? storedTokenLimit : ''
    resetTime.value = typeof storedResetTime === 'string' ? storedResetTime : '00:00'
})

const loadUsage = async () => {
    loading.value = true
    try {
        usage.value = await services.tokenUsage.getUsage<TokenUsageResponse>(props.service)
    } catch (error) {
        console.error('Failed to load token usage', error)
    } finally {
        loading.value = false
    }
}

const saveTokenLimit = async () => {
    await settingsStore.updateSetting(
        tokenLimitSettingKey.value as keyof ISettings,
        tokenLimit.value,
        true
    )
    await loadUsage()
}

const saveResetTime = async () => {
    await settingsStore.updateSetting('token_limit_reset_time', resetTime.value, true)
}

const applyPreset = (value: number) => {
    tokenLimit.value = value.toString()
    saveTokenLimit()
}

const formatNumber = (num: number): string => {
    return new Intl.NumberFormat().format(num)
}
</script>

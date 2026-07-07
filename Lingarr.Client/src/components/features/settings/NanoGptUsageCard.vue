<template>
    <div class="border-secondary bg-primary mt-6 flex flex-col rounded-md border p-4 shadow-sm">
        <div class="flex flex-col gap-2 md:flex-row md:items-center md:justify-between">
            <div>
                <p class="font-semibold">
                    {{ translate('settings.services.nanoGptUsageTitle') }}
                </p>
                <p class="text-secondary-content/80 text-sm">
                    {{ usageSubtitle }}
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

        <div v-if="errorMessage" class="mt-3 rounded-md bg-red-500/10 p-3 text-sm text-red-300">
            {{ errorMessage }}
        </div>

        <div class="mt-4 grid gap-3 lg:grid-cols-3">
            <UsageWindow
                v-if="hasUsageData(usage?.dailyImages)"
                :title="translate('settings.services.nanoGptDailyImages')"
                :window="usage?.dailyImages" />
            <UsageWindow
                v-if="hasUsageData(usage?.daily)"
                :title="translate('settings.services.nanoGptDailyUnits')"
                :window="usage?.daily"
                :reserve="dailyUnitReserve" />
            <UsageWindow
                v-if="hasUsageData(usage?.monthly)"
                :title="translate('settings.services.nanoGptMonthlyUnits')"
                :window="usage?.monthly"
                :reserve="monthlyUnitReserve" />
            <UsageWindow
                v-if="hasUsageData(usage?.weeklyTokens)"
                :title="translate('settings.services.nanoGptWeeklyTokens')"
                :window="usage?.weeklyTokens"
                :reserve="tokenReserve" />
        </div>

        <div class="mt-4 grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
            <InputComponent
                v-model="weeklyTokenAllowance"
                validation-type="number"
                type="number"
                :label="translate('settings.services.nanoGptWeeklyAllowance')"
                :error-message="translate('settings.services.overrideUsageLimitError')"
                @update:validation="(val) => (weeklyTokenAllowanceIsValid = val)"
                @blur="saveWeeklyTokenAllowance"
                @keydown.enter.prevent="saveWeeklyTokenAllowance" />
            <InputComponent
                v-model="tokenReserve"
                validation-type="number"
                type="number"
                :label="translate('settings.services.nanoGptTokenReserve')"
                :error-message="translate('settings.services.overrideUsageLimitError')"
                @update:validation="(val) => (tokenReserveIsValid = val)"
                @blur="saveTokenReserve"
                @keydown.enter.prevent="saveTokenReserve" />
            <InputComponent
                v-model="dailyUnitReserve"
                validation-type="number"
                type="number"
                :label="translate('settings.services.nanoGptDailyReserve')"
                :error-message="translate('settings.services.overrideUsageLimitError')"
                @update:validation="(val) => (dailyUnitReserveIsValid = val)"
                @blur="saveDailyUnitReserve"
                @keydown.enter.prevent="saveDailyUnitReserve" />
            <InputComponent
                v-model="monthlyUnitReserve"
                validation-type="number"
                type="number"
                :label="translate('settings.services.nanoGptMonthlyReserve')"
                :error-message="translate('settings.services.overrideUsageLimitError')"
                @update:validation="(val) => (monthlyUnitReserveIsValid = val)"
                @blur="saveMonthlyUnitReserve"
                @keydown.enter.prevent="saveMonthlyUnitReserve" />
        </div>

        <div class="mt-2 flex flex-wrap gap-2">
            <button
                class="bg-secondary text-secondary-content hover:bg-accent hover:text-primary-content rounded px-2 py-1 text-xs transition-colors"
                @click="applyWeeklyAllowancePreset">
                {{ translate('settings.services.nanoGptWeeklyAllowancePreset') }}
            </button>
        </div>

        <p class="text-secondary-content/70 mt-3 text-xs">
            {{ translate('settings.services.nanoGptReserveDescription') }}
        </p>
    </div>
</template>

<script setup lang="ts">
import { computed, defineComponent, h, onMounted, ref, type PropType } from 'vue'
import services from '@/services'
import InputComponent from '@/components/common/InputComponent.vue'
import { useI18n } from '@/plugins/i18n'
import { useSettingStore } from '@/store/setting'
import { SETTINGS, type NanoGptUsageSnapshot, type NanoGptUsageWindow } from '@/ts'

const { translate } = useI18n()
const settingsStore = useSettingStore()

const usage = ref<NanoGptUsageSnapshot | null>(null)
const loading = ref(false)
const errorMessage = ref<string | null>(null)
const weeklyTokenAllowance = ref('')
const tokenReserve = ref('')
const dailyUnitReserve = ref('')
const monthlyUnitReserve = ref('')
const weeklyTokenAllowanceIsValid = ref(true)
const tokenReserveIsValid = ref(true)
const dailyUnitReserveIsValid = ref(true)
const monthlyUnitReserveIsValid = ref(true)

const usageSubtitle = computed(() => {
    if (loading.value) {
        return translate('common.loading')
    }

    if (usage.value?.state) {
        return translate('settings.services.nanoGptState', { state: usage.value.state })
    }

    return translate('settings.services.nanoGptUsageDescription')
})

onMounted(async () => {
    weeklyTokenAllowance.value =
        (settingsStore.getSetting(SETTINGS.NANOGPT_WEEKLY_TOKEN_ALLOWANCE) as string) || '60000000'
    tokenReserve.value = (settingsStore.getSetting(SETTINGS.NANOGPT_TOKEN_RESERVE) as string) || '0'
    dailyUnitReserve.value =
        (settingsStore.getSetting(SETTINGS.NANOGPT_DAILY_UNIT_RESERVE) as string) || '0'
    monthlyUnitReserve.value =
        (settingsStore.getSetting(SETTINGS.NANOGPT_MONTHLY_UNIT_RESERVE) as string) || '0'
    await loadUsage()
})

const loadUsage = async (forceRefresh = false) => {
    loading.value = true
    errorMessage.value = null
    try {
        usage.value = await services.nanoGpt.getUsage<NanoGptUsageSnapshot>(forceRefresh)
        if (!usage.value?.hasApiKey) {
            errorMessage.value = translate('settings.services.nanoGptMissingApiKey')
        } else if (usage.value?.message) {
            errorMessage.value = usage.value.message
        }
    } catch (error) {
        console.error('Failed to load NanoGPT usage', error)
        errorMessage.value = translate('settings.services.usageLoadError')
    } finally {
        loading.value = false
    }
}

const saveWeeklyTokenAllowance = async () => {
    if (!weeklyTokenAllowanceIsValid.value) return
    await settingsStore.updateSetting(
        SETTINGS.NANOGPT_WEEKLY_TOKEN_ALLOWANCE,
        weeklyTokenAllowance.value,
        true
    )
    await loadUsage()
}

const saveTokenReserve = async () => {
    if (!tokenReserveIsValid.value) return
    await settingsStore.updateSetting(SETTINGS.NANOGPT_TOKEN_RESERVE, tokenReserve.value, true)
    await loadUsage()
}

const saveDailyUnitReserve = async () => {
    if (!dailyUnitReserveIsValid.value) return
    await settingsStore.updateSetting(
        SETTINGS.NANOGPT_DAILY_UNIT_RESERVE,
        dailyUnitReserve.value,
        true
    )
    await loadUsage()
}

const saveMonthlyUnitReserve = async () => {
    if (!monthlyUnitReserveIsValid.value) return
    await settingsStore.updateSetting(
        SETTINGS.NANOGPT_MONTHLY_UNIT_RESERVE,
        monthlyUnitReserve.value,
        true
    )
    await loadUsage()
}

const applyWeeklyAllowancePreset = () => {
    weeklyTokenAllowance.value = '60000000'
    saveWeeklyTokenAllowance()
}

const formatNumber = (value?: number | null): string => {
    if (value === null || value === undefined) {
        return translate('settings.services.unlimited')
    }

    return new Intl.NumberFormat().format(value)
}

const formatDate = (value?: string | null): string => {
    if (!value) {
        return translate('settings.services.nanoGptResetUnknown')
    }

    return new Date(value).toLocaleString()
}

const getPercent = (window?: NanoGptUsageWindow): number => {
    if (!window) return 0
    if (window.percentUsed > 0) {
        return Math.min(
            window.percentUsed <= 1 ? window.percentUsed * 100 : window.percentUsed,
            100
        )
    }

    if (window.limit && window.limit > 0) {
        return Math.min((window.used / window.limit) * 100, 100)
    }

    return 0
}

const hasUsageData = (window?: NanoGptUsageWindow): boolean => {
    if (!window) return false

    return (
        window.used > 0 ||
        (window.limit !== null && window.limit !== undefined) ||
        (window.remaining !== null && window.remaining !== undefined) ||
        Boolean(window.resetAt)
    )
}

const UsageWindow = defineComponent({
    props: {
        title: {
            type: String,
            required: true
        },
        window: {
            type: Object as PropType<NanoGptUsageWindow | undefined>,
            default: undefined
        },
        reserve: {
            type: String,
            default: ''
        }
    },
    setup(props) {
        return () => {
            const percent = getPercent(props.window)
            return h('div', { class: 'border-secondary/60 bg-tertiary rounded-md border p-3' }, [
                h(
                    'div',
                    { class: 'flex items-center justify-between gap-2 text-sm font-semibold' },
                    [
                        h('span', props.title),
                        h(
                            'span',
                            `${formatNumber(props.window?.used ?? 0)} / ${formatNumber(props.window?.limit)}`
                        )
                    ]
                ),
                h(
                    'div',
                    {
                        class: 'bg-secondary-content/20 relative mt-2 h-2 overflow-hidden rounded-full'
                    },
                    [
                        h('div', {
                            class: [
                                'bg-accent absolute top-0 left-0 h-full transition-all',
                                percent > 80 ? 'bg-yellow-500' : '',
                                percent >= 100 ? 'bg-red-500' : ''
                            ],
                            style: { width: `${percent}%` }
                        })
                    ]
                ),
                h('div', { class: 'text-secondary-content/80 mt-3 space-y-1 text-xs' }, [
                    h(
                        'p',
                        `${translate('settings.services.nanoGptRemaining')}: ${formatNumber(props.window?.remaining)}`
                    ),
                    props.reserve
                        ? h(
                              'p',
                              `${translate('settings.services.nanoGptReserve')}: ${formatNumber(Number(props.reserve || 0))}`
                          )
                        : null,
                    h(
                        'p',
                        `${translate('settings.services.nanoGptReset')}: ${formatDate(props.window?.resetAt)}`
                    )
                ])
            ])
        }
    }
})
</script>

<template>
    <div class="relative">
        <label v-if="label" class="mb-1 block text-sm font-semibold">
            {{ label }}
        </label>
        <div
            ref="excludeClickOutside"
            class="border-accent flex h-12 cursor-pointer items-center justify-between rounded-md border px-4 py-2"
            @click="toggleDropdown">
            <span v-if="!displayLabel" class="text-gray-400">
                {{ translate('settings.automation.translationScheduleLabel') }}
            </span>
            <span v-else class="text-primary-content">{{ displayLabel }}</span>
            <CaretRightIcon
                :class="{ 'rotate-90': isOpen }"
                class="arrow-right h-5 w-5 transition-transform duration-200" />
        </div>
        <ul
            v-show="isOpen"
            ref="clickOutside"
            class="border-accent bg-primary absolute z-10 mt-1 max-h-60 w-full overflow-auto rounded-md border shadow-lg">
            <li
                v-for="option in scheduleOptions"
                :key="option.value"
                class="hover:bg-accent/20 cursor-pointer px-4 py-2"
                :class="{ 'bg-accent/20': isSelected(option.value) }"
                @click="selectOption(option)">
                {{ option.label }}
            </li>
        </ul>

        <!-- Custom cron input (shown when "custom" is selected) -->
        <div v-if="isCustomMode" class="mt-2 space-y-2">
            <InputComponent
                v-model="customCronValue"
                label=""
                :placeholder="'*/15 * * * *'"
                validation-type="cron"
                @update:validation="(val) => (customCronIsValid = val)"
                @update:model-value="handleCustomCronChange" />
            <p class="text-xs text-gray-400">
                Format:
                <code class="bg-accent/30 rounded px-1">minute</code>
                <code class="bg-accent/30 rounded px-1">hour</code>
                <code class="bg-accent/30 rounded px-1">day</code>
                <code class="bg-accent/30 rounded px-1">month</code>
                <code class="bg-accent/30 rounded px-1">weekday</code>
                · Use
                <code class="bg-accent/30 rounded px-1">*</code>
                for "every" ·
                <code class="bg-accent/30 rounded px-1">*/15</code>
                = every 15
            </p>
        </div>
    </div>
</template>

<script setup lang="ts">
import { Ref, ref, computed, watch } from 'vue'
import CaretRightIcon from '@/components/icons/CaretRightIcon.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import useClickOutside from '@/composables/useClickOutside'
import { useI18n } from '@/plugins/i18n'

interface ScheduleOption {
    value: string
    label: string
    cron: string
}

const props = defineProps<{
    label?: string
    modelValue: string
}>()

const emit = defineEmits<{
    (e: 'update:modelValue', value: string): void
    (e: 'update:validation', isValid: boolean): void
}>()

const { translate } = useI18n()

const isOpen: Ref<boolean> = ref(false)
const clickOutside: Ref<HTMLElement | undefined> = ref()
const excludeClickOutside: Ref<HTMLElement | undefined> = ref()
const customCronValue = ref('')
const customCronIsValid = ref(true)
const customModeSelected = ref(false) // Explicitly track custom mode

// Schedule options with their cron expressions
const scheduleOptions = computed<ScheduleOption[]>(() => [
    {
        value: 'every15min',
        label: translate('settings.cronOptions.everyFifteenMinutes'),
        cron: '*/15 * * * *'
    },
    {
        value: 'every30min',
        label: translate('settings.cronOptions.everyThirtyMinutes'),
        cron: '*/30 * * * *'
    },
    { value: 'hourly', label: translate('settings.cronOptions.hourly'), cron: '0 * * * *' },
    {
        value: 'every2hours',
        label: translate('settings.cronOptions.everyTwoHours'),
        cron: '0 */2 * * *'
    },
    {
        value: 'every4hours',
        label: translate('settings.cronOptions.everyFourHours'),
        cron: '0 */4 * * *'
    },
    {
        value: 'every6hours',
        label: translate('settings.cronOptions.everySixHours'),
        cron: '0 */6 * * *'
    },
    {
        value: 'twiceDaily',
        label: translate('settings.cronOptions.twiceADay'),
        cron: '0 */12 * * *'
    },
    {
        value: 'dailyMidnight',
        label: translate('settings.cronOptions.dailyAtMidnight'),
        cron: '0 0 * * *'
    },
    { value: 'dailyFour', label: translate('settings.cronOptions.dailyAtFour'), cron: '0 4 * * *' },
    {
        value: 'weeklySunday',
        label: translate('settings.cronOptions.weeklyOnSundayAtMidnight'),
        cron: '0 0 * * 0'
    },
    { value: 'custom', label: translate('settings.cronOptions.custom'), cron: '' }
])

// Find which preset matches the current cron value
const matchedPreset = computed(() => {
    return scheduleOptions.value.find(
        (opt) => opt.cron === props.modelValue && opt.value !== 'custom'
    )
})

const currentSelection = computed(() => {
    if (customModeSelected.value) return 'custom'
    return matchedPreset.value ? matchedPreset.value.value : 'custom'
})

const isCustomMode = computed(() => currentSelection.value === 'custom')

const displayLabel = computed(() => {
    if (isCustomMode.value && props.modelValue) {
        return `${translate('settings.cronOptions.custom')}: ${props.modelValue}`
    }
    const option = scheduleOptions.value.find((opt) => opt.value === currentSelection.value)
    return option?.label || ''
})

// Initialize custom cron value when in custom mode
watch(
    () => props.modelValue,
    (newValue) => {
        if (isCustomMode.value) {
            customCronValue.value = newValue
        }
        // If value changes to match a preset and we're in custom mode, stay in custom mode
        // (user explicitly chose custom)
    },
    { immediate: true }
)

const toggleDropdown = () => {
    isOpen.value = !isOpen.value
}

const selectOption = (option: ScheduleOption) => {
    isOpen.value = false

    // Emit validation FIRST so parent's validation ref is true when modelValue setter runs
    emit('update:validation', true)

    if (option.value === 'custom') {
        customModeSelected.value = true
        customCronValue.value = props.modelValue || '0 * * * *'
        // Don't emit new value, just show the input
    } else {
        customModeSelected.value = false
        emit('update:modelValue', option.cron)
    }
}

const handleCustomCronChange = (value: string) => {
    if (customCronIsValid.value) {
        emit('update:modelValue', value)
    }
    emit('update:validation', customCronIsValid.value)
}

const isSelected = (optionValue: string) => {
    return currentSelection.value === optionValue
}

useClickOutside(
    clickOutside,
    () => {
        isOpen.value = false
    },
    excludeClickOutside
)
</script>

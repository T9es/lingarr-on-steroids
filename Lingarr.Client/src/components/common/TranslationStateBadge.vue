<template>
    <span
        :class="badgeClasses"
        :title="tooltip"
        class="inline-flex items-center gap-1 rounded px-2 py-0.5 text-xs font-medium">
        <component
            :is="iconComponent"
            class="h-3 w-3"
            :class="{ 'animate-spin': state === TRANSLATION_STATE.IN_PROGRESS }" />
        <span v-if="showLabel">{{ label }}</span>
    </span>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { TRANSLATION_STATE, TranslationStateType } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import CheckMarkCicleIcon from '@/components/icons/CheckMarkCicleIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import ClockIcon from '@/components/icons/ClockIcon.vue'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import QuestionMarkCircleIcon from '@/components/icons/QuestionMarkCircleIcon.vue'
import TimesCircleIcon from '@/components/icons/TimesCircleIcon.vue'

const { translate } = useI18n()

const props = withDefaults(
    defineProps<{
        state: TranslationStateType
        showLabel?: boolean
    }>(),
    {
        showLabel: false
    }
)

const badgeClasses = computed(() => {
    switch (props.state) {
        case TRANSLATION_STATE.COMPLETE:
            return 'bg-green-900/50 text-green-300 border border-green-500/50'
        case TRANSLATION_STATE.IN_PROGRESS:
            return 'bg-amber-900/50 text-amber-300 border border-amber-500/50'
        case TRANSLATION_STATE.PENDING:
            return 'bg-orange-900/50 text-orange-300 border border-orange-500/50'
        case TRANSLATION_STATE.STALE:
            return 'bg-gray-700/50 text-gray-300 border border-gray-500/50'
        case TRANSLATION_STATE.NOT_APPLICABLE:
            return 'bg-gray-800/30 text-gray-500 border border-gray-700/30 opacity-50'
        case TRANSLATION_STATE.UNKNOWN:
        default:
            return 'bg-gray-800/50 text-gray-400 border border-gray-600/50 opacity-60'
    }
})

const iconComponent = computed(() => {
    switch (props.state) {
        case TRANSLATION_STATE.COMPLETE:
            return CheckMarkCicleIcon
        case TRANSLATION_STATE.IN_PROGRESS:
            return LoaderCircleIcon
        case TRANSLATION_STATE.PENDING:
            return ClockIcon
        case TRANSLATION_STATE.STALE:
            return RefreshIcon
        case TRANSLATION_STATE.NOT_APPLICABLE:
            return TimesCircleIcon
        case TRANSLATION_STATE.UNKNOWN:
        default:
            return QuestionMarkCircleIcon
    }
})

const label = computed(() => {
    switch (props.state) {
        case TRANSLATION_STATE.COMPLETE:
            return translate('translationState.complete')
        case TRANSLATION_STATE.IN_PROGRESS:
            return translate('translationState.inProgress')
        case TRANSLATION_STATE.PENDING:
            return translate('translationState.pending')
        case TRANSLATION_STATE.STALE:
            return translate('translationState.stale')
        case TRANSLATION_STATE.NOT_APPLICABLE:
            return translate('translationState.notApplicable')
        case TRANSLATION_STATE.UNKNOWN:
        default:
            return translate('translationState.unknown')
    }
})

const tooltip = computed(() => {
    switch (props.state) {
        case TRANSLATION_STATE.COMPLETE:
            return translate('translationState.tooltipComplete')
        case TRANSLATION_STATE.IN_PROGRESS:
            return translate('translationState.tooltipInProgress')
        case TRANSLATION_STATE.PENDING:
            return translate('translationState.tooltipPending')
        case TRANSLATION_STATE.STALE:
            return translate('translationState.tooltipStale')
        case TRANSLATION_STATE.NOT_APPLICABLE:
            return translate('translationState.tooltipNotApplicable')
        case TRANSLATION_STATE.UNKNOWN:
        default:
            return translate('translationState.tooltipUnknown')
    }
})
</script>

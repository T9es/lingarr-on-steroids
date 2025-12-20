<template>
    <div v-if="loading" class="md:px-4">
        <LoaderCircleIcon class="h-5 w-5 animate-spin" />
    </div>
    <div v-else-if="cancelable">
        <button
            type="button"
            :disabled="loading"
            class="focus-visible:ring-accent cursor-pointer rounded-md focus-visible:ring-2 focus-visible:outline-none md:px-4"
            :aria-label="translate('translations.cancel')"
            :title="translate('translations.cancel')"
            @click="executeAction(TRANSLATION_ACTIONS.CANCEL)">
            <TimesIcon class="h-5 w-5" />
        </button>
    </div>
    <div v-else-if="removable" class="md:space-x-2">
        <button
            type="button"
            :disabled="loading"
            class="focus-visible:ring-accent cursor-pointer rounded-md focus-visible:ring-2 focus-visible:outline-none"
            :aria-label="translate('common.retry')"
            :title="translate('common.retry')"
            @click="executeAction(TRANSLATION_ACTIONS.RETRY)">
            <RetryIcon class="h-5 w-5" />
        </button>
        <button
            type="button"
            :disabled="loading"
            class="focus-visible:ring-accent cursor-pointer rounded-md focus-visible:ring-2 focus-visible:outline-none"
            :aria-label="translate('translations.delete')"
            :title="translate('translations.delete')"
            @click="executeAction(TRANSLATION_ACTIONS.REMOVE)">
            <TrashIcon class="h-5 w-5" />
        </button>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { TRANSLATION_ACTIONS, TRANSLATION_STATUS, TranslationStatus } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import TimesIcon from '@/components/icons/TimesIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import TrashIcon from '@/components/icons/TrashIcon.vue'
import RetryIcon from '@/components/icons/RetryIcon.vue'

const props = defineProps<{
    status: TranslationStatus
    onAction: (action: TRANSLATION_ACTIONS) => Promise<void>
}>()

const { translate } = useI18n()
const loading = ref(false)

const cancelable = computed(
    () =>
        props.status === TRANSLATION_STATUS.INPROGRESS ||
        props.status === TRANSLATION_STATUS.PENDING
)

const removable = computed(() => {
    return (
        props.status == TRANSLATION_STATUS.COMPLETED ||
        props.status == TRANSLATION_STATUS.CANCELLED ||
        props.status == TRANSLATION_STATUS.FAILED ||
        props.status == TRANSLATION_STATUS.INTERRUPTED
    )
})

const executeAction = async (action: TRANSLATION_ACTIONS) => {
    loading.value = true
    await props.onAction(action)

    if (action == TRANSLATION_ACTIONS.RETRY) {
        // Special case to set loading to false when onAction is done
        // Retry is the only case that does not change props.status
        loading.value = false
    }
}

watch(
    () => props.status,
    () => {
        loading.value = false
    }
)
</script>

<template>
    <div class="w-full space-y-4 px-4 py-4">
        <div class="border-accent bg-secondary rounded-md border p-4 shadow-sm">
            <div class="mb-3 flex flex-wrap items-center justify-between gap-2">
                <h2 class="text-sm font-semibold tracking-wide uppercase">
                    {{ translate('translations.tabBlocked') }}
                </h2>
                <div class="flex items-center gap-2">
                    <ReloadComponent :loading="loading" @toggle:update="load" />
                    <span class="text-secondary-content text-xs">
                        {{ pageData.totalCount }} {{ translate('common.items') }}
                    </span>
                </div>
            </div>

            <div class="mb-3 flex flex-wrap gap-1.5">
                <button
                    v-for="option in stateFilterOptions"
                    :key="option.value"
                    class="border-accent cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                    :class="
                        stateFilter === option.value
                            ? 'bg-accent text-primary-content'
                            : 'text-secondary-content hover:bg-accent/20'
                    "
                    @click="setStateFilter(option.value)">
                    {{ option.label }} ({{ option.count }})
                </button>
            </div>

            <div v-if="actionMessage" class="text-secondary-content mb-2 text-xs">
                {{ actionMessage }}
            </div>
            <div v-if="actionError" class="text-error mb-2 text-xs">
                {{ actionError }}
            </div>

            <div v-if="error" class="text-error mb-3 flex items-center gap-2 text-xs">
                <span>{{ error }}</span>
                <button class="cursor-pointer underline" @click="load">
                    {{ translate('common.retry') }}
                </button>
            </div>

            <div
                v-if="loading && !items.length"
                class="text-secondary-content py-4 text-center text-sm">
                {{ translate('common.loading') }}
            </div>
            <div v-else-if="items.length" class="max-h-[32rem] space-y-3 overflow-y-auto pr-1">
                <div
                    v-for="item in items"
                    :key="itemKey(item)"
                    class="border-secondary/40 bg-tertiary flex flex-col gap-2 rounded-md border px-3 py-2 md:grid md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
                    <div class="min-w-0">
                        <div class="flex min-w-0 flex-wrap items-center gap-2">
                            <div class="min-w-0 flex-1 overflow-hidden">
                                <span
                                    class="block min-w-0 truncate font-semibold"
                                    :title="item.title">
                                    {{ item.title }}
                                </span>
                            </div>
                            <div class="flex shrink-0 flex-wrap items-center gap-2">
                                <span class="text-secondary-content text-xs uppercase">
                                    {{ item.mediaType }}
                                </span>
                                <TranslationStateBadge
                                    :state="item.translationState"
                                    show-label />
                            </div>
                        </div>

                        <div
                            class="text-secondary-content mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                            <span>
                                {{ translate('translations.lastEvaluated') }}:
                                <span v-if="item.lastEvaluatedAt">
                                    {{ formatDate(item.lastEvaluatedAt) }}
                                </span>
                                <span v-else>-</span>
                            </span>
                            <template v-if="isOcrBlocked(item)">
                                <span>
                                    {{ translate('translations.streamIndex') }}:
                                    {{ streamLabel(item) }}
                                </span>
                                <span v-if="item.ocrQualityScore != null">
                                    {{ translate('translations.ocrQualityScore') }}:
                                    {{ item.ocrQualityScore }}/100
                                </span>
                                <span v-if="item.ocrMinimumQualityScore != null">
                                    {{ translate('translations.ocrMinimumScore') }}:
                                    {{ item.ocrMinimumQualityScore }}
                                </span>
                            </template>
                        </div>

                        <div
                            v-if="isOcrBlocked(item) && item.ocrIssueSummary"
                            class="text-secondary-content mt-1 text-xs"
                            :title="item.ocrIssueSummary">
                            {{ translate('translations.ocrIssueSummary') }}:
                            {{ item.ocrIssueSummary }}
                        </div>
                        <div
                            v-else-if="isFailed(item) && item.failureMessage"
                            class="text-error mt-1 text-xs"
                            :title="item.failureMessage">
                            {{ translate('translations.failureReason') }}:
                            {{ item.failureMessage }}
                        </div>
                    </div>

                    <div class="flex shrink-0 flex-wrap items-center justify-end gap-2">
                        <button
                            class="border-accent hover:bg-accent cursor-pointer rounded border px-3 py-1 text-xs transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                            :disabled="actionInFlight === itemKey(item)"
                            @click="runPrimaryAction(item)">
                            {{ primaryActionLabel(item) }}
                        </button>
                    </div>
                </div>

                <PaginationComponent
                    v-if="pageData.totalCount > pageData.pageSize"
                    v-model="paginationFilter"
                    :total-count="pageData.totalCount"
                    :page-size="pageData.pageSize" />
            </div>
            <div v-else class="text-secondary-content py-4 text-center text-sm">
                {{ translate('translations.noBlockedMedia') }}
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import {
    BlockedMediaStateFilter,
    IBlockedMediaItem,
    IBlockedMediaPage,
    IFilter,
    normalizeTranslationState,
    TRANSLATION_STATE
} from '@/ts'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import ReloadComponent from '@/components/common/ReloadComponent.vue'
import PaginationComponent from '@/components/common/PaginationComponent.vue'
import TranslationStateBadge from '@/components/common/TranslationStateBadge.vue'

const props = defineProps<{
    active: boolean
}>()

const { translate } = useI18n()

const PAGE_SIZE = 50

const page = ref(1)
const stateFilter = ref<BlockedMediaStateFilter>('all')
const pageData = ref<IBlockedMediaPage>({
    items: [],
    totalCount: 0,
    pageNumber: 1,
    pageSize: PAGE_SIZE,
    countsByState: {}
})
const loading = ref(false)
const error = ref<string | null>(null)
const actionInFlight = ref<string | null>(null)
const actionMessage = ref<string | null>(null)
const actionError = ref<string | null>(null)

const items = computed(() => pageData.value.items)

const paginationFilter = computed<IFilter>({
    get: () => ({
        pageNumber: page.value,
        searchQuery: '',
        sortBy: '',
        isAscending: true
    }),
    set: (value) => {
        page.value = value.pageNumber
        void load()
    }
})

const stateFilterOptions = computed(() => {
    const counts = pageData.value.countsByState ?? {}
    const total = Object.values(counts).reduce((sum, count) => sum + count, 0)
    return [
        {
            value: 'all' as BlockedMediaStateFilter,
            label: translate('translations.blockedStateAll'),
            count: total
        },
        {
            value: 'OcrBlocked' as BlockedMediaStateFilter,
            label: translate('translationState.ocrBlocked'),
            count: counts.OcrBlocked ?? 0
        },
        {
            value: 'Failed' as BlockedMediaStateFilter,
            label: translate('translationState.failed'),
            count: counts.Failed ?? 0
        },
        {
            value: 'Stale' as BlockedMediaStateFilter,
            label: translate('translationState.stale'),
            count: counts.Stale ?? 0
        },
        {
            value: 'AwaitingSource' as BlockedMediaStateFilter,
            label: translate('translationState.awaitingSource'),
            count: counts.AwaitingSource ?? 0
        }
    ]
})

const itemKey = (item: IBlockedMediaItem): string => `${item.mediaType}-${item.mediaId}`

const isOcrBlocked = (item: IBlockedMediaItem): boolean =>
    normalizeTranslationState(item.translationState) === TRANSLATION_STATE.OCR_BLOCKED

const isFailed = (item: IBlockedMediaItem): boolean =>
    normalizeTranslationState(item.translationState) === TRANSLATION_STATE.FAILED

const canRetryOcr = (item: IBlockedMediaItem): boolean =>
    isOcrBlocked(item) && item.streamIndex != null

const streamLabel = (item: IBlockedMediaItem): string =>
    item.streamIndex == null
        ? translate('translationState.unknown')
        : String(item.streamIndex)

const primaryActionLabel = (item: IBlockedMediaItem): string => {
    if (!canRetryOcr(item)) {
        return translate('translations.recheck')
    }

    return item.ocrPassesCurrentThreshold
        ? translate('translations.recheck')
        : translate('translations.retryOcr')
}

const formatDate = (value: string): string => new Date(value).toLocaleString()

const load = async () => {
    loading.value = true
    error.value = null
    try {
        pageData.value = await services.blockedMedia.getBlockedMedia({
            page: page.value,
            pageSize: PAGE_SIZE,
            state: stateFilter.value === 'all' ? undefined : stateFilter.value
        })
        page.value = pageData.value.pageNumber
    } catch (loadError) {
        // eslint-disable-next-line no-console
        console.error('Failed to load blocked media', loadError)
        error.value = translate('translations.loadBlockedMediaError')
    } finally {
        loading.value = false
    }
}

const setStateFilter = (value: BlockedMediaStateFilter) => {
    if (stateFilter.value === value) {
        return
    }

    stateFilter.value = value
    page.value = 1
    actionMessage.value = null
    actionError.value = null
    void load()
}

const runPrimaryAction = async (item: IBlockedMediaItem) => {
    const key = itemKey(item)
    if (actionInFlight.value != null) {
        return
    }

    actionInFlight.value = key
    actionMessage.value = null
    actionError.value = null

    try {
        if (canRetryOcr(item)) {
            // QueueOcr promotes a blocked stream whose score already meets the
            // current threshold without re-running OCR, so the same POST covers
            // both "retry OCR" and "promote after a threshold change".
            const result = await services.subtitle.queueOcr(
                item.mediaType,
                item.mediaId,
                item.streamIndex as number
            )
            actionMessage.value =
                result.status === 'Succeeded'
                    ? translate('translations.recheckQueued')
                    : translate('translations.ocrRetryQueued')
        } else {
            // Explicit POST re-evaluation; GET requests never mutate state.
            await services.blockedMedia.recheck(item.mediaType, item.mediaId)
            actionMessage.value = translate('translations.recheckQueued')
        }

        await load()
    } catch (actionFailure) {
        // eslint-disable-next-line no-console
        console.error('Failed to run blocked media action', actionFailure)
        actionError.value = translate('translations.recheckError')
    } finally {
        actionInFlight.value = null
    }
}

watch(
    () => props.active,
    (active) => {
        if (active) {
            void load()
        }
    }
)

onMounted(() => {
    if (props.active) {
        void load()
    }
})
</script>

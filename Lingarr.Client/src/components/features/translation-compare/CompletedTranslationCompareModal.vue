<template>
    <ModalComponent
        :is-open="isOpen"
        size="xxl"
        :body-scrollable="false"
        body-class="p-0"
        @close="$emit('close')">
        <template #header>
            {{ headerTitle }}
        </template>

        <div v-if="isLoading" class="flex h-[75vh] items-center justify-center">
            <div class="text-secondary-content text-sm">
                {{ t('common.loading', 'Loading compare data...') }}
            </div>
        </div>

        <div v-else-if="errorMessage" class="flex h-[75vh] items-center justify-center">
            <div class="space-y-4 px-6 text-center">
                <p class="text-error text-sm">{{ errorMessage }}</p>
                <button
                    v-if="translationRequestId"
                    @click="loadCompareData(translationRequestId)"
                    class="bg-accent text-primary-content rounded px-3 py-2 text-sm">
                    {{ t('common.retry', 'Retry') }}
                </button>
            </div>
        </div>

        <template v-else-if="compareData">
            <div
                v-if="compareData.isPartialFailure"
                class="mx-4 mt-4 mb-3 rounded border border-yellow-500/30 bg-yellow-500/10 p-3 text-sm text-yellow-300">
                This translation failed with {{ compareData.missingPositions?.length }} untranslated
                position(s). Click any missing line to provide a translation, or click Accept to
                finalize as-is. Untranslated positions will keep the original source text.
            </div>
            <div
                v-if="failedCueDetails.length > 0"
                class="mx-4 mt-3 mb-3 max-h-[30vh] overflow-y-auto rounded border border-yellow-500/30 bg-yellow-500/5 p-3">
                <div class="mb-2 flex flex-wrap items-baseline justify-between gap-2">
                    <h3 class="text-sm font-medium text-yellow-300">
                        {{
                            t(
                                'translationTest.failedCues.title',
                                'Rejected translation text is available'
                            )
                        }}
                    </h3>
                    <span class="text-secondary-content text-xs">
                        {{
                            t(
                                'translationTest.failedCues.hint',
                                'The language check rejected the provider text below. The matching lines are highlighted in the table. Use rejected text only for the lines you approve.'
                            )
                        }}
                    </span>
                </div>
                <ul class="space-y-2">
                    <li
                        v-for="entry in failedCueDetails"
                        :key="entry.position"
                        class="bg-primary/40 rounded border border-yellow-500/20 p-2 text-xs">
                        <div class="flex flex-wrap items-start gap-2">
                            <span
                                class="shrink-0 rounded bg-yellow-500/15 px-2 py-0.5 text-[11px] font-medium text-yellow-300">
                                {{ t('translationTest.failedCues.line', 'Line') }}
                                {{ entry.position }}
                            </span>
                            <div class="min-w-0 flex-1 space-y-1">
                                <p v-if="entry.sourceTextDisplay" class="text-secondary-content">
                                    <span class="text-secondary-content/60">
                                        {{ t('translations.source', 'Source') }}:
                                    </span>
                                    <span
                                        class="text-primary-content break-words whitespace-pre-wrap">
                                        {{ entry.sourceTextDisplay }}
                                    </span>
                                </p>
                                <p
                                    v-if="entry.rejectedTextDisplay"
                                    class="break-words whitespace-pre-wrap">
                                    <span class="text-secondary-content/60">
                                        {{
                                            t(
                                                'translationTest.failedCues.rejected',
                                                'Rejected by the language check'
                                            )
                                        }}:
                                    </span>
                                    <span class="text-yellow-200">
                                        {{ entry.rejectedTextDisplay }}
                                    </span>
                                </p>
                                <p v-else class="text-secondary-content/60 italic">
                                    {{
                                        t(
                                            'translationTest.failedCues.noRejectedText',
                                            'No rejected provider text was recorded for this line.'
                                        )
                                    }}
                                </p>
                                <p
                                    v-if="entry.reasonLabel"
                                    class="text-secondary-content break-words">
                                    <span class="text-secondary-content/60">
                                        {{ t('translations.failureReason', 'Failure reason') }}:
                                    </span>
                                    {{ entry.reasonLabel }}
                                </p>
                            </div>
                            <button
                                v-if="entry.rejectedText"
                                type="button"
                                :disabled="isSaving || isRejectedTextApplied(entry)"
                                @click="applyRejectedText(entry)"
                                class="bg-accent text-primary-content hover:bg-accent/80 shrink-0 rounded px-3 py-1 text-xs font-medium transition-colors disabled:opacity-60">
                                {{
                                    isRejectedTextApplied(entry)
                                        ? t('translationTest.failedCues.selected', 'Selected')
                                        : t(
                                              'translationTest.failedCues.useRejected',
                                              'Use rejected text'
                                          )
                                }}
                            </button>
                        </div>
                        <p
                            v-if="isRejectedTextApplied(entry)"
                            class="text-secondary-content/70 mt-1 text-[11px]">
                            {{
                                t(
                                    'translationTest.failedCues.appliedHint',
                                    'This text is queued for this line. Save edits or accept the translation to use it.'
                                )
                            }}
                        </p>
                    </li>
                </ul>
            </div>
            <div class="border-secondary/30 bg-primary/40 border-b px-4 py-2 text-xs">
                <span class="text-secondary-content">
                    {{ compareData.sourceLanguage }} -> {{ compareData.targetLanguage }}
                </span>
                <span class="text-secondary-content/70 ml-3">
                    {{ compareData.lines.length }} {{ t('translationTest.lines', 'lines') }}
                </span>
            </div>
            <TranslationCompareTable
                :lines="compareData.lines"
                :editable="true"
                :missing-positions="compareData.missingPositions ?? []"
                @edit-line="handleEditLine" />
        </template>

        <div v-else class="flex h-[75vh] items-center justify-center">
            <p class="text-secondary-content text-sm">
                {{ t('translationTest.noResults', 'No compare data available.') }}
            </p>
        </div>

        <template #footer>
            <div v-if="saveSuccess" class="mr-auto text-sm text-green-400">
                {{ t('common.saved', 'Saved!') }}
            </div>
            <button
                v-if="translationRequestId"
                :disabled="isLoading"
                @click="loadCompareData(translationRequestId)"
                class="text-primary-content/60 hover:text-primary-content disabled:text-primary-content/30 rounded px-4 py-2">
                {{ translate('common.refresh') }}
            </button>
            <button
                v-if="editedLines.size > 0"
                @click="handleSaveEdits"
                :disabled="isSaving"
                class="bg-accent text-primary-content hover:bg-accent/80 rounded px-4 py-2 text-sm font-medium transition-colors disabled:opacity-50">
                {{ t('common.saveEdits', 'Save Edits') }}
            </button>
            <button
                v-if="allowAccept && compareData?.canAccept === true"
                @click="handleAcceptTranslation"
                :disabled="isSaving"
                class="bg-accent text-primary-content hover:bg-accent/80 rounded px-4 py-2 text-sm font-medium transition-colors disabled:opacity-50">
                {{ t('translations.acceptTranslation', 'Accept Translation') }}
            </button>
            <button
                @click="$emit('close')"
                class="text-primary-content/60 hover:text-primary-content rounded px-4 py-2">
                {{ translate('common.close') }}
            </button>
        </template>
    </ModalComponent>
</template>

<script setup lang="ts">
import type { AxiosError } from 'axios'
import { computed, ref, shallowRef, watch } from 'vue'
import { useI18n } from '@/plugins/i18n'
import ModalComponent from '@/components/common/ModalComponent.vue'
import TranslationCompareTable from '@/components/features/translation-compare/TranslationCompareTable.vue'
import { translationCompareService } from '@/services/translationCompareService'
import type { CompletedTranslationCompareResponse } from '@/ts/translationCompare'

const props = withDefaults(
    defineProps<{
        isOpen: boolean
        translationRequestId: number | null
        allowAccept?: boolean
    }>(),
    {
        allowAccept: true
    }
)

const emit = defineEmits<{
    close: []
    accepted: []
}>()

const { translate } = useI18n()

const compareData = shallowRef<CompletedTranslationCompareResponse | null>(null)
const isLoading = ref(false)
const errorMessage = ref<string | null>(null)
const editedLines = ref<Map<number, string>>(new Map())
const isSaving = ref(false)
const saveSuccess = ref(false)

const MAX_DISPLAY_TEXT_LENGTH = 400
const MAX_REASON_TEXT_LENGTH = 240

interface FailedCueDisplayEntry {
    position: number
    sourceTextDisplay: string
    rejectedText: string | null
    rejectedTextDisplay: string | null
    reasonLabel: string | null
}

interface RejectionReasonPattern {
    pattern: RegExp
    key: string
    fallback: string
}

const rejectionReasonPatterns: RejectionReasonPattern[] = [
    {
        pattern:
            /\becho(?:ed|ing)?\b|source[-_ ]?echo|source text (?:unchanged|identical)|unchanged source/i,
        key: 'translationTest.failedCues.reasons.echo',
        fallback: 'The provider returned the source text unchanged (echo).'
    },
    {
        pattern:
            /wrong[-_ ]?target[-_ ]?language|target[-_ ]?language|language[-_ ]?mismatch|wrong[-_ ]?language|language[-_ ]?check|mismatch/i,
        key: 'translationTest.failedCues.reasons.wrongLanguage',
        fallback: 'The translated text does not match the target language.'
    },
    {
        pattern: /mojibake|garbled|encoding|utf-?8|replacement character|wrong[-_ ]?encoding/i,
        key: 'translationTest.failedCues.reasons.encoding',
        fallback: 'The translated text looked like garbled text (encoding check).'
    },
    {
        pattern: /\bempty\b|\bblank\b|no[-_ ]?translation|no text/i,
        key: 'translationTest.failedCues.reasons.empty',
        fallback: 'The provider returned an empty translation.'
    },
    {
        pattern: /\binvalid\b|\bmalformed\b|\bparse\b|format|structure/i,
        key: 'translationTest.failedCues.reasons.invalid',
        fallback: 'The translated text failed a format or structure check.'
    }
]

const headerTitle = computed(() => {
    if (compareData.value) {
        return `${compareData.value.title} (${compareData.value.sourceLanguage} -> ${compareData.value.targetLanguage})`
    }

    return t('translationTest.debugPanel.compareView', 'Side-by-Side Compare')
})

const failedCueDetails = computed<FailedCueDisplayEntry[]>(() => {
    const data = compareData.value
    const details = data?.failedCueDetails
    if (!data || !Array.isArray(details) || details.length === 0) {
        return []
    }

    const missingPositions = new Set<number>()
    for (const position of data.missingPositions ?? []) {
        if (typeof position === 'number' && Number.isFinite(position)) {
            missingPositions.add(position)
        }
    }
    for (const line of data.lines ?? []) {
        if (line.isMissing && typeof line.position === 'number') {
            missingPositions.add(line.position)
        }
    }

    // Prefer entries that match a flagged missing position. A partial backend response may
    // omit the position list, so fall back to showing every reported rejection.
    const restrictToMissing = missingPositions.size > 0
    const entriesByPosition = new Map<number, FailedCueDisplayEntry>()

    for (const detail of details) {
        if (!detail || typeof detail.position !== 'number' || !Number.isFinite(detail.position)) {
            continue
        }
        if (restrictToMissing && !missingPositions.has(detail.position)) {
            continue
        }

        entriesByPosition.set(detail.position, {
            position: detail.position,
            sourceTextDisplay: toSafeDisplayText(detail.sourceText),
            rejectedText: normalizeRejectedText(detail.rejectedText),
            rejectedTextDisplay: toSafeDisplayText(detail.rejectedText),
            reasonLabel: formatRejectionReason(detail.rejectionReason)
        })
    }

    return Array.from(entriesByPosition.values()).sort(
        (left, right) => left.position - right.position
    )
})

watch(
    () => ({
        isOpen: props.isOpen,
        translationRequestId: props.translationRequestId
    }),
    async ({ isOpen, translationRequestId }) => {
        if (!isOpen) {
            compareData.value = null
            errorMessage.value = null
            editedLines.value.clear()
            saveSuccess.value = false
            return
        }

        if (!translationRequestId) {
            compareData.value = null
            errorMessage.value = t('common.error', 'Missing translation request identifier.')
            return
        }

        await loadCompareData(translationRequestId)
    },
    { immediate: true }
)

async function loadCompareData(translationRequestId: number) {
    isLoading.value = true
    errorMessage.value = null

    try {
        compareData.value =
            await translationCompareService.getCompletedTranslationCompare(translationRequestId)
    } catch (error) {
        compareData.value = null
        const axiosError = error as AxiosError<{ message?: string }>
        errorMessage.value =
            axiosError.response?.data?.message ??
            t('translationTest.errors.loadHistoryFailed', 'Failed to load compare data.')
    } finally {
        isLoading.value = false
    }
}

function handleEditLine(payload: { position: number; translatedText: string }) {
    editedLines.value.set(payload.position, payload.translatedText)
    saveSuccess.value = false
}

function applyRejectedText(entry: FailedCueDisplayEntry) {
    if (!entry.rejectedText) {
        return
    }

    // Reuse the same edit state as manual editing so the rejected text is only sent for the
    // position the user explicitly approved.
    handleEditLine({ position: entry.position, translatedText: entry.rejectedText })
}

function isRejectedTextApplied(entry: FailedCueDisplayEntry): boolean {
    return (
        entry.rejectedText !== null && editedLines.value.get(entry.position) === entry.rejectedText
    )
}

function normalizeRejectedText(value: string | null | undefined): string | null {
    if (typeof value !== 'string' || value.trim().length === 0) {
        return null
    }

    return value
}

function toSafeDisplayText(
    value: string | null | undefined,
    maxLength = MAX_DISPLAY_TEXT_LENGTH
): string {
    if (typeof value !== 'string') {
        return ''
    }

    const normalized = value
        .replace(/\r\n?/g, '\n')
        .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/g, ' ')
        .trim()

    if (normalized.length <= maxLength) {
        return normalized
    }

    return `${normalized.slice(0, maxLength).trimEnd()}...`
}

function formatRejectionReason(reason: string | null | undefined): string | null {
    if (typeof reason !== 'string') {
        return null
    }

    const normalized = reason
        .replace(/[\u0000-\u001f\u007f]/g, ' ')
        .replace(/\s+/g, ' ')
        .trim()

    if (!normalized) {
        return null
    }

    for (const entry of rejectionReasonPatterns) {
        if (entry.pattern.test(normalized)) {
            return t(entry.key, entry.fallback)
        }
    }

    // Never surface raw exception or stack trace text in the UI.
    if (looksLikeExceptionDump(normalized)) {
        return t(
            'translationTest.failedCues.reasons.generic',
            'The language check rejected this translation.'
        )
    }

    if (normalized.length <= MAX_REASON_TEXT_LENGTH) {
        return normalized
    }

    return `${normalized.slice(0, MAX_REASON_TEXT_LENGTH).trimEnd()}...`
}

function looksLikeExceptionDump(value: string): boolean {
    return (
        /(?:^|\s)at\s+[A-Za-z_][A-Za-z0-9_.<>]*\s*\(/.test(value) ||
        /\b(?:[A-Za-z_][A-Za-z0-9_.]*)?(?:Exception|Error)\b/.test(value) ||
        /stack trace/i.test(value)
    )
}

async function handleSaveEdits() {
    if (!props.translationRequestId || editedLines.value.size === 0) return
    const sourceFingerprint = compareData.value?.sourceFingerprint
    if (!sourceFingerprint) {
        errorMessage.value = t(
            'translationTest.errors.sourceFingerprintMissing',
            'Compare data is out of date. Reload before saving edits.'
        )
        return
    }

    isSaving.value = true
    saveSuccess.value = false
    try {
        const edits = Array.from(editedLines.value.entries()).map(([position, translatedText]) => ({
            position,
            translatedText
        }))
        const result = await translationCompareService.saveEdits(
            props.translationRequestId,
            edits,
            sourceFingerprint
        )
        compareData.value = result
        editedLines.value.clear()
        saveSuccess.value = true
        setTimeout(() => {
            saveSuccess.value = false
        }, 2000)
    } catch (error) {
        errorMessage.value = getRequestErrorMessage(
            error,
            t('translationTest.errors.saveFailed', 'Failed to save edits.')
        )
    } finally {
        isSaving.value = false
    }
}

async function handleAcceptTranslation() {
    if (!props.translationRequestId) return
    const sourceFingerprint = compareData.value?.sourceFingerprint
    if (!sourceFingerprint) {
        errorMessage.value = t(
            'translationTest.errors.sourceFingerprintMissing',
            'Compare data is out of date. Reload before accepting the translation.'
        )
        return
    }

    isSaving.value = true
    try {
        const edits =
            editedLines.value.size > 0
                ? Array.from(editedLines.value.entries()).map(([position, translatedText]) => ({
                      position,
                      translatedText
                  }))
                : undefined
        const result = await translationCompareService.acceptTranslation(
            props.translationRequestId,
            edits,
            sourceFingerprint
        )
        compareData.value = result
        editedLines.value.clear()
        emit('accepted')
    } catch (error) {
        errorMessage.value = getRequestErrorMessage(
            error,
            t('translationTest.errors.acceptFailed', 'Failed to accept translation.')
        )
    } finally {
        isSaving.value = false
    }
}

function getRequestErrorMessage(error: unknown, fallback: string): string {
    const axiosError = error as AxiosError<{ message?: string }>
    if (axiosError.response?.status === 409) {
        compareData.value = null
        editedLines.value.clear()
        return (
            axiosError.response.data?.message ??
            t(
                'translationTest.errors.sourceChanged',
                'The source subtitle changed. Reload the compare data before retrying.'
            )
        )
    }

    return axiosError.response?.data?.message ?? fallback
}

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}
</script>

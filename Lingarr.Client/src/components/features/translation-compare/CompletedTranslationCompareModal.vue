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
            <div v-if="compareData.isPartialFailure" class="border-yellow-500/30 bg-yellow-500/10 border rounded p-3 text-sm text-yellow-300 mb-3 mx-4 mt-4">
                This translation failed with {{ compareData.missingPositions?.length }} untranslated position(s). 
                Review the missing lines, edit if needed, then Accept to finalize.
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
                @edit-line="handleEditLine" />
        </template>

        <div v-else class="flex h-[75vh] items-center justify-center">
            <p class="text-secondary-content text-sm">
                {{ t('translationTest.noResults', 'No compare data available.') }}
            </p>
        </div>

        <template #footer>
            <div v-if="saveSuccess" class="text-green-400 text-sm mr-auto">
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
                class="bg-accent text-primary-content hover:bg-accent/80 disabled:opacity-50 rounded px-4 py-2 text-sm font-medium transition-colors">
                {{ t('common.saveEdits', 'Save Edits') }}
            </button>
            <button
                v-if="allowAccept && compareData?.isPartialFailure"
                @click="handleAcceptTranslation"
                :disabled="isSaving"
                class="bg-accent text-primary-content hover:bg-accent/80 disabled:opacity-50 rounded px-4 py-2 text-sm font-medium transition-colors">
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

const props = withDefaults(defineProps<{
    isOpen: boolean
    translationRequestId: number | null
    allowAccept?: boolean
}>(), {
    allowAccept: true
})

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

const headerTitle = computed(() => {
    if (compareData.value) {
        return `${compareData.value.title} (${compareData.value.sourceLanguage} -> ${compareData.value.targetLanguage})`
    }

    return t('translationTest.debugPanel.compareView', 'Side-by-Side Compare')
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

async function handleSaveEdits() {
    if (!props.translationRequestId || editedLines.value.size === 0) return

    isSaving.value = true
    saveSuccess.value = false
    try {
        const edits = Array.from(editedLines.value.entries()).map(([position, translatedText]) => ({
            position,
            translatedText
        }))
        const result = await translationCompareService.saveEdits(props.translationRequestId, edits)
        compareData.value = result
        editedLines.value.clear()
        saveSuccess.value = true
        setTimeout(() => {
            saveSuccess.value = false
        }, 2000)
    } catch (error) {
        errorMessage.value = t('translationTest.errors.saveFailed', 'Failed to save edits.')
    } finally {
        isSaving.value = false
    }
}

async function handleAcceptTranslation() {
    if (!props.translationRequestId) return

    isSaving.value = true
    try {
        const edits = editedLines.value.size > 0
            ? Array.from(editedLines.value.entries()).map(([position, translatedText]) => ({
                  position,
                  translatedText
              }))
            : undefined
        const result = await translationCompareService.acceptTranslation(
            props.translationRequestId,
            edits
        )
        compareData.value = result
        editedLines.value.clear()
        emit('accepted')
    } catch (error) {
        errorMessage.value = t('translationTest.errors.acceptFailed', 'Failed to accept translation.')
    } finally {
        isSaving.value = false
    }
}

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}
</script>

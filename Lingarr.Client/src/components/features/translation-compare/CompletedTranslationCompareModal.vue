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
            <div class="border-secondary/30 bg-primary/40 border-b px-4 py-2 text-xs">
                <span class="text-secondary-content">
                    {{ compareData.sourceLanguage }} -> {{ compareData.targetLanguage }}
                </span>
                <span class="text-secondary-content/70 ml-3">
                    {{ compareData.lines.length }} {{ t('translationTest.lines', 'lines') }}
                </span>
            </div>
            <TranslationCompareTable :lines="compareData.lines" />
        </template>

        <div v-else class="flex h-[75vh] items-center justify-center">
            <p class="text-secondary-content text-sm">
                {{ t('translationTest.noResults', 'No compare data available.') }}
            </p>
        </div>

        <template #footer>
            <button
                v-if="translationRequestId"
                :disabled="isLoading"
                @click="loadCompareData(translationRequestId)"
                class="text-primary-content/60 hover:text-primary-content disabled:text-primary-content/30 rounded px-4 py-2">
                {{ translate('common.refresh') }}
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
import { computed, ref, watch } from 'vue'
import { useI18n } from '@/plugins/i18n'
import ModalComponent from '@/components/common/ModalComponent.vue'
import TranslationCompareTable from '@/components/features/translation-compare/TranslationCompareTable.vue'
import { translationCompareService } from '@/services/translationCompareService'
import type { CompletedTranslationCompareResponse } from '@/ts/translationCompare'

const props = defineProps<{
    isOpen: boolean
    translationRequestId: number | null
}>()

defineEmits<{
    close: []
}>()

const { translate } = useI18n()

const compareData = ref<CompletedTranslationCompareResponse | null>(null)
const isLoading = ref(false)
const errorMessage = ref<string | null>(null)

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

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}
</script>

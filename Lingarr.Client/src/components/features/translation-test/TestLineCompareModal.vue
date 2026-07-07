<template>
    <ModalComponent
        :is-open="isOpen"
        size="xxl"
        :body-scrollable="false"
        body-class="p-0"
        @close="$emit('close')">
        <template #header>
            {{ t('translationTest.debugPanel.compareView', 'Side-by-Side Compare') }}
        </template>

        <TranslationCompareTable :lines="lines" />

        <template #footer>
            <button
                @click="$emit('close')"
                class="text-primary-content/60 hover:text-primary-content rounded px-4 py-2">
                {{ translate('common.close') }}
            </button>
        </template>
    </ModalComponent>
</template>

<script setup lang="ts">
import { useI18n } from '@/plugins/i18n'
import ModalComponent from '@/components/common/ModalComponent.vue'
import TranslationCompareTable from '@/components/features/translation-compare/TranslationCompareTable.vue'
import type { TranslationCompareLine } from '@/ts/translationCompare'

export type CompareLineResult = TranslationCompareLine

defineProps<{
    isOpen: boolean
    lines: CompareLineResult[]
}>()

defineEmits<{
    close: []
}>()

const { translate } = useI18n()

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}
</script>

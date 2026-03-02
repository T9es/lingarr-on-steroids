<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <h2 class="text-lg font-semibold">{{ translate('translationTest.testHistory') }}</h2>
            <button
                v-if="testResults.length > 0"
                @click="clearAll"
                class="text-secondary-content hover:text-error text-sm">
                {{ translate('translationTest.clearAll') }}
            </button>
        </div>

        <div v-if="loading" class="flex justify-center py-8">
            <span class="text-secondary-content">
                {{ translate('translationTest.loadingHistory') }}
            </span>
        </div>

        <div
            v-else-if="testResults.length === 0"
            class="text-secondary-content py-8 text-center">
            {{ translate('translationTest.noHistory') }}
        </div>

        <div v-else class="grid gap-3">
            <div
                v-for="result in testResults"
                :key="result.id"
                class="bg-secondary hover:bg-secondary/80 cursor-pointer rounded-lg p-4 transition"
                @click="$emit('select', result)">
                <div class="flex items-start justify-between gap-4">
                    <div class="flex items-start gap-3">
                        <img
                            v-if="result.posterPath"
                            :src="`/api/image/${result.posterPath}`"
                            class="h-16 w-12 rounded object-cover"
                            @error="($event.target as HTMLImageElement).style.display = 'none'" />
                        <div class="flex-1">
                            <h3 class="font-medium">
                                {{ result.title || result.subtitlePath?.split('/').pop() }}
                            </h3>
                            <div class="text-secondary-content mt-1 flex items-center gap-2 text-xs">
                                <span
                                    :class="
                                        result.success
                                            ? 'bg-success/20 text-success'
                                            : 'bg-error/20 text-error'
                                    "
                                    class="rounded px-1.5 py-0.5">
                                    {{ result.success ? '✓' : '✗' }}
                                </span>
                                <span>
                                    {{ result.sourceLanguage }} → {{ result.targetLanguage }}
                                </span>
                                <span>·</span>
                                <span>
                                    {{ result.translatedLines }}/{{ result.totalLines }}
                                    {{ translate('translationTest.lines') }}
                                </span>
                            </div>
                            <div class="text-secondary-content/60 mt-1 text-xs">
                                {{ formatDate(result.createdAt) }} ·
                                {{ result.durationSeconds?.toFixed(1) }}s
                            </div>
                        </div>
                    </div>
                    <button
                        @click.stop="deleteResult(result.id)"
                        class="text-secondary-content/60 hover:text-error p-1">
                        <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path
                                stroke-linecap="round"
                                stroke-linejoin="round"
                                stroke-width="2"
                                d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from '@/plugins/i18n'

interface TestResult {
    id: number
    subtitlePath: string
    title?: string
    posterPath?: string
    sourceLanguage: string
    targetLanguage: string
    success: boolean
    totalLines: number
    translatedLines: number
    durationSeconds: number
    createdAt: string
}

defineEmits<{
    select: [result: TestResult]
}>()

const { translate } = useI18n()

const testResults = ref<TestResult[]>([])
const loading = ref(true)

async function loadHistory() {
    try {
        const response = await fetch('/api/test-results?page=1&pageSize=20')
        if (!response.ok) throw new Error('Failed to load history')
        const data = await response.json()
        testResults.value = data.items
    } catch (error) {
        console.error('Failed to load test history:', error)
    } finally {
        loading.value = false
    }
}

async function deleteResult(id: number) {
    try {
        await fetch(`/api/test-results/${id}`, { method: 'DELETE' })
        testResults.value = testResults.value.filter((r) => r.id !== id)
    } catch (error) {
        console.error('Failed to delete test result:', error)
    }
}

async function clearAll() {
    if (!confirm(translate('translationTest.confirmClearAll'))) return
    try {
        const ids = testResults.value.map((r) => r.id)
        await fetch('/api/test-results', {
            method: 'DELETE',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(ids)
        })
        testResults.value = []
    } catch (error) {
        console.error('Failed to clear test history:', error)
    }
}

function formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleString()
}

onMounted(loadHistory)

defineExpose({ loadHistory })
</script>
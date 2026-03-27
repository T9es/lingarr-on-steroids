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

        <div v-else-if="testResults.length === 0" class="text-secondary-content py-8 text-center">
            {{ translate('translationTest.noHistory') }}
        </div>

        <div v-else class="space-y-3">
            <div
                v-for="result in testResults"
                :key="result.id"
                class="bg-tertiary hover:bg-tertiary/80 cursor-pointer rounded-lg p-4 transition"
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
                            <div
                                class="text-secondary-content mt-1 flex items-center gap-2 text-xs">
                                <span
                                    :class="
                                        result.success
                                            ? 'bg-success/20 text-success'
                                            : 'bg-error/20 text-error'
                                    "
                                    class="rounded px-1.5 py-0.5">
                                    {{ result.success ? 'OK' : 'ERR' }}
                                </span>
                                <span>
                                    {{ result.sourceLanguage }} -> {{ result.targetLanguage }}
                                </span>
                                <span>|</span>
                                <span>
                                    {{ result.translatedLines }}/{{ result.totalLines }}
                                    {{ translate('translationTest.lines') }}
                                </span>
                            </div>
                            <div class="text-secondary-content/60 mt-1 text-xs">
                                {{ formatDate(result.createdAt) }} |
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

            <div ref="sentinel" class="h-4"></div>

            <div v-if="isLoadingMore" class="text-secondary-content py-2 text-center text-sm">
                {{ translate('translationTest.loadingHistory') }}
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref } from 'vue'
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
const isLoadingMore = ref(false)
const currentPage = ref(1)
const pageSize = 20
const hasMore = ref(true)
const sentinel = ref<HTMLElement | null>(null)
let observer: IntersectionObserver | null = null

async function fetchPage(page: number) {
    const response = await fetch(`/api/test-results?page=${page}&pageSize=${pageSize}`)

    if (!response.ok) {
        throw new Error('Failed to load history')
    }

    return response.json()
}

async function loadHistory(reset = true) {
    if (reset) {
        loading.value = true
        currentPage.value = 1
        hasMore.value = true
    } else if (isLoadingMore.value || !hasMore.value) {
        return
    } else {
        isLoadingMore.value = true
    }

    try {
        const data = await fetchPage(currentPage.value)
        const items = data.items || []

        if (reset) {
            testResults.value = items
        } else {
            testResults.value = [...testResults.value, ...items]
        }

        const totalPages = Number(data.totalPages || 0)
        hasMore.value = currentPage.value < totalPages

        if (hasMore.value) {
            currentPage.value += 1
        }
    } catch (error) {
        console.error('Failed to load test history:', error)
    } finally {
        loading.value = false
        isLoadingMore.value = false
        await nextTick()
        setupObserver()
    }
}

function setupObserver() {
    if (observer) {
        observer.disconnect()
    }

    observer = new IntersectionObserver((entries) => {
        if (entries[0].isIntersecting && hasMore.value && !isLoadingMore.value && !loading.value) {
            loadHistory(false)
        }
    })

    if (sentinel.value) {
        observer.observe(sentinel.value)
    }
}

async function deleteResult(id: number) {
    try {
        await fetch(`/api/test-results/${id}`, { method: 'DELETE' })
        testResults.value = testResults.value.filter((result) => result.id !== id)
    } catch (error) {
        console.error('Failed to delete test result:', error)
    }
}

async function clearAll() {
    if (!confirm(translate('translationTest.confirmClearAll'))) {
        return
    }

    try {
        const response = await fetch('/api/test-results/all', { method: 'DELETE' })

        if (!response.ok && response.status !== 404) {
            throw new Error('Failed to clear test history')
        }

        testResults.value = []
        currentPage.value = 1
        hasMore.value = false
    } catch (error) {
        console.error('Failed to clear test history:', error)
    }
}

function formatDate(dateStr: string): string {
    return new Date(dateStr).toLocaleString()
}

onMounted(() => {
    loadHistory(true)
})

onUnmounted(() => {
    if (observer) {
        observer.disconnect()
    }
})

defineExpose({ loadHistory })
</script>

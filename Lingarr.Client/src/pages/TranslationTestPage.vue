<template>
    <PageLayout>
        <div class="w-full p-4">
            <div class="bg-tertiary mb-4 rounded-lg p-4">
                <h1 class="text-2xl font-bold">{{ translate('translationTest.title') }}</h1>
                <p class="text-secondary-content mt-1 text-sm">
                    {{ translate('translationTest.description') }}
                </p>
            </div>

            <div class="grid gap-4 lg:grid-cols-2">
                <div class="space-y-4">
                    <div class="bg-secondary rounded-lg p-4">
                        <h2 class="mb-3 text-lg font-semibold">
                            {{ translate('translationTest.searchMedia') }}
                        </h2>

                        <input
                            v-model="searchQuery"
                            type="text"
                            :placeholder="translate('translationTest.searchPlaceholder')"
                            class="bg-primary border-accent mb-3 w-full rounded border px-3 py-2 text-sm"
                            :disabled="isRunning" />

                        <div v-if="isSearching" class="text-secondary-content py-4 text-center text-sm">
                            {{ translate('common.loading') }}
                        </div>

                        <div v-else-if="searchResults.length === 0 && searchQuery.trim().length >= 2" class="text-secondary-content py-4 text-center text-sm">
                            {{ translate('translationTest.noSearchResults') }}
                        </div>

                        <div v-else class="grid max-h-80 gap-2 overflow-y-auto">
                            <div
                                v-for="result in searchResults"
                                :key="`${result.mediaType}-${result.mediaId}`"
                                class="bg-tertiary hover:bg-tertiary/80 cursor-pointer rounded-lg p-3 transition"
                                @click="openConfigModal(result)">
                                <div class="flex gap-3">
                                    <img
                                        v-if="result.posterPath"
                                        :src="`/api/image/${result.posterPath}`"
                                        class="h-16 w-12 rounded object-cover"
                                        @error="($event.target as HTMLImageElement).style.display = 'none'" />
                                    <div class="flex-1">
                                        <div class="flex items-start justify-between gap-2">
                                            <h3 class="text-sm font-semibold">{{ result.displayTitle }}</h3>
                                            <span class="bg-accent/20 text-accent rounded px-1.5 py-0.5 text-[10px]">
                                                {{ result.mediaType === 'Movie' ? 'Movie' : 'TV' }}
                                            </span>
                                        </div>
                                        <p v-if="result.year" class="text-secondary-content text-xs">{{ result.year }}</p>
                                        <div class="mt-1 flex flex-wrap gap-1">
                                            <span
                                                v-for="subtitle in result.subtitles.slice(0, 4)"
                                                :key="subtitle.path"
                                                class="bg-primary text-primary-content rounded px-1.5 py-0.5 text-[10px]">
                                                {{ subtitle.language?.toUpperCase() || '??' }}
                                            </span>
                                            <span v-if="result.subtitles.length > 4" class="text-secondary-content text-[10px]">
                                                +{{ result.subtitles.length - 4 }}
                                            </span>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>

                    <TestHistoryList ref="historyList" @select="showTestResult" />
                </div>

                <div class="space-y-4">
                    <div v-if="activeTestResult" class="bg-secondary rounded-lg p-4">
                        <div class="flex items-center justify-between mb-3">
                            <h2 class="text-lg font-semibold">{{ translate('translationTest.testResult') }}</h2>
                            <button @click="activeTestResult = null" class="text-secondary-content hover:text-primary-content text-sm">
                                {{ translate('common.close') }}
                            </button>
                        </div>
                        <TestDebugPanel :result="activeTestResult" />
                    </div>

                    <div v-else class="bg-secondary rounded-lg p-4">
                        <h2 class="mb-3 text-lg font-semibold">{{ translate('translationTest.logs') }}</h2>
                        <div
                            ref="logContainer"
                            class="bg-primary h-80 overflow-y-auto rounded p-2 font-mono text-xs">
                            <div v-if="logs.length === 0" class="text-secondary-content flex h-full items-center justify-center">
                                {{ translate('translationTest.waitingForLogs') }}
                            </div>
                            <div v-else>
                                <div v-for="(log, i) in logs" :key="i" class="border-secondary/30 border-b py-1">
                                    <span class="text-secondary-content/70 mr-2">{{ formatTime(log.timestamp) }}</span>
                                    <span :class="getLogLevelClass(log.level)" class="mr-2 font-semibold">[{{ log.level }}]</span>
                                    <span>{{ log.message }}</span>
                                </div>
                            </div>
                        </div>
                        <div class="mt-2 flex justify-end gap-2">
                            <button
                                v-if="isRunning"
                                @click="cancelTest"
                                class="bg-error hover:bg-error/80 rounded px-3 py-1.5 text-sm text-primary-content">
                                {{ translate('translationTest.cancel') }}
                            </button>
                            <button
                                v-else-if="logs.length > 0"
                                @click="clearLogs"
                                class="bg-tertiary hover:bg-tertiary/80 rounded px-3 py-1.5 text-sm">
                                {{ translate('translationTest.clearLogs') }}
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </PageLayout>

    <TestConfigModal
        v-if="configModalOpen"
        :is-open="configModalOpen"
        :subtitle-path="selectedSubtitlePath"
        :title="selectedMedia?.displayTitle"
        :poster-path="selectedMedia?.posterPath"
        :year="selectedMedia?.year"
        :total-lines="selectedTotalLines"
        :default-source-language="selectedSubtitle?.language || 'en'"
        :default-target-language="defaultTargetLanguage"
        :available-source-languages="availableLanguages"
        :available-target-languages="availableLanguages"
        @close="closeConfigModal"
        @start="handleStartTest" />
</template>

<script setup lang="ts">
import { ref, watch, onMounted, nextTick } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from '@/plugins/i18n'
import useDebounce from '@/composables/useDebounce'
import PageLayout from '@/components/layout/PageLayout.vue'
import TestConfigModal from '@/components/features/translation-test/TestConfigModal.vue'
import TestHistoryList from '@/components/features/translation-test/TestHistoryList.vue'
import TestDebugPanel from '@/components/features/translation-test/TestDebugPanel.vue'

interface Subtitle {
    path: string
    language?: string
}

interface SearchResult {
    displayTitle: string
    mediaType: string
    mediaId: number
    posterPath?: string
    year?: number
    subtitles: Subtitle[]
}

interface LogEntry {
    level: string
    message: string
    timestamp: string
}

interface TestResultDetail {
    id: number
    success: boolean
    totalLines: number
    translatedLines: number
    durationSeconds: number
    tokenUsagePrompt?: number
    tokenUsageCompletion?: number
    apiCallsJson?: string
    lineResultsJson?: string
    timingJson?: string
}

const { translate } = useI18n()
const route = useRoute()

const searchQuery = ref('')
const searchResults = ref<SearchResult[]>([])
const isSearching = ref(false)
const isRunning = ref(false)
const logs = ref<LogEntry[]>([])
const logContainer = ref<HTMLElement | null>(null)
const activeTestResult = ref<TestResultDetail | null>(null)
const historyList = ref<{ loadHistory: () => void } | null>(null)

const configModalOpen = ref(false)
const selectedMedia = ref<SearchResult | null>(null)
const selectedSubtitle = ref<Subtitle | null>(null)
const selectedSubtitlePath = ref('')
const selectedTotalLines = ref(100)

const defaultTargetLanguage = ref('pl')

const availableLanguages = ref([
    { code: 'en', name: 'English' },
    { code: 'pl', name: 'Polish' },
    { code: 'de', name: 'German' },
    { code: 'fr', name: 'French' },
    { code: 'es', name: 'Spanish' },
    { code: 'nl', name: 'Dutch' },
    { code: 'it', name: 'Italian' },
    { code: 'pt', name: 'Portuguese' },
    { code: 'ru', name: 'Russian' },
    { code: 'ja', name: 'Japanese' },
    { code: 'zh', name: 'Chinese' },
    { code: 'ko', name: 'Korean' }
])

const performSearch = useDebounce(async (value: string) => {
    const trimmed = value.trim()
    if (trimmed.length < 2) {
        searchResults.value = []
        return
    }

    isSearching.value = true
    try {
        const response = await fetch(`/api/test-translation/search?query=${encodeURIComponent(trimmed)}`)
        if (response.ok) {
            searchResults.value = await response.json()
        }
    } catch (error) {
        console.error('Search failed:', error)
    } finally {
        isSearching.value = false
    }
}, 300)

watch(searchQuery, (value) => {
    if (value) performSearch(value)
    else searchResults.value = []
})

async function openConfigModal(result: SearchResult) {
    if (result.subtitles.length === 0) return
    
    selectedMedia.value = result
    selectedSubtitle.value = result.subtitles[0]
    selectedSubtitlePath.value = result.subtitles[0].path
    
    try {
        const response = await fetch(`/api/test-translation/subtitle-preview?path=${encodeURIComponent(result.subtitles[0].path)}`)
        if (response.ok) {
            const data = await response.json()
            selectedTotalLines.value = data.totalLines || 100
        }
    } catch {
        selectedTotalLines.value = 100
    }
    
    configModalOpen.value = true
}

function closeConfigModal() {
    configModalOpen.value = false
    selectedMedia.value = null
    selectedSubtitle.value = null
}

async function handleStartTest(config: {
    subtitlePath: string
    sourceLanguage: string
    targetLanguage: string
    startLine?: number
    endLine?: number
    maxLines?: number
}) {
    closeConfigModal()
    isRunning.value = true
    logs.value = []
    activeTestResult.value = null

    try {
        const response = await fetch('/api/test-translation/start', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                subtitlePath: config.subtitlePath,
                sourceLanguage: config.sourceLanguage,
                targetLanguage: config.targetLanguage,
                startLine: config.startLine,
                endLine: config.endLine,
                maxLines: config.maxLines,
                mediaId: selectedMedia.value?.mediaId,
                mediaType: selectedMedia.value?.mediaType
            })
        })

        const reader = response.body?.getReader()
        const decoder = new TextDecoder()

        if (!reader) throw new Error('Failed to get response reader')

        while (true) {
            const { done, value } = await reader.read()
            if (done) break

            const text = decoder.decode(value)
            const lines = text.split('\n').filter((line) => line.startsWith('data: '))

            for (const line of lines) {
                try {
                    const data = JSON.parse(line.substring(6))
                    if (data.type === 'log') {
                        logs.value.push({
                            level: data.Level,
                            message: data.Message,
                            timestamp: data.Timestamp
                        })
                        scrollToBottom()
                    } else if (data.type === 'result') {
                        if (data.TestResultId) {
                            await loadTestResult(data.TestResultId)
                        }
                    }
                } catch {
                    // Skip malformed JSON
                }
            }
        }
    } catch (error) {
        logs.value.push({
            level: 'ERROR',
            message: `Error: ${error instanceof Error ? error.message : 'Unknown'}`,
            timestamp: new Date().toISOString()
        })
    } finally {
        isRunning.value = false
        historyList.value?.loadHistory()
    }
}

async function loadTestResult(id: number) {
    try {
        const response = await fetch(`/api/test-results/${id}`)
        if (response.ok) {
            activeTestResult.value = await response.json()
        }
    } catch (error) {
        console.error('Failed to load test result:', error)
    }
}

function showTestResult(result: { id: number }) {
    loadTestResult(result.id)
}

async function cancelTest() {
    await fetch('/api/test-translation/cancel', { method: 'POST' })
    logs.value.push({
        level: 'WARNING',
        message: 'Cancel request sent...',
        timestamp: new Date().toISOString()
    })
}

function clearLogs() {
    logs.value = []
}

function formatTime(timestamp: string): string {
    return new Date(timestamp).toLocaleTimeString()
}

function getLogLevelClass(level: string): string {
    switch (level.toUpperCase()) {
        case 'ERROR': return 'text-error'
        case 'WARNING': return 'text-warning'
        case 'INFORMATION': return 'text-success'
        default: return 'text-accent'
    }
}

async function scrollToBottom() {
    await nextTick()
    if (logContainer.value) {
        logContainer.value.scrollTop = logContainer.value.scrollHeight
    }
}

onMounted(async () => {
    if (route.query.subtitlePath) {
        selectedSubtitlePath.value = route.query.subtitlePath as string
        try {
            const response = await fetch(`/api/test-translation/subtitle-preview?path=${encodeURIComponent(selectedSubtitlePath.value)}`)
            if (response.ok) {
                const data = await response.json()
                selectedTotalLines.value = data.totalLines || 100
            }
        } catch {
            selectedTotalLines.value = 100
        }
        
        selectedMedia.value = {
            displayTitle: (route.query.title as string) || 'Test',
            mediaType: (route.query.mediaType as string) || 'Movie',
            mediaId: route.query.mediaId ? parseInt(route.query.mediaId as string) : 0,
            subtitles: [{ path: selectedSubtitlePath.value, language: route.query.sourceLanguage as string }]
        }
        selectedSubtitle.value = selectedMedia.value.subtitles[0]
        
        if (route.query.targetLanguage) {
            defaultTargetLanguage.value = route.query.targetLanguage as string
        }
        
        configModalOpen.value = true
    }
})
</script>
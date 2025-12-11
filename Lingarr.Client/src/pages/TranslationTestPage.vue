<template>
    <PageLayout>
        <div class="w-full p-4">
            <!-- Header -->
            <div class="bg-tertiary mb-4 rounded-lg p-4">
                <h1 class="text-2xl font-bold">{{ translate('translationTest.title') }}</h1>
                <p class="text-secondary-content text-sm mt-1">
                    {{ translate('translationTest.description') }}
                </p>
            </div>

            <!-- Configuration Panel -->
            <div class="bg-secondary rounded-lg p-4 mb-4">
                <h2 class="text-lg font-semibold mb-3">{{ translate('translationTest.configuration') }}</h2>

                <!-- Media Search -->
                <div class="mb-4">
                    <label class="block text-sm font-medium mb-1">
                        {{ translate('translationTest.searchMedia') }}
                    </label>
                    <input
                        v-model="searchQuery"
                        type="text"
                        :placeholder="translate('translationTest.searchPlaceholder')"
                        class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm"
                        :disabled="isRunning" />
                    <p class="text-secondary-content mt-1 text-xs">
                        {{ translate('translationTest.searchHelp') }}
                    </p>
                    <p v-if="searchError" class="text-error mt-1 text-xs">
                        {{ searchError }}
                    </p>
                </div>

                <!-- Search Results -->
                <div v-if="searchResults.length" class="mb-4 rounded border border-accent/40 bg-tertiary">
                    <div
                        class="border-secondary/40 flex items-center justify-between border-b px-3 py-2 text-xs text-secondary-content">
                        <span>{{ translate('translationTest.searchResultsTitle') }}</span>
                        <span v-if="isSearching" class="text-[10px] uppercase tracking-wide">
                            {{ translate('common.loading') }}
                        </span>
                    </div>
                    <div class="max-h-64 divide-y divide-secondary/40 overflow-y-auto">
                        <div
                            v-for="result in searchResults"
                            :key="`${result.mediaType}-${result.mediaId}`"
                            class="px-3 py-2">
                            <p class="text-sm font-semibold">
                                {{ result.displayTitle }}
                            </p>
                            <p class="text-secondary-content mb-2 text-xs">
                                {{
                                    result.mediaType === 'Movie'
                                        ? translate('movies.title')
                                        : translate('tvShows.episode')
                                }}
                            </p>
                            <div class="flex flex-wrap gap-2">
                                <button
                                    v-for="subtitle in result.subtitles"
                                    :key="subtitle.path"
                                    type="button"
                                    class="border-accent hover:bg-accent cursor-pointer rounded border px-2 py-1 text-xs text-primary-content transition-colors"
                                    @click="applySubtitleFromSearch(result, subtitle)">
                                    {{ subtitle.language.toUpperCase() || '??' }}
                                    <span
                                        v-if="subtitle.caption"
                                        class="text-primary-content/70">
                                        - {{ subtitle.caption.toUpperCase() }}
                                    </span>
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
                <div
                    v-else-if="searchQuery.trim().length >= 2 && !isSearching"
                    class="mb-4 text-xs text-secondary-content">
                    {{ translate('translationTest.noSearchResults') }}
                </div>

                <!-- Subtitle File Path -->
                <div class="mb-4">
                    <label class="block text-sm font-medium mb-1">
                        {{ translate('translationTest.subtitlePath') }}
                    </label>
                    <input
                        v-model="subtitlePath"
                        type="text"
                        :placeholder="translate('translationTest.subtitlePathPlaceholder')"
                        class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm"
                        :disabled="isRunning" />
                    <p v-if="selectedFromSearch" class="text-secondary-content mt-1 text-xs">
                        {{ translate('translationTest.selectedFromSearch') }} {{ selectedFromSearch }}
                    </p>
                </div>

                <!-- Languages -->
                <div class="grid grid-cols-2 gap-4 mb-4">
                    <div>
                        <label class="block text-sm font-medium mb-1">
                            {{ translate('translationTest.sourceLanguage') }}
                        </label>
                        <input
                            v-model="sourceLanguage"
                            type="text"
                            placeholder="en"
                            class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm"
                            :disabled="isRunning" />
                    </div>
                    <div>
                        <label class="block text-sm font-medium mb-1">
                            {{ translate('translationTest.targetLanguage') }}
                        </label>
                        <input
                            v-model="targetLanguage"
                            type="text"
                            placeholder="pl"
                            class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm"
                            :disabled="isRunning" />
                    </div>
                </div>

                <!-- Actions -->
                <div class="flex gap-2">
                    <button
                        v-if="!isRunning"
                        class="bg-accent hover:bg-accent/80 cursor-pointer rounded px-4 py-2 text-sm font-medium text-white transition"
                        :disabled="!canStart"
                        @click="startTest">
                        {{ translate('translationTest.startTest') }}
                    </button>
                    <button
                        v-else
                        class="bg-error hover:bg-error/80 cursor-pointer rounded px-4 py-2 text-sm font-medium text-white transition"
                        @click="cancelTest">
                        {{ translate('translationTest.cancel') }}
                    </button>
                </div>
            </div>

            <!-- Results Panel (shown after completion) -->
            <div
                v-if="result"
                class="rounded-lg p-4 mb-4"
                :class="result.success ? 'bg-success/20 border border-success' : 'bg-error/20 border border-error'">
                <h2 class="text-lg font-semibold mb-2">
                    {{ result.success ? translate('translationTest.success') : translate('translationTest.failed') }}
                </h2>
                <div class="text-sm">
                    <p v-if="result.errorMessage" class="text-error">{{ result.errorMessage }}</p>
                    <p v-if="result.totalSubtitles">
                        {{ translate('translationTest.translated') }}: {{ result.translatedCount }}/{{ result.totalSubtitles }}
                    </p>
                    <p v-if="result.duration">
                        {{ translate('translationTest.duration') }}: {{ result.duration.toFixed(1) }}s
                    </p>
                </div>
            </div>

            <!-- Log Console -->
            <div class="bg-secondary rounded-lg overflow-hidden">
                <div class="bg-tertiary flex items-center justify-between px-4 py-2">
                    <h2 class="text-sm font-semibold">{{ translate('translationTest.logs') }}</h2>
                    <button
                        class="bg-warning hover:bg-warning/80 cursor-pointer rounded px-2 py-1 text-xs text-white transition"
                        @click="clearLogs">
                        {{ translate('translationTest.clearLogs') }}
                    </button>
                </div>
                
                <div
                    ref="logContainer"
                    class="bg-primary h-[40vh] overflow-y-auto font-mono text-xs p-2">
                    <div v-if="logs.length === 0" class="flex h-full items-center justify-center text-gray-500">
                        {{ translate('translationTest.waitingForLogs') }}
                    </div>
                    
                    <div
                        v-for="(log, index) in logs"
                        :key="index"
                        class="py-1 border-b border-secondary/30">
                        <span class="text-gray-400 mr-2">{{ formatTime(log.timestamp) }}</span>
                        <span
                            :class="getLogLevelClass(log.level)"
                            class="mr-2 font-semibold">
                            [{{ log.level }}]
                        </span>
                        <span>{{ log.message }}</span>
                        <div v-if="log.details" class="text-xs text-gray-500 ml-4 whitespace-pre-wrap">
                            {{ log.details }}
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </PageLayout>
</template>

<script setup lang="ts">
import { ref, computed, nextTick, watch } from 'vue'
import { useI18n } from '@/plugins/i18n'
import useDebounce from '@/composables/useDebounce'
import PageLayout from '@/components/layout/PageLayout.vue'
import type { ISubtitle } from '@/ts'

interface LogEntry {
    level: string
    message: string
    timestamp: string
    details?: string
}

interface TestResult {
    success: boolean
    errorMessage?: string
    totalSubtitles?: number
    translatedCount?: number
    duration?: number
}

const { translate } = useI18n()

const subtitlePath = ref('')
const sourceLanguage = ref('en')
const targetLanguage = ref('')
const isRunning = ref(false)
const logs = ref<LogEntry[]>([])
const result = ref<TestResult | null>(null)
const logContainer = ref<HTMLElement | null>(null)

interface SearchResult {
    displayTitle: string
    mediaType: 'Movie' | 'Episode'
    mediaId: number
    subtitles: ISubtitle[]
}

const searchQuery = ref('')
const searchResults = ref<SearchResult[]>([])
const isSearching = ref(false)
const searchError = ref<string | null>(null)
const selectedFromSearch = ref<string | null>(null)
let lastSearchToken = ''

const canStart = computed(() => {
    return subtitlePath.value.trim() !== '' && 
           sourceLanguage.value.trim() !== '' && 
           targetLanguage.value.trim() !== ''
})

function formatTime(timestamp: string): string {
    const date = new Date(timestamp)
    return date.toLocaleTimeString()
}

function getLogLevelClass(level: string): string {
    switch (level.toUpperCase()) {
        case 'ERROR':
            return 'text-red-500'
        case 'WARNING':
            return 'text-orange-500'
        case 'INFORMATION':
            return 'text-green-500'
        default:
            return 'text-blue-500'
    }
}

function clearLogs() {
    logs.value = []
    result.value = null
}

const performSearch = useDebounce(async (value: string) => {
    const trimmed = value.trim()
    const token = `${Date.now()}-${trimmed}`
    lastSearchToken = token

    if (trimmed.length < 2) {
        searchResults.value = []
        searchError.value = null
        isSearching.value = false
        return
    }

    isSearching.value = true
    searchError.value = null

    try {
        const response = await fetch(
            `/api/test-translation/search?query=${encodeURIComponent(trimmed)}`
        )
        if (!response.ok) {
            throw new Error(`Search failed with status ${response.status}`)
        }

        const data = (await response.json()) as SearchResult[]

        // Drop out-of-order responses
        if (token !== lastSearchToken) {
            return
        }

        searchResults.value = data
    } catch (error) {
        console.error('Search failed', error)
        searchError.value =
            error instanceof Error ? error.message : 'Failed to search media for test translation.'
        searchResults.value = []
    } finally {
        if (token === lastSearchToken) {
            isSearching.value = false
        }
    }
}, 300)

watch(
    () => searchQuery.value,
    (value) => {
        if (!value) {
            searchResults.value = []
            searchError.value = null
            isSearching.value = false
            selectedFromSearch.value = null
            return
        }

        performSearch(value)
    }
)

function applySubtitleFromSearch(result: SearchResult, subtitle: ISubtitle) {
    subtitlePath.value = subtitle.path
    if (subtitle.language && subtitle.language.trim() !== '') {
        sourceLanguage.value = subtitle.language
    }

    const language = subtitle.language ? subtitle.language.toUpperCase() : '??'
    const caption = subtitle.caption ? subtitle.caption.toUpperCase() : ''
    selectedFromSearch.value = caption
        ? `${result.displayTitle} • ${language} • ${caption}`
        : `${result.displayTitle} • ${language}`
}

async function scrollToBottom() {
    await nextTick()
    if (logContainer.value) {
        logContainer.value.scrollTop = logContainer.value.scrollHeight
    }
}

async function startTest() {
    if (!canStart.value || isRunning.value) return
    
    isRunning.value = true
    result.value = null
    logs.value = []
    
    try {
        const response = await fetch('/api/test-translation/start', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                subtitlePath: subtitlePath.value,
                sourceLanguage: sourceLanguage.value,
                targetLanguage: targetLanguage.value
            })
        })
        
        const reader = response.body?.getReader()
        const decoder = new TextDecoder()
        
        if (!reader) {
            throw new Error('Failed to get response reader')
        }
        
        while (true) {
            const { done, value } = await reader.read()
            if (done) break
            
            const text = decoder.decode(value)
            const lines = text.split('\n').filter(line => line.startsWith('data: '))
            
            for (const line of lines) {
                try {
                    const data = JSON.parse(line.substring(6))
                    
                    if (data.type === 'log') {
                        logs.value.push({
                            level: data.Level,
                            message: data.Message,
                            timestamp: data.Timestamp,
                            details: data.Details
                        })
                        scrollToBottom()
                    } else if (data.type === 'result') {
                        result.value = {
                            success: data.Success,
                            errorMessage: data.ErrorMessage,
                            totalSubtitles: data.TotalSubtitles,
                            translatedCount: data.TranslatedCount,
                            duration: data.Duration
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
            message: `Connection error: ${error instanceof Error ? error.message : 'Unknown error'}`,
            timestamp: new Date().toISOString()
        })
    } finally {
        isRunning.value = false
    }
}

async function cancelTest() {
    try {
        await fetch('/api/test-translation/cancel', {
            method: 'POST'
        })
        logs.value.push({
            level: 'WARNING',
            message: 'Cancel request sent...',
            timestamp: new Date().toISOString()
        })
    } catch (error) {
        logs.value.push({
            level: 'ERROR',
            message: `Failed to cancel: ${error instanceof Error ? error.message : 'Unknown error'}`,
            timestamp: new Date().toISOString()
        })
    }
}
</script>

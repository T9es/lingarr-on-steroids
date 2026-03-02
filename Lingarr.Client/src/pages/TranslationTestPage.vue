<template>
    <PageLayout>
        <div class="w-full p-4">
            <!-- Header -->
            <div class="bg-tertiary mb-4 rounded-lg p-4">
                <h1 class="text-2xl font-bold">{{ translate('translationTest.title') }}</h1>
                <p class="text-secondary-content mt-1 text-sm">
                    {{ translate('translationTest.description') }}
                </p>
            </div>

            <!-- Configuration Panel -->
            <div class="bg-secondary mb-4 rounded-lg p-4">
                <h2 class="mb-3 text-lg font-semibold">
                    {{ translate('translationTest.configuration') }}
                </h2>

                <!-- Media Search -->
                <div class="mb-4">
                    <label class="mb-1 block text-sm font-medium">
                        {{ translate('translationTest.searchMedia') }}
                    </label>
                    <input
                        v-model="searchQuery"
                        type="text"
                        :placeholder="
                            translate('translationTest.searchPlaceholder') +
                            ' (e.g. Movie Title, Show Name)'
                        "
                        class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm focus:ring-2 focus:ring-accent focus:border-transparent"
                        :disabled="isRunning" />
                    <p class="text-secondary-content mt-1 text-xs">
                        {{ translate('translationTest.searchDescription') }}
                    </p>
                    <p v-if="searchError" class="text-error mt-1 text-xs">
                        {{ searchError }}
                    </p>
                </div>

                <!-- Search Results - Card Grid -->
                <div v-if="searchResults.length" class="mb-4">
                    <div class="text-secondary-content mb-2 flex items-center justify-between text-xs">
                        <span>{{ translate('translationTest.searchResultsTitle') }}</span>
                        <span v-if="isSearching" class="text-[10px] tracking-wide uppercase">
                            {{ translate('common.loading') }}
                        </span>
                    </div>
                    <div class="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                        <div
                            v-for="result in searchResults"
                            :key="`${result.mediaType}-${result.mediaId}`"
                            class="bg-tertiary border-accent/30 hover:border-accent overflow-hidden rounded-lg border transition-colors">
                            <!-- Card Header with Poster -->
                            <div class="flex gap-3 p-3">
                                <!-- Poster -->
                                <div class="bg-secondary h-24 w-16 shrink-0 overflow-hidden rounded">
                                    <img
                                        v-if="result.posterPath"
                                        :src="`/api/image/${result.posterPath}`"
                                        class="h-full w-full object-cover"
                                        alt=""
                                        @error="($event.target as HTMLImageElement).style.display = 'none'" />
                                    <div
                                        v-else
                                        class="text-secondary-content/50 flex h-full items-center justify-center text-2xl">
                                        {{ result.mediaType === 'Movie' ? '🎬' : '📺' }}
                                    </div>
                                </div>
                                <!-- Info -->
                                <div class="min-w-0 flex-1">
                                    <div class="flex items-start justify-between gap-1">
                                        <h3 class="text-sm font-semibold leading-tight">
                                            {{ result.displayTitle }}
                                        </h3>
                                        <span
                                            class="bg-accent/20 text-accent shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium">
                                            {{ result.mediaType === 'Movie' ? 'Movie' : 'TV' }}
                                        </span>
                                    </div>
                                    <p v-if="result.year" class="text-secondary-content text-xs">
                                        {{ result.year }}
                                    </p>
                                </div>
                            </div>
                            <!-- Subtitle Badges -->
                            <div class="border-secondary/30 border-t px-3 py-2">
                                <div class="flex flex-wrap gap-1.5">
                                    <button
                                        v-for="subtitle in result.subtitles.slice(0, 5)"
                                        :key="subtitle.path"
                                        type="button"
                                        class="bg-primary hover:bg-accent text-primary-content cursor-pointer rounded px-2 py-1 text-xs font-medium transition-colors"
                                        :class="{
                                            'ring-2 ring-accent':
                                                selectedSubtitleInfo ===
                                                getSubtitleKey(result, subtitle)
                                        }"
                                        @click="applySubtitleFromSearch(result, subtitle)">
                                        {{ subtitle.language.toUpperCase() || '??' }}
                                        <span
                                            v-if="subtitle.caption"
                                            class="text-primary-content/70 ml-0.5">
                                            {{ subtitle.caption.toUpperCase().slice(0, 8) }}
                                        </span>
                                    </button>
                                    <button
                                        v-if="result.subtitles.length > 5"
                                        type="button"
                                        class="text-secondary-content hover:text-primary-content cursor-pointer px-2 py-1 text-xs transition-colors"
                                        @click="toggleExpandedSubtitles(result)">
                                        +{{ result.subtitles.length - 5 }} more
                                    </button>
                                </div>
                                <!-- Expanded subtitles -->
                                <div
                                    v-if="expandedResults.has(getResultKey(result))"
                                    class="mt-2 flex flex-wrap gap-1.5 border-t border-secondary/30 pt-2">
                                    <button
                                        v-for="subtitle in result.subtitles.slice(5)"
                                        :key="subtitle.path"
                                        type="button"
                                        class="bg-primary hover:bg-accent text-primary-content cursor-pointer rounded px-2 py-1 text-xs font-medium transition-colors"
                                        @click="applySubtitleFromSearch(result, subtitle)">
                                        {{ subtitle.language.toUpperCase() || '??' }}
                                        <span
                                            v-if="subtitle.caption"
                                            class="text-primary-content/70 ml-0.5">
                                            {{ subtitle.caption.toUpperCase().slice(0, 8) }}
                                        </span>
                                    </button>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div
                    v-else-if="searchQuery.trim().length >= 2 && !isSearching"
                    class="text-secondary-content mb-4 text-xs">
                    {{ translate('translationTest.noSearchResults') }}
                </div>

                <!-- Subtitle File Path -->
                <div class="mb-4">
                    <label class="mb-1 block text-sm font-medium">
                        {{ translate('translationTest.subtitlePath') }}
                    </label>
                    <input
                        v-model="subtitlePath"
                        type="text"
                        :placeholder="translate('translationTest.subtitlePathPlaceholder')"
                        class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm focus:ring-2 focus:ring-accent focus:border-transparent"
                        :disabled="isRunning" />
                    <p v-if="selectedSubtitleInfo" class="text-accent mt-1 text-xs">
                        ✓ {{ selectedSubtitleInfo }}
                    </p>
                </div>

                <!-- Languages -->
                <div class="mb-4 grid grid-cols-2 gap-4">
                    <div>
                        <label class="mb-1 block text-sm font-medium">
                            {{ translate('translationTest.sourceLanguage') }}
                        </label>
                        <input
                            v-model="sourceLanguage"
                            type="text"
                            placeholder="en"
                            class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm focus:ring-2 focus:ring-accent focus:border-transparent"
                            :disabled="isRunning" />
                    </div>
                    <div>
                        <label class="mb-1 block text-sm font-medium">
                            {{ translate('translationTest.targetLanguage') }}
                        </label>
                        <input
                            v-model="targetLanguage"
                            type="text"
                            placeholder="pl"
                            class="bg-primary border-accent w-full rounded border px-3 py-2 text-sm focus:ring-2 focus:ring-accent focus:border-transparent"
                            :disabled="isRunning" />
                    </div>
                </div>

                <!-- Actions -->
                <div class="flex gap-2">
                    <button
                        v-if="!isRunning"
                        class="bg-accent hover:bg-accent/80 cursor-pointer rounded px-4 py-2 text-sm font-medium text-primary-content transition disabled:cursor-not-allowed disabled:opacity-50"
                        :disabled="!canStart"
                        @click="startTest">
                        {{ translate('translationTest.startTest') }}
                    </button>
                    <button
                        v-else
                        class="bg-error hover:bg-error/80 cursor-pointer rounded px-4 py-2 text-sm font-medium text-primary-content transition"
                        @click="cancelTest">
                        {{ translate('translationTest.cancel') }}
                    </button>
                </div>
            </div>

            <!-- Results Panel (shown after completion) -->
            <div
                v-if="result"
                class="mb-4 rounded-lg p-4"
                :class="
                    result.success
                        ? 'bg-success/20 border-success border'
                        : 'bg-error/20 border-error border'
                ">
                <h2 class="mb-2 text-lg font-semibold">
                    {{
                        result.success
                            ? translate('translationTest.success')
                            : translate('translationTest.failed')
                    }}
                </h2>
                <div class="text-sm">
                    <p v-if="result.errorMessage" class="text-error">{{ result.errorMessage }}</p>
                    <p v-if="result.totalSubtitles">
                        {{ translate('translationTest.translated') }}:
                        {{ result.translatedCount }}/{{ result.totalSubtitles }}
                    </p>
                    <p v-if="result.duration">
                        {{ translate('translationTest.duration') }}:
                        {{ result.duration.toFixed(1) }}s
                    </p>
                </div>

                <!-- Comparison View -->
                <div
                    v-if="result.preview && result.preview.length > 0"
                    class="border-secondary/30 mt-4 border-t pt-4">
                    <div class="mb-2 flex items-center justify-between">
                        <h3 class="text-md font-semibold">
                            {{ translate('translationTest.preview') }}
                        </h3>
                        <div class="flex gap-2">
                            <button
                                class="bg-secondary hover:bg-secondary/80 rounded px-2 py-1 text-xs transition"
                                @click="downloadOriginal">
                                ⬇ Original
                            </button>
                            <button
                                class="bg-accent hover:bg-accent/80 rounded px-2 py-1 text-xs text-primary-content transition"
                                @click="downloadTranslated">
                                ⬇ Translated
                            </button>
                        </div>
                    </div>

                    <div
                        class="bg-primary border-secondary/40 flex h-96 flex-col overflow-hidden rounded-lg border">
                        <div
                            class="bg-secondary/50 border-secondary/40 grid grid-cols-2 border-b p-2 text-xs font-bold">
                            <div class="pl-2">Original ({{ sourceLanguage.toUpperCase() }})</div>
                            <div class="pl-2">Translated ({{ targetLanguage.toUpperCase() }})</div>
                        </div>
                        <div class="divide-secondary/20 flex-1 divide-y overflow-y-auto">
                            <div
                                v-for="item in result.preview"
                                :key="item.position"
                                class="hover:bg-secondary/10 grid grid-cols-2">
                                <div
                                    class="border-secondary/20 border-r p-2 text-xs whitespace-pre-wrap">
                                    {{ item.original }}
                                </div>
                                <div class="p-2 text-xs whitespace-pre-wrap">
                                    {{ item.translated }}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Log Console -->
            <div class="bg-secondary overflow-hidden rounded-lg">
                <div class="bg-tertiary flex items-center justify-between px-4 py-2">
                    <h2 class="text-sm font-semibold">{{ translate('translationTest.logs') }}</h2>
                    <button
                        class="bg-warning hover:bg-warning/80 cursor-pointer rounded px-2 py-1 text-xs text-primary-content transition"
                        @click="clearLogs">
                        {{ translate('translationTest.clearLogs') }}
                    </button>
                </div>

                <div
                    ref="logContainer"
                    class="bg-primary h-[40vh] overflow-y-auto p-2 font-mono text-xs">
                    <div
                        v-if="logs.length === 0"
                        class="flex h-full items-center justify-center text-secondary-content/60">
                        {{ translate('translationTest.waitingForLogs') }}
                    </div>

                    <div
                        v-for="(log, index) in logs"
                        :key="index"
                        class="border-secondary/30 border-b py-1">
                        <span class="mr-2 text-secondary-content/70">{{ formatTime(log.timestamp) }}</span>
                        <span :class="getLogLevelClass(log.level)" class="mr-2 font-semibold">
                            [{{ log.level }}]
                        </span>
                        <span>{{ log.message }}</span>
                        <div
                            v-if="log.details"
                            class="ml-4 text-xs whitespace-pre-wrap text-secondary-content/60">
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

interface SubtitlePreview {
    position: number
    original: string
    translated: string
}

interface TestResult {
    success: boolean
    errorMessage?: string
    totalSubtitles?: number
    translatedCount?: number
    duration?: number
    preview?: SubtitlePreview[]
}

interface SearchResult {
    displayTitle: string
    mediaType: 'Movie' | 'Episode'
    mediaId: number
    posterPath?: string
    year?: number
    subtitles: ISubtitle[]
}

const { translate } = useI18n()

const subtitlePath = ref('')
const sourceLanguage = ref('en')
const targetLanguage = ref('')
const isRunning = ref(false)
const logs = ref<LogEntry[]>([])
const result = ref<TestResult | null>(null)
const logContainer = ref<HTMLElement | null>(null)

const searchQuery = ref('')
const searchResults = ref<SearchResult[]>([])
const isSearching = ref(false)
const searchError = ref<string | null>(null)
const selectedSubtitleInfo = ref<string | null>(null)
const expandedResults = ref<Set<string>>(new Set())
let lastSearchToken = ''

const canStart = computed(() => {
    return (
        subtitlePath.value.trim() !== '' &&
        sourceLanguage.value.trim() !== '' &&
        targetLanguage.value.trim() !== ''
    )
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

function downloadContent(content: string, filename: string) {
    const blob = new Blob([content], { type: 'text/plain' })
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = filename
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    window.URL.revokeObjectURL(url)
}

function downloadOriginal() {
    if (!result.value?.preview) return
    const content = result.value.preview.map((p) => `${p.position}\n${p.original}\n`).join('\n')
    downloadContent(content, `original_${Date.now()}.srt`)
}

function downloadTranslated() {
    if (!result.value?.preview) return
    const content = result.value.preview.map((p) => `${p.position}\n${p.translated}\n`).join('\n')
    downloadContent(content, `translated_${targetLanguage.value}_${Date.now()}.srt`)
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
            selectedSubtitleInfo.value = null
            return
        }

        performSearch(value)
    }
)

function getResultKey(result: SearchResult): string {
    return `${result.mediaType}-${result.mediaId}`
}

function getSubtitleKey(result: SearchResult, subtitle: ISubtitle): string {
    return `${result.mediaType}-${result.mediaId}-${subtitle.path}`
}

function toggleExpandedSubtitles(result: SearchResult) {
    const key = getResultKey(result)
    if (expandedResults.value.has(key)) {
        expandedResults.value.delete(key)
    } else {
        expandedResults.value.add(key)
    }
}

function applySubtitleFromSearch(result: SearchResult, subtitle: ISubtitle) {
    subtitlePath.value = subtitle.path
    if (subtitle.language && subtitle.language.trim() !== '') {
        sourceLanguage.value = subtitle.language
    }

    const language = subtitle.language ? subtitle.language.toUpperCase() : '??'
    const caption = subtitle.caption ? ` • ${subtitle.caption.toUpperCase()}` : ''
    const year = result.year ? ` (${result.year})` : ''
    selectedSubtitleInfo.value = `${result.displayTitle}${year} • ${language}${caption}`
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
            const lines = text.split('\n').filter((line) => line.startsWith('data: '))

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
                            duration: data.Duration,
                            preview: data.Preview
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
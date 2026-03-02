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

                        <div v-else-if="hierarchicalSearchResults.movies.length === 0 && hierarchicalSearchResults.shows.length === 0 && searchQuery.trim().length >= 2" class="text-secondary-content py-4 text-center text-sm">
                            {{ translate('translationTest.noSearchResults') }}
                        </div>

                        <div v-else class="space-y-3 max-h-[60vh] overflow-y-auto">
                            <!-- Movies -->
                            <div
                                v-for="movie in hierarchicalSearchResults.movies"
                                :key="`movie-${movie.movieId}`"
                                class="bg-tertiary hover:bg-tertiary/80 cursor-pointer rounded-lg p-3 transition"
                                @click="openConfigModalFromMovie(movie)">
                                <div class="flex gap-3">
                                    <img
                                        v-if="movie.posterPath"
                                        :src="`/api/image/${movie.posterPath}`"
                                        class="h-16 w-12 rounded object-cover"
                                        @error="($event.target as HTMLImageElement).style.display = 'none'" />
                                    <div class="flex-1">
                                        <div class="flex items-start justify-between gap-2">
                                            <h3 class="text-sm font-semibold text-primary-content">{{ movie.title }}</h3>
                                            <span class="bg-accent/20 text-accent rounded px-1.5 py-0.5 text-[10px]">
                                                Movie
                                            </span>
                                        </div>
                                        <p v-if="movie.year" class="text-secondary-content text-xs">{{ movie.year }}</p>
                                        <div class="mt-1 flex flex-wrap gap-1">
                                            <span
                                                v-for="subtitle in movie.subtitles.slice(0, 4)"
                                                :key="subtitle.path"
                                                class="bg-primary text-primary-content rounded px-1.5 py-0.5 text-[10px]">
                                                {{ subtitle.language?.toUpperCase() || '??' }}
                                            </span>
                                            <span v-if="movie.subtitles.length > 4" class="text-secondary-content text-[10px]">
                                                +{{ movie.subtitles.length - 4 }}
                                            </span>
                                            <span
                                                v-for="embSub in (movie.embeddedSubtitles || []).slice(0, 4)"
                                                :key="`emb-${embSub.streamIndex}`"
                                                class="border rounded px-1.5 py-0.5 text-[10px]"
                                                :class="getEmbeddedBadgeClasses(embSub)">
                                                <span class="mr-0.5">📦</span>
                                                {{ formatEmbeddedLanguage(embSub) }}
                                            </span>
                                            <span v-if="(movie.embeddedSubtitles?.length || 0) > 4" class="text-secondary-content text-[10px]">
                                                +{{ (movie.embeddedSubtitles?.length || 0) - 4 }} emb
                                            </span>
                                        </div>
                                    </div>
                                </div>
                            </div>

                            <!-- Shows -->
                            <ShowResultCard
                                v-for="show in hierarchicalSearchResults.shows"
                                :key="`show-${show.showId}`"
                                :show="show"
                                @select="openConfigModalFromEpisode" />
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
        :title="selectedEpisode ? `${selectedShow?.title} - ${selectedEpisode.displayTitle} - ${selectedEpisode.title}` : (hierarchicalSearchResults.movies.find((m: MovieResult) => m.subtitles.some((s: Subtitle) => s.path === selectedSubtitlePath))?.title)"
        :poster-path="selectedShow?.posterPath || hierarchicalSearchResults.movies.find((m: MovieResult) => m.subtitles.some((s: Subtitle) => s.path === selectedSubtitlePath))?.posterPath"
        :year="selectedShow?.year || hierarchicalSearchResults.movies.find((m: MovieResult) => m.subtitles.some((s: Subtitle) => s.path === selectedSubtitlePath))?.year"
        :total-lines="selectedTotalLines"
        :default-source-language="defaultSourceLanguage"
        :default-target-language="defaultTargetLanguage"
        :available-source-languages="availableLanguages"
        :available-target-languages="availableLanguages"
        :available-subtitles="availableSubtitles"
        @close="closeConfigModal"
        @start="handleStartTest" />
</template>

<script setup lang="ts">
import { ref, watch, onMounted, nextTick, computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from '@/plugins/i18n'
import { useSettingStore } from '@/store/setting'
import { SETTINGS, ILanguage } from '@/ts'
import useDebounce from '@/composables/useDebounce'
import PageLayout from '@/components/layout/PageLayout.vue'
import TestConfigModal from '@/components/features/translation-test/TestConfigModal.vue'
import TestHistoryList from '@/components/features/translation-test/TestHistoryList.vue'
import TestDebugPanel from '@/components/features/translation-test/TestDebugPanel.vue'
import ShowResultCard from '@/components/features/translation-test/ShowResultCard.vue'

interface Subtitle {
    path: string
    language?: string
    fileName?: string
}

interface EmbeddedSubtitle {
    streamIndex: number
    language?: string
    title?: string
    codecName: string
    isTextBased: boolean
    isDefault: boolean
    isForced: boolean
    isExtracted?: boolean
}

const getEmbeddedBadgeClasses = (sub: EmbeddedSubtitle): string => {
    if (!sub.isTextBased) {
        return 'text-secondary-content/50 border-secondary-content/30 bg-secondary/30 opacity-60'
    }
    if (sub.isExtracted) {
        return 'text-green-300 border-green-500 bg-green-900/30'
    }
    return 'text-amber-300 border-amber-500 bg-amber-900/30'
}

const formatEmbeddedLanguage = (sub: EmbeddedSubtitle): string => {
    if (sub.language) {
        return sub.language.toUpperCase()
    }
    return `#${sub.streamIndex}`
}

interface MovieResult {
    title: string
    movieId: number
    posterPath?: string
    year?: number
    subtitles: Subtitle[]
    embeddedSubtitles?: EmbeddedSubtitle[]
}

interface EpisodePreview {
    episodeId: number
    episodeNumber: number
    title: string
    displayTitle: string
    seasonNumber: number
    subtitles: Subtitle[]
    embeddedSubtitles?: EmbeddedSubtitle[]
}

interface ShowSearchResult {
    title: string
    showId: number
    posterPath?: string
    year?: number
    seasons: {
        seasonNumber: number
        episodes: EpisodePreview[]
    }[]
}

interface MediaSearchResult {
    movies: MovieResult[]
    shows: ShowSearchResult[]
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
const settingStore = useSettingStore()

const searchQuery = ref('')
const hierarchicalSearchResults = ref<MediaSearchResult>({ movies: [], shows: [] })
const isSearching = ref(false)
const isRunning = ref(false)
const logs = ref<LogEntry[]>([])
const logContainer = ref<HTMLElement | null>(null)
const activeTestResult = ref<TestResultDetail | null>(null)
const historyList = ref<{ loadHistory: () => void } | null>(null)

const configModalOpen = ref(false)
const selectedSubtitle = ref<Subtitle | null>(null)
const selectedSubtitlePath = ref('')
const selectedTotalLines = ref(100)
const selectedShow = ref<ShowSearchResult | null>(null)
const selectedEpisode = ref<EpisodePreview | null>(null)
const selectedMediaId = ref<number | null>(null)
const selectedMediaType = ref<'Movie' | 'Episode' | null>(null)
const availableSubtitles = ref<(Subtitle | EmbeddedSubtitle)[]>([])

const sourceLanguages = computed(() => 
    (settingStore.getSetting(SETTINGS.SOURCE_LANGUAGES) as ILanguage[]) || []
)
const targetLanguages = computed(() => 
    (settingStore.getSetting(SETTINGS.TARGET_LANGUAGES) as ILanguage[]) || []
)

const defaultSourceLanguage = computed(() => sourceLanguages.value[0]?.code || 'en')
const defaultTargetLanguage = computed(() => targetLanguages.value[0]?.code || 'pl')

const availableLanguages = computed(() => {
    const allLangs = [...sourceLanguages.value, ...targetLanguages.value]
    const unique = new Map(allLangs.map(l => [l.code, l]))
    return Array.from(unique.values()).map(l => ({ code: l.code, name: l.name }))
})

const performSearch = useDebounce(async (value: string) => {
    const trimmed = value.trim()
    if (trimmed.length < 2) {
        hierarchicalSearchResults.value = { movies: [], shows: [] }
        return
    }

    isSearching.value = true
    try {
        const response = await fetch(`/api/test-translation/search-hierarchical?query=${encodeURIComponent(trimmed)}`)
        if (response.ok) {
            hierarchicalSearchResults.value = await response.json()
        }
    } catch (error) {
        console.error('Search failed:', error)
    } finally {
        isSearching.value = false
    }
}, 300)

watch(searchQuery, (value) => {
    if (value) performSearch(value)
    else hierarchicalSearchResults.value = { movies: [], shows: [] }
})

async function openConfigModalFromMovie(movie: MovieResult) {
    if (movie.subtitles.length === 0 && (!movie.embeddedSubtitles || movie.embeddedSubtitles.length === 0)) return
    
    selectedShow.value = null
    selectedEpisode.value = null
    selectedMediaId.value = movie.movieId
    selectedMediaType.value = 'Movie'
    
    const allSubs: (Subtitle | EmbeddedSubtitle)[] = [
        ...movie.subtitles,
        ...(movie.embeddedSubtitles || [])
    ]
    availableSubtitles.value = allSubs
    
    const firstExternal = movie.subtitles[0]
    selectedSubtitle.value = firstExternal
    selectedSubtitlePath.value = firstExternal.path
    
    try {
        const response = await fetch(`/api/test-translation/subtitle-preview?path=${encodeURIComponent(firstExternal.path)}`)
        if (response.ok) {
            const data = await response.json()
            selectedTotalLines.value = data.totalLines || 100
        }
    } catch {
        selectedTotalLines.value = 100
    }
    
    configModalOpen.value = true
}

async function openConfigModalFromEpisode(episode: EpisodePreview, show: ShowSearchResult) {
    if (episode.subtitles.length === 0 && (!episode.embeddedSubtitles || episode.embeddedSubtitles.length === 0)) return
    
    selectedShow.value = show
    selectedEpisode.value = episode
    selectedMediaId.value = episode.episodeId
    selectedMediaType.value = 'Episode'
    
    const allSubs: (Subtitle | EmbeddedSubtitle)[] = [
        ...episode.subtitles,
        ...(episode.embeddedSubtitles || [])
    ]
    availableSubtitles.value = allSubs
    
    const firstExternal = episode.subtitles[0]
    selectedSubtitle.value = firstExternal
    selectedSubtitlePath.value = firstExternal.path
    
    try {
        const response = await fetch(`/api/test-translation/subtitle-preview?path=${encodeURIComponent(firstExternal.path)}`)
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
    selectedShow.value = null
    selectedEpisode.value = null
    selectedSubtitle.value = null
}

async function handleStartTest(config: {
    subtitlePath: string
    sourceLanguage: string
    targetLanguage: string
    startLine?: number
    endLine?: number
    maxLines?: number
    embeddedStreamIndex?: number
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
                subtitlePath: config.subtitlePath || null,
                sourceLanguage: config.sourceLanguage,
                targetLanguage: config.targetLanguage,
                startLine: config.startLine,
                endLine: config.endLine,
                maxLines: config.maxLines,
                mediaId: selectedMediaId.value,
                mediaType: selectedMediaType.value,
                embeddedStreamIndex: config.embeddedStreamIndex
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
        
        configModalOpen.value = true
    }
})
</script>
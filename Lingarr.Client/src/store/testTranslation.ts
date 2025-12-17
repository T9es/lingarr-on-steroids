import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { MediaType } from '@/ts'

export interface LogEntry {
    level: string
    message: string
    timestamp: string
    details?: string
}

export interface TestResult {
    success: boolean
    errorMessage?: string
    totalSubtitles?: number
    translatedCount?: number
    duration?: number
}

export interface ActiveTest {
    title?: string
    subtitlePath?: string | null
    mediaId?: number | null
    mediaType?: MediaType | null
    sourceLanguage: string
    targetLanguage: string
}

export const useTestTranslationStore = defineStore('testTranslation', () => {
    // State
    const activeTest = ref<ActiveTest | null>(null)
    const isRunning = ref(false)
    const logs = ref<LogEntry[]>([])
    const result = ref<TestResult | null>(null)
    let abortController: AbortController | null = null

    const hasActiveTest = computed(() => activeTest.value !== null)

    // Actions
    function setActiveTest(test: ActiveTest) {
        activeTest.value = test
        result.value = null
        logs.value = []
    }

    function clearActiveTest() {
        activeTest.value = null
        result.value = null
        logs.value = []
    }

    function clearLogs() {
        logs.value = []
        result.value = null
    }

    function addLog(entry: LogEntry) {
        logs.value.push(entry)
    }

    const startTest = async (
        subtitlePath?: string | null,
        sourceLanguage?: string,
        targetLanguage?: string,
        mediaId?: number | null,
        mediaType?: MediaType | null
    ) => {
        if (isRunning.value) return

        // Use provided values or fall back to activeTest values
        const source = sourceLanguage ?? activeTest.value?.sourceLanguage
        const target = targetLanguage ?? activeTest.value?.targetLanguage
        const subPath = subtitlePath !== undefined ? subtitlePath : activeTest.value?.subtitlePath
        const mId = mediaId !== undefined ? mediaId : activeTest.value?.mediaId
        const mType = mediaType !== undefined ? mediaType : activeTest.value?.mediaType

        if (!source || !target) {
            logs.value.push({
                level: 'ERROR',
                message: 'Source and target languages are required',
                timestamp: new Date().toISOString()
            })
            return
        }

        isRunning.value = true
        logs.value = []
        result.value = null

        // Update activeTest with calculated values
        if (activeTest.value) {
            activeTest.value = {
                ...activeTest.value,
                subtitlePath: subPath,
                sourceLanguage: source,
                targetLanguage: target,
                mediaId: mId,
                mediaType: mType
            }
        }

        // Start the stream reader
        await startStreamReader({
            subtitlePath: subPath,
            sourceLanguage: source,
            targetLanguage: target,
            mediaId: mId,
            mediaType: mType
        })
    }

    const startStreamReader = async (request: {
        subtitlePath?: string | null
        sourceLanguage: string
        targetLanguage: string
        mediaId?: number | null
        mediaType?: MediaType | null
    }) => {
        abortController = new AbortController()

        try {
            const response = await fetch('/api/test-translation/start', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(request),
                signal: abortController.signal
            })

            if (!response.body) throw new Error('No response body')

            const reader = response.body.getReader()
            const decoder = new TextDecoder()

            while (isRunning.value) {
                const { done, value } = await reader.read()
                if (done) break

                const text = decoder.decode(value)
                const lines = text.split('\n').filter((line) => line.startsWith('data: '))

                for (const line of lines) {
                    try {
                        const data = JSON.parse(line.substring(6))

                        if (data.type === 'log') {
                            addLog({
                                level: data.Level,
                                message: data.Message,
                                timestamp: data.Timestamp,
                                details: data.Details
                            })
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
            if ((error as Error).name !== 'AbortError') {
                addLog({
                    level: 'ERROR',
                    message: `Connection error: ${error instanceof Error ? error.message : 'Unknown error'}`,
                    timestamp: new Date().toISOString()
                })
            }
        } finally {
            isRunning.value = false
            abortController = null
        }
    }

    async function cancelTest() {
        if (abortController) {
            abortController.abort()
        }

        try {
            await fetch('/api/test-translation/cancel', {
                method: 'POST'
            })
            addLog({
                level: 'WARNING',
                message: 'Cancel request sent...',
                timestamp: new Date().toISOString()
            })
        } catch (error) {
            addLog({
                level: 'ERROR',
                message: `Failed to cancel: ${error instanceof Error ? error.message : 'Unknown error'}`,
                timestamp: new Date().toISOString()
            })
        }
    }

    return {
        activeTest,
        isRunning,
        logs,
        result,
        hasActiveTest,
        setActiveTest,
        clearActiveTest,
        clearLogs,
        startTest,
        cancelTest
    }
})

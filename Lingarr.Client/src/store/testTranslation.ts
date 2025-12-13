import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

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
    subtitlePath?: string | null
    mediaId?: number | null
    mediaType?: number | null
    sourceLanguage: string
    targetLanguage: string
}

export const useTestTranslationStore = defineStore('testTranslation', () => {
    // State
    const activeTest = ref<ActiveTest | null>(null)
    const isRunning = ref(false)
    const logs = ref<LogEntry[]>([]) // Kept LogEntry as TestLogEntry was not defined
    const result = ref<TestResult | null>(null)
    let eventSource: EventSource | null = null // Replaced abortController with eventSource

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
        subtitlePath: string | null,
        sourceLanguage: string,
        targetLanguage: string,
        mediaId?: number | null,
        mediaType?: number | null
    ) => {
        if (isRunning.value) return

        isRunning.value = true
        logs.value = []
        result.value = null
        activeTest.value = { subtitlePath, sourceLanguage, targetLanguage, mediaId, mediaType }

        try {
            // Initial POST request to start the process on the server
            await axios.post('/api/test-translation/start', {
                subtitlePath,
                sourceLanguage,
                targetLanguage,
                mediaId,
                mediaType
            })

            // Re-connect with EventSource for real streaming
            connectEventSource({ subtitlePath, sourceLanguage, targetLanguage, mediaId, mediaType })

        } catch (error) {
            console.error('Failed to start test translation', error)
            isRunning.value = false
            logs.value.push({
                level: 'ERROR',
                message: 'Failed to start test: ' + (error as Error).message,
                timestamp: new Date().toISOString()
            })
        }
    }

    const connectEventSource = (request: any) => {
        // Close existing if any
        if (eventSource) {
            eventSource.close()
        }

        // In a real implementation with SSE via POST (not standard), we might need a library like @microsoft/fetch-event-source
        // But for standard GET SSE, we'd use EventSource.
        // For this implementation, we'll assume the controller handles the POST and keeps the connection open
        // OR we use fetch to read the stream manually.

        // Let's implement manually reading the stream using fetch api since EventSource doesn't support POST
        startStreamReader(request)
    }

    const processChunk = (chunk: string) => {
        const lines = chunk.split('\n').filter((line) => line.startsWith('data: '))

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
                    isRunning.value = false // Test finished
                }
            } catch {
                // Skip malformed JSON
            }
        }
    }

    const startStreamReader = async (request: any) => {
        try {
            const response = await fetch('/api/test-translation/start', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(request)
            })

            if (!response.body) throw new Error('No response body')

            const reader = response.body.getReader()
            const decoder = new TextDecoder()

            while (isRunning.value) { // Continue reading as long as test is running
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
            abortController.value = null
        }
    }

    async function cancelTest() {
        if (abortController.value) {
            abortController.value.abort()
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

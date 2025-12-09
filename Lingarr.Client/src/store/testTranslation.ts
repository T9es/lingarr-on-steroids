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
    subtitlePath: string
    sourceLanguage: string
    targetLanguage: string
    title: string
}

export const useTestTranslationStore = defineStore('testTranslation', () => {
    const activeTest = ref<ActiveTest | null>(null)
    const isRunning = ref(false)
    const logs = ref<LogEntry[]>([])
    const result = ref<TestResult | null>(null)
    const abortController = ref<AbortController | null>(null)

    const hasActiveTest = computed(() => activeTest.value !== null)

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

    async function startTest() {
        if (!activeTest.value || isRunning.value) return

        isRunning.value = true
        result.value = null
        logs.value = []
        abortController.value = new AbortController()

        try {
            const response = await fetch('/api/test-translation/start', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    subtitlePath: activeTest.value.subtitlePath,
                    sourceLanguage: activeTest.value.sourceLanguage,
                    targetLanguage: activeTest.value.targetLanguage
                }),
                signal: abortController.value.signal
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

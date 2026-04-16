export interface TranslationCompareLine {
    position: number
    original: string
    translated?: string
    success: boolean
    error?: string
    durationMs?: number
    startTimeMs?: number
    endTimeMs?: number
}

export interface CompletedTranslationCompareResponse {
    translationRequestId: number
    title: string
    sourceLanguage: string
    targetLanguage: string
    mediaType: string
    completedAt: string | null
    originalSubtitlePath: string
    translatedSubtitlePath: string
    originalLineCount: number
    translatedLineCount: number
    lines: TranslationCompareLine[]
}

import { ISubtitle } from '@/ts'

interface IBaseEntity {
    id: number
    createdAt: Date
    updatedAt: Date
}

export interface IShow extends IBaseEntity {
    sonarrId: number
    title: string
    path: string
    dateAdded?: Date | null
    images: IImage[]
    seasons: ISeason[]
    excludeFromTranslation: string
    translationAgeThreshold: string
    isPriority: boolean
    priorityDate?: Date | null
    sourceInstanceId?: string | null
}

export interface IEmbeddedSubtitle {
    id: number
    streamIndex: number
    language: string | null
    title: string | null
    codecName: string
    isTextBased: boolean
    isDefault: boolean
    isForced: boolean
    isExtracted: boolean
    extractedPath: string | null
    ocrStatus: SubtitleOcrStatus
    ocrExtractedPath: string | null
    ocrError: string | null
    ocrAttemptedAt: string | null
    ocrCompletedAt: string | null
    ocrCueCount: number | null
    ocrQualityScore: number | null
    ocrIssueSummary: string | null
    ocrApprovedAt: string | null
    isOcrSupported: boolean
    isOcrUsable: boolean
}

export type SubtitleOcrStatus =
    | 'NotStarted'
    | 'Queued'
    | 'Processing'
    | 'Succeeded'
    | 'BlockedLowQuality'
    | 'Failed'
    | 'Approved'

export interface ISubtitleOcrPreviewLine {
    position: number
    startTime: number
    endTime: number
    text: string
}

export interface ISubtitleOcrPreview {
    success: boolean
    status: SubtitleOcrStatus
    cueCount: number | null
    qualityScore: number | null
    issueSummary: string | null
    error: string | null
    lines: ISubtitleOcrPreviewLine[]
}

export interface IMovie extends IBaseEntity {
    radarrId: number
    title: string
    fileName: string
    path: string
    dateAdded?: Date | null
    images: IImage[]
    subtitles?: ISubtitle[]
    embeddedSubtitles?: IEmbeddedSubtitle[]
    excludeFromTranslation: string
    translationAgeThreshold: string
    isPriority: boolean
    priorityDate?: Date | null
    translationState?: TranslationStateType
    sourceInstanceId?: string | null
}

export interface ITranslationRequest {
    id: number
    jobId: string
    title: string
    workloadKind?: TranslationWorkloadKind
    workloadItemKey?: string
    workloadSourceLabel?: string
    sourceLanguage: string
    targetLanguage: string
    subtitleToTranslate?: string
    translatedSubtitle?: string
    mediaType: MediaType
    mediaId?: number | null
    customMediaItemId?: number | null
    uploadBatchFileId?: number | null
    status: TranslationStatus
    progress: number
    completedAt?: string | null
    latestFailureMessage?: string | null
    isPriority?: boolean
    isActive?: boolean
    startedAt?: string | null
    pausedAt?: string | null
    pauseReason?: string | null
    pausedProvider?: string | null
    nextRetryAt?: string | null
}

export interface ITranslationRequestLog {
    id: number
    level: string
    message: string
    details?: string | null
    createdAt: string
}

export interface IRetryFailedRequestsResponse {
    totalFailed: number
    retried: number
    blockedByActiveRequest: number
    remainingFailed: number
    message: string
}

export interface IRetryTranslationRequestResponse {
    requestId: number
    retried: boolean
    blockedByActiveRequest: boolean
    message: string
}

export interface ITranslationRequestSection {
    items: ITranslationRequest[]
    totalCount: number
}

export interface ITranslationRequestsOverview {
    activeCount: number
    pending: IPagedResult<ITranslationRequest>
    failed: ITranslationRequestSection
    inProgress: ITranslationRequestSection
}

export interface IRequestProgress {
    id: number
    jobId: string
    title: string
    mediaType: MediaType
    sourceLanguage: string
    targetLanguage: string
    startedAt?: string | null
    status: TranslationStatus
    progress: number
    completed: boolean
    completedAt?: string | null
}

export interface IImage {
    id: number
    type: string
    path: string
    showId?: number | null
    show?: IShow | null
    movieId?: number | null
    movie?: IMovie | null
}

export interface ISeason extends IBaseEntity {
    seasonNumber: number
    path: string
    episodes: IEpisode[]
    showId: number
    show: IShow
    excludeFromTranslation: string
}

export interface IEpisode extends IBaseEntity {
    sonarrId: number
    episodeNumber: number
    title: string
    fileName?: string | null
    path?: string | null
    seasonId: number
    season: ISeason
    excludeFromTranslation: string
    embeddedSubtitles?: IEmbeddedSubtitle[]
    translationState?: TranslationStateType
}

export interface IPagedResult<T> {
    items: T[]
    totalCount: number
    pageNumber: number
    pageSize: number
}

export interface ICustomSource extends IBaseEntity {
    name: string
    sourceType: 'MovieRoot' | 'ShowRoot'
    rootPath: string
    recursive: boolean
    enabled: boolean
    includeInAutomation: boolean
    lastScannedAt?: string | null
    lastScanResult?: string | null
    lastScanError?: string | null
    items?: ICustomMediaItem[]
}

export interface ICustomMediaItem extends IBaseEntity {
    customSourceId: number
    itemKind: 'Movie' | 'Episode'
    title: string
    fileName: string
    path: string
    relativePath: string
    mediaHash?: string | null
    dateAdded?: string | null
    translationState?: TranslationStateType
    indexedAt?: string | null
    stateSettingsVersion?: number
    lastSubtitleCheckAt?: string | null
    excludeFromTranslation: boolean
    isPriority: boolean
    priorityDate?: string | null
    seriesTitle?: string | null
    seasonNumber?: number | null
    episodeNumber?: number | null
}

export const MEDIA_TYPE = {
    MOVIE: 'Movie',
    SHOW: 'Show',
    SEASON: 'Season',
    EPISODE: 'Episode'
} as const

export type MediaType = (typeof MEDIA_TYPE)[keyof typeof MEDIA_TYPE]

export interface IBlockedMediaItem {
    mediaId: number
    mediaType: 'movie' | 'episode'
    title: string
    translationState: TranslationStateValue
    streamIndex?: number | null
    ocrStatus?: number | string | null
    ocrQualityScore?: number | null
    ocrMinimumQualityScore?: number | null
    ocrPassesCurrentThreshold?: boolean
    ocrIssueSummary?: string | null
    failureMessage?: string | null
    lastSubtitleCheckAt?: string | null
    lastEvaluatedAt?: string | null
}

export interface IBlockedMediaPage extends IPagedResult<IBlockedMediaItem> {
    countsByState: Record<string, number>
}

export interface IBlockedMediaRecheckResult {
    mediaId: number
    mediaType: 'movie' | 'episode'
    state: TranslationStateValue
    evaluatedAt: string
}

export const TRANSLATION_WORKLOAD_KIND = {
    LIBRARY: 'Library',
    CUSTOM_SOURCE: 'CustomSource',
    UPLOAD: 'Upload'
} as const

export type TranslationWorkloadKind =
    (typeof TRANSLATION_WORKLOAD_KIND)[keyof typeof TRANSLATION_WORKLOAD_KIND]

export const TRANSLATION_STATUS = {
    PENDING: 'Pending',
    INPROGRESS: 'InProgress',
    COMPLETED: 'Completed',
    FAILED: 'Failed',
    CANCELLED: 'Cancelled',
    INTERRUPTED: 'Interrupted',
    PAUSED: 'Paused'
} as const

export type TranslationStatus = (typeof TRANSLATION_STATUS)[keyof typeof TRANSLATION_STATUS]

export enum TRANSLATION_ACTIONS {
    CANCEL,
    REMOVE,
    RETRY
}

export const TRANSLATION_STATE = {
    UNKNOWN: 0,
    NOT_APPLICABLE: 1,
    PENDING: 2,
    IN_PROGRESS: 3,
    COMPLETE: 4,
    STALE: 5,
    NO_SUITABLE_SUBTITLES: 6,
    FAILED: 7,
    AWAITING_SOURCE: 8,
    OCR_PENDING: 9,
    OCR_BLOCKED: 10
} as const

export type TranslationStateType = (typeof TRANSLATION_STATE)[keyof typeof TRANSLATION_STATE]

/**
 * A translation state value as it can arrive from the API (enum name string by
 * default JSON serialization), from an older payload (number), or from local
 * state. Components normalize it through `normalizeTranslationState`.
 */
export type TranslationStateValue = TranslationStateType | string | number | null | undefined

const TRANSLATION_STATE_BY_NAME: Record<string, TranslationStateType> = {
    unknown: TRANSLATION_STATE.UNKNOWN,
    notapplicable: TRANSLATION_STATE.NOT_APPLICABLE,
    pending: TRANSLATION_STATE.PENDING,
    inprogress: TRANSLATION_STATE.IN_PROGRESS,
    complete: TRANSLATION_STATE.COMPLETE,
    stale: TRANSLATION_STATE.STALE,
    nosuitablesubtitles: TRANSLATION_STATE.NO_SUITABLE_SUBTITLES,
    failed: TRANSLATION_STATE.FAILED,
    awaitingsource: TRANSLATION_STATE.AWAITING_SOURCE,
    ocrpending: TRANSLATION_STATE.OCR_PENDING,
    ocrblocked: TRANSLATION_STATE.OCR_BLOCKED
}

/**
 * Normalizes a translation state that may be a numeric enum value or the enum
 * name string emitted by the backend's JsonStringEnumConverter.
 */
export const normalizeTranslationState = (
    value: TranslationStateValue
): TranslationStateType => {
    if (typeof value === 'number' && Number.isFinite(value)) {
        return value as TranslationStateType
    }
    if (typeof value === 'string') {
        const normalized = value.trim().toLowerCase()
        if (!normalized) {
            return TRANSLATION_STATE.UNKNOWN
        }
        if (/^\d+$/.test(normalized)) {
            return Number(normalized) as TranslationStateType
        }
        return TRANSLATION_STATE_BY_NAME[normalized] ?? TRANSLATION_STATE.UNKNOWN
    }
    return TRANSLATION_STATE.UNKNOWN
}

export type BlockedMediaStateFilter =
    | 'all'
    | 'OcrBlocked'
    | 'Failed'
    | 'Stale'
    | 'AwaitingSource'

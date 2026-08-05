import {
    DirectoryItem,
    IBlockedMediaItem,
    ICustomMediaItem,
    ICustomSource,
    ICreateUploadBatchRequest,
    ICreateUploadChunkSessionRequest,
    ICreateUploadChunkSessionResponse,
    ILanguage,
    ISettings,
    ISubtitle,
    ITranslationRequest,
    ITranslationRequestLog,
    ITranslationRequestsOverview,
    IRetryFailedRequestsResponse,
    IRetryTranslationRequestResponse,
    IUploadChunkResponse,
    IUpdateUploadBatchFileRequest,
    IUpdateUploadBatchRequest,
    IUploadArtifact,
    IUploadBatch,
    IUploadBatchFile,
    MediaType,
    UploadProgressCallback
} from '@/ts'
import { ISubtitleOcrPreview } from '@/ts/media'
import { IPathMapping } from '@/ts/index'

export interface Services {
    setting: ISettingService
    subtitle: ISubtitleService
    blockedMedia: IBlockedMediaService
    translate: ITranslateService
    chutes: IChutesService
    nanoGpt: INanoGptService
    crofAi: ICrofAiService
    tokenUsage: ITokenUsageService
    translationRequest: ITranslationRequestService
    version: IVersionService
    media: IMediaService
    schedule: IScheduleService
    mapping: IMappingService
    customSources: ICustomSourceService
    directory: IDirectoryService
    uploadWorkspace: IUploadWorkspaceService
    statistics: IStatisticsService
    logs: ILogsService
    dashboard: IDashboardService
}

export interface IMediaService {
    movies<T>(
        pageNumber: number,
        searchQuery: string,
        orderBy: string,
        ascending: boolean
    ): Promise<T>
    shows<T>(
        pageNumber: number,
        searchQuery: string,
        orderBy: string,
        ascending: boolean
    ): Promise<T>
    show<T>(id: number): Promise<T>
    exclude<T>(mediaType: MediaType, id: number): Promise<T>
    threshold<T>(mediaType: MediaType, id: number, hours: string): Promise<T>
    priority<T>(mediaType: MediaType, id: number): Promise<T>
    integrityCheck<T>(mediaType: MediaType, id: number): Promise<T>
}

export interface IBlockedMediaService {
    getBlockedMedia<T = IBlockedMediaItem[]>(limit?: number): Promise<T>
}

export interface ISettingService {
    getSetting<T>(key: string): Promise<T>
    getSettings<T>(keys: string[]): Promise<T>
    getEncryptedSettings<T>(keys: string[]): Promise<T>
    setSetting(key: string, value: string): Promise<void>
    setEncryptedSetting(key: string, value: string): Promise<void>
    setSettings(keys: ISettings): Promise<void>
    getCleanupDuplicatePreview<T>(): Promise<T>
    cleanupDuplicateInstances<T>(): Promise<T>
    getSystemLimits<T>(): Promise<T>
    testRadarrConnection<T>(): Promise<T>
    testSonarrConnection<T>(): Promise<T>
    testRadarrInstance<T>(request: { url: string; apiKey: string }): Promise<T>
    testSonarrInstance<T>(request: { url: string; apiKey: string }): Promise<T>
}

export interface ISubtitleService {
    collect<T>(path: string): Promise<T>
    getEmbeddedSubtitles<T>(mediaType: 'movie' | 'episode', mediaId: number): Promise<T>
    extractSubtitle(
        mediaType: 'movie' | 'episode',
        mediaId: number,
        streamIndex: number
    ): Promise<{ success: boolean; extractedPath: string | null; error: string | null }>
    queueOcr(
        mediaType: 'movie' | 'episode',
        mediaId: number,
        streamIndex: number
    ): Promise<{
        success: boolean
        status: string
        extractedPath: string | null
        error: string | null
        cueCount: number | null
        qualityScore: number | null
        issueSummary: string | null
    }>
    approveOcr(
        mediaType: 'movie' | 'episode',
        mediaId: number,
        streamIndex: number
    ): Promise<{
        success: boolean
        status: string
        extractedPath: string | null
        error: string | null
        cueCount: number | null
        qualityScore: number | null
        issueSummary: string | null
    }>
    previewOcr(
        mediaType: 'movie' | 'episode',
        mediaId: number,
        streamIndex: number
    ): Promise<ISubtitleOcrPreview>
    probeEmbeddedSubtitles<T>(mediaType: 'movie' | 'episode', mediaId: number): Promise<T>
}

export interface IVersionService {
    getVersion<T>(): Promise<T>
}

export interface ITranslateService {
    translateSubtitle<T>(
        mediaId: number,
        subtitle: ISubtitle,
        source: string,
        target: ILanguage,
        mediaType: MediaType
    ): Promise<T>
    translateMedia<T>(mediaId: number, mediaType: MediaType, forceRecreate?: boolean): Promise<T>
    recreateAllMedia<T>(): Promise<T>
    reconcileOutputs<T>(): Promise<T>
    getLanguages<T>(): Promise<T>
    getModels<T>(): Promise<T>
}

export interface IChutesService {
    getUsage<T>(forceRefresh?: boolean): Promise<T>
}

export interface INanoGptService {
    getUsage<T>(forceRefresh?: boolean): Promise<T>
}

export interface ICrofAiService {
    getUsage<T>(forceRefresh?: boolean): Promise<T>
}

export interface ITokenUsageService {
    getUsage<T>(service: string): Promise<T>
    setChutesMode(mode: 'subscription' | 'payg'): Promise<void>
    getChutesMode<T>(): Promise<T>
}

export interface ITranslationRequestService {
    getActiveCount<T>(): Promise<T>
    getFailedRequests<T>(): Promise<T>
    getInProgressRequests<T>(): Promise<T>
    overview(
        pageNumber: number,
        searchQuery: string,
        sortBy: string,
        ascending: boolean,
        pageSize?: number,
        sectionLimit?: number
    ): Promise<ITranslationRequestsOverview>
    getRecentCompleted<T>(offset?: number, limit?: number): Promise<T>
    requests<T>(
        pageNumber: number,
        searchQuery: string,
        sortBy: string,
        ascending: boolean
    ): Promise<T>
    cancel<T>(translationRequest: ITranslationRequest): Promise<T>
    remove<T>(translationRequest: ITranslationRequest): Promise<T>
    retry(translationRequest: ITranslationRequest): Promise<IRetryTranslationRequestResponse>
    retryAllFailed(): Promise<IRetryFailedRequestsResponse>
    removeAllFailed<T>(): Promise<T>
    reenqueueQueued<T>(includeInProgress?: boolean): Promise<T>
    cancelAll<T>(includeInProgress?: boolean): Promise<T>
    logs<T extends ITranslationRequestLog[]>(translationRequestId: number): Promise<T>
}

export interface IScheduleService {
    startJob<T>(jobName: string): Promise<T>
    recurringJobs<T>(): Promise<T>
    remove<T>(jobId: string): Promise<T>
    indexShows<T>(): Promise<T>
    indexMovies<T>(): Promise<T>
}

export interface IMappingService {
    getMappings(): Promise<IPathMapping[]>
    setMappings(mappings: IPathMapping[]): Promise<void>
}

export interface IDirectoryService {
    get(path: string): Promise<DirectoryItem[]>
}

export interface ICustomSourceService {
    getSources(): Promise<ICustomSource[]>
    getSource<T>(id: number): Promise<T>
    createSource<T>(source: Partial<ICustomSource>): Promise<T>
    updateSource<T>(id: number, source: Partial<ICustomSource>): Promise<T>
    deleteSource(id: number): Promise<void>
    getItems(id: number): Promise<ICustomMediaItem[]>
    rescan(id: number): Promise<void>
    rescanAll(): Promise<void>
    setExcluded(itemId: number, excluded: boolean): Promise<void>
    setPriority(itemId: number, priority: boolean): Promise<void>
    translate(
        itemId: number,
        forceRecreate?: boolean
    ): Promise<{ translationsQueued: number; message: string }>
}

export interface IUploadWorkspaceService {
    createBatch(request: ICreateUploadBatchRequest): Promise<IUploadBatch>
    listBatches(): Promise<IUploadBatch[]>
    getBatch(batchId: number): Promise<IUploadBatch>
    updateBatch(batchId: number, request: IUpdateUploadBatchRequest): Promise<IUploadBatch>
    deleteBatch(batchId: number): Promise<void>
    createChunkSession(
        batchId: number,
        request: ICreateUploadChunkSessionRequest
    ): Promise<ICreateUploadChunkSessionResponse>
    uploadChunk(
        batchId: number,
        uploadId: string,
        chunkIndex: number,
        blob: Blob,
        onProgress?: UploadProgressCallback
    ): Promise<IUploadChunkResponse>
    completeChunkSession(batchId: number, uploadId: string): Promise<IUploadBatch>
    cancelChunkSession(batchId: number, uploadId: string): Promise<void>
    uploadFiles(
        batchId: number,
        files: File[],
        onProgress?: UploadProgressCallback
    ): Promise<IUploadBatch>
    reprobeFile(batchId: number, fileId: number): Promise<IUploadBatchFile>
    updateFile(
        batchId: number,
        fileId: number,
        request: IUpdateUploadBatchFileRequest
    ): Promise<IUploadBatchFile>
    startBatch<T = number>(batchId: number): Promise<T>
    cancelBatch<T = boolean>(batchId: number): Promise<T>
    listArtifacts(batchId: number, fileId?: number): Promise<IUploadArtifact[]>
    downloadArtifact(artifactId: number): Promise<Blob>
    deleteArtifact(artifactId: number): Promise<void>
}

export interface IStatisticsService {
    getStatistics<T>(): Promise<T>
    getDailyStatistics<T>(days?: number): Promise<T>
    getFilteredStatistics<T>(startDate?: Date, endDate?: Date): Promise<T>
}

export interface ILogsService {
    getStream(includeRecent?: boolean): EventSource
    getRecent<T>(take?: number): Promise<T>
}

export interface IDashboardService {
    getLayout<T>(): Promise<T | null>
    saveLayout(layoutJson: string): Promise<void>
    resetLayout(): Promise<void>
    getJobs<T>(): Promise<T>
    getApiUsage<T>(): Promise<T>
    clearFailedJobs(): Promise<{ cleared: number }>
    getFailedJobs(
        offset?: number,
        limit?: number
    ): Promise<{
        jobs: unknown[]
        totalCount: number
        hasMore: boolean
    }>
}

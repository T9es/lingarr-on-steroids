import {
    DirectoryItem,
    ILanguage,
    ISettings,
    ISubtitle,
    ITranslationRequestLog,
    ITranslationRequest,
    MediaType
} from '@/ts'
import { IPathMapping } from '@/ts/index'

export interface Services {
    setting: ISettingService
    subtitle: ISubtitleService
    translate: ITranslateService
    chutes: IChutesService
    translationRequest: ITranslationRequestService
    version: IVersionService
    media: IMediaService
    schedule: IScheduleService
    mapping: IMappingService
    directory: IDirectoryService
    statistics: IStatisticsService
    logs: ILogsService
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
    exclude<T>(mediaType: MediaType, id: number): Promise<T>
    threshold<T>(mediaType: MediaType, id: number, hours: string): Promise<T>
    priority<T>(mediaType: MediaType, id: number): Promise<T>
}

export interface ISettingService {
    getSetting<T>(key: string): Promise<T>
    getSettings<T>(keys: string[]): Promise<T>
    setSetting(key: string, value: string): Promise<void>
    setSettings(keys: ISettings): Promise<void>
    getSystemLimits<T>(): Promise<T>
}

export interface ISubtitleService {
    collect<T>(path: string): Promise<T>
    getEmbeddedSubtitles<T>(mediaType: 'movie' | 'episode', mediaId: number): Promise<T>
    extractSubtitle(
        mediaType: 'movie' | 'episode',
        mediaId: number,
        streamIndex: number
    ): Promise<{ success: boolean; extractedPath: string | null; error: string | null }>
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
    translateMedia<T>(mediaId: number, mediaType: MediaType): Promise<T>
    getLanguages<T>(): Promise<T>
    getModels<T>(): Promise<T>
}

export interface IChutesService {
    getUsage<T>(forceRefresh?: boolean): Promise<T>
}

export interface ITranslationRequestService {
    getActiveCount<T>(): Promise<T>
    requests<T>(
        pageNumber: number,
        searchQuery: string,
        sortBy: string,
        ascending: boolean
    ): Promise<T>
    cancel<T>(translationRequest: ITranslationRequest): Promise<T>
    remove<T>(translationRequest: ITranslationRequest): Promise<T>
    retry<T>(translationRequest: ITranslationRequest): Promise<T>
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

export interface IStatisticsService {
    getStatistics<T>(): Promise<T>
    getDailyStatistics<T>(days?: number): Promise<T>
}

export interface ILogsService {
    getStream(): EventSource
}

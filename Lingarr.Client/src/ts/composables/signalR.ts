import { IRequestProgress, ISettings } from '@/ts'
import type { HubConnection as SignalRHubConnection } from '@microsoft/signalr'

export interface SignalRStore {
    state: SignalRState
    connect: (hubName: string, url: string) => Promise<Hub>
}

export interface SignalRState {
    hubs: Record<string, HubConnection>
}

interface HubConnection {
    connection: SignalRHubConnection
    isConnected: boolean
    lastError: Error | null
}

export type EventCallbacks = {
    GroupCompleted: (group: string) => void
    SettingUpdate: (setting: { key: keyof ISettings; value: string }) => void
    RequestProgress: (requestProgress: IRequestProgress) => void
    RequestActive: (request: { count: number }) => void
    JobProgressUpdated: (jobId: string, progress: number) => void
    JobStateUpdated: (jobId: string, state: string) => void
    BulkIntegrityProgress: (stats: {
        total: number
        totalMovies: number
        totalEpisodes: number
        processedCount: number
        validCount: number
        corruptCount: number
        queuedCount: number
        errorCount: number
        autoQueueEnabled: boolean
        maxAutoQueuePerRun: number
        flaggedItems?: Array<{
            mediaId: number
            mediaType: string
            mediaTitle: string
            sourceLanguage: string
            targetLanguage: string
            sourceRole: string
            reason: string
            sourcePath: string | null
            targetPath: string | null
            sourceEntries: number | null
            targetEntries: number | null
            minimumTargetEntries: number | null
            sourceSnapshotType: string | null
            sourceSnapshotIdentity: string | null
            sourceSnapshotStreamIndex: number | null
            isQueued: boolean
            dismissed: boolean
        }>
        isComplete: boolean
        error: string | null
        progressPercent: number
    }) => void
    AssVerificationProgress: (stats: {
        total: number
        processedCount: number
        isComplete: boolean
        isRunning: boolean
        error: string | null
        progressPercent: number
    }) => void
    SubtitleTypeValidationProgress: (stats: {
        total: number
        processedCount: number
        incompleteCount: number
        isComplete: boolean
        isRunning: boolean
        error: string | null
        progressPercent: number
    }) => void
}

export interface Hub {
    joinGroup: (groupName: { group: string }) => Promise<void>
    leaveGroup: (groupName: { group: string }) => Promise<void>
    send: (event: string, ...args: unknown[]) => Promise<void>
    on<K extends keyof EventCallbacks>(event: K, callback: EventCallbacks[K]): void
    off<K extends keyof EventCallbacks>(event: K, callback: EventCallbacks[K]): void
}

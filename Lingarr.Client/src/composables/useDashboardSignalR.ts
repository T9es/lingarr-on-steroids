import { ref } from 'vue'
import axios from 'axios'
import { useSignalR } from './useSignalR'
import type { IRequestProgress } from '@/ts'

export interface ActiveTranslation {
    id: number
    jobId: string
    progress: number
    status: string
    startedAt: Date
}

export interface DashboardRealtimeState {
    isConnected: boolean
    activeTranslations: Map<number, ActiveTranslation>
    activeCount: number
    lastUpdate: Date | null
}

export function useDashboardSignalR() {
    const signalR = useSignalR()

    const state = ref<DashboardRealtimeState>({
        isConnected: false,
        activeTranslations: new Map(),
        activeCount: 0,
        lastUpdate: null
    })

    const handleRequestProgress = (progress: IRequestProgress) => {
        const existing = state.value.activeTranslations.get(progress.id)

        if (progress.status === 'Completed' || progress.status === 'Failed') {
            state.value.activeTranslations.delete(progress.id)
            state.value.activeCount = state.value.activeTranslations.size
        } else {
            const translation: ActiveTranslation = {
                id: progress.id,
                jobId: progress.jobId,
                progress: progress.progress,
                status: progress.status,
                startedAt: existing?.startedAt || new Date()
            }
            state.value.activeTranslations.set(progress.id, translation)
            state.value.activeCount = state.value.activeTranslations.size
        }

        state.value.lastUpdate = new Date()
    }

    const handleRequestActive = (request: { count: number }) => {
        state.value.activeCount = request.count
        state.value.lastUpdate = new Date()
    }

    /**
     * Loads initial running translations from the REST API.
     * This seeds the activeTranslations Map with currently running jobs
     * that were already in progress before the page loaded.
     * Called once on mount, before SignalR events start arriving.
     */
    const loadInitialTranslations = async () => {
        try {
            const response = await axios.get('/api/dashboard/jobs')
            const data = response.data

            // Handle various response formats (defensive)
            const rawJobs = Array.isArray(data)
                ? data
                : data?.jobs || data?.Jobs || []

            if (!Array.isArray(rawJobs) || rawJobs.length === 0) {
                return
            }

            // Filter for running translations only
            const runningJobs = rawJobs.filter(
                (job: { state?: string; State?: string }) =>
                    job?.state === 'Running' || job?.State === 'Running'
            )

            let addedCount = 0
            for (const job of runningJobs) {
                // Extract ID - handle both "translation-123" format and raw IDs
                const rawId = job?.id ?? job?.Id ?? job?.jobId ?? job?.JobId
                if (rawId === undefined || rawId === null) continue

                // Parse numeric ID from "translation-123" format, or use raw value
                let numericId: number
                if (typeof rawId === 'string' && rawId.startsWith('translation-')) {
                    const parsed = parseInt(rawId.replace('translation-', ''), 10)
                    if (isNaN(parsed)) continue
                    numericId = parsed
                } else if (typeof rawId === 'number') {
                    numericId = rawId
                } else if (typeof rawId === 'string') {
                    const parsed = parseInt(rawId, 10)
                    if (isNaN(parsed)) continue
                    numericId = parsed
                } else {
                    continue
                }

                // Don't overwrite if already in Map (SignalR may have added it)
                if (state.value.activeTranslations.has(numericId)) {
                    continue
                }

                // Parse startedAt date
                const startedAtRaw = job?.startedAt ?? job?.StartedAt
                let startedAt: Date
                if (startedAtRaw) {
                    const parsed = new Date(startedAtRaw)
                    startedAt = isNaN(parsed.getTime()) ? new Date() : parsed
                } else {
                    startedAt = new Date()
                }

                const translation: ActiveTranslation = {
                    id: numericId,
                    jobId: String(rawId),
                    progress: typeof job?.progress === 'number' ? job.progress : (typeof job?.Progress === 'number' ? job.Progress : 0),
                    status: 'InProgress',
                    startedAt
                }

                state.value.activeTranslations.set(numericId, translation)
                addedCount++
            }

            if (addedCount > 0) {
                state.value.activeCount = state.value.activeTranslations.size
                state.value.lastUpdate = new Date()
            }
        } catch (error) {
            // Log but don't throw - initial load failure shouldn't break the UI
            console.warn('Failed to load initial translations:', error)
        }
    }

    const connect = async () => {
        try {
            const hub = await signalR.connect('translationRequests', '/signalr/TranslationRequests')

            await hub.joinGroup({ group: 'TranslationRequests' })

            hub.on('RequestProgress', handleRequestProgress)
            hub.on('RequestActive', handleRequestActive)

            state.value.isConnected = true
        } catch (error) {
            console.error('Failed to connect to dashboard SignalR:', error)
            state.value.isConnected = false
        }
    }

    const disconnect = async () => {
        const hubState = signalR.state.hubs['translationRequests']
        if (hubState?.connection) {
            const hub = {
                on: hubState.connection.on.bind(hubState.connection),
                off: hubState.connection.off.bind(hubState.connection)
            }
            hub.off('RequestProgress', handleRequestProgress)
            hub.off('RequestActive', handleRequestActive)
        }
        state.value.isConnected = false
    }

    const getActiveTranslations = (): ActiveTranslation[] => {
        return Array.from(state.value.activeTranslations.values())
    }

    return {
        state,
        connect,
        disconnect,
        getActiveTranslations,
        loadInitialTranslations
    }
}

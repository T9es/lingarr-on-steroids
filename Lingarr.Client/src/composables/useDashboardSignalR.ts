import { ref } from 'vue'
import axios from 'axios'
import { useSignalR } from './useSignalR'
import type { IRequestProgress } from '@/ts'

export interface ActiveTranslation {
    id: number
    jobId: string
    title: string
    mediaType: string
    sourceLanguage: string
    targetLanguage: string
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
                title: progress.title,
                mediaType: progress.mediaType,
                sourceLanguage: progress.sourceLanguage,
                targetLanguage: progress.targetLanguage,
                progress: progress.progress,
                status: progress.status,
                startedAt:
                    existing?.startedAt ||
                    (progress.startedAt ? new Date(progress.startedAt) : new Date())
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
            const response = await axios.get('/api/translation-request/inprogress')
            const requests = response.data

            if (!Array.isArray(requests) || requests.length === 0) {
                return
            }

            let addedCount = 0
            for (const request of requests) {
                const numericId = request.id
                if (!numericId) continue

                // Don't overwrite if already in Map (SignalR may have added it)
                if (state.value.activeTranslations.has(numericId)) {
                    continue
                }

                const translation: ActiveTranslation = {
                    id: numericId,
                    jobId: String(numericId),
                    title: request.title ?? `Translation #${numericId}`,
                    mediaType: request.mediaType ?? 'Movie',
                    sourceLanguage: request.sourceLanguage ?? '',
                    targetLanguage: request.targetLanguage ?? '',
                    progress: request.progress ?? 0,
                    status: 'InProgress',
                    startedAt: request.startedAt ? new Date(request.startedAt) : new Date()
                }

                state.value.activeTranslations.set(numericId, translation)
                addedCount++
            }

            if (addedCount > 0) {
                state.value.activeCount = state.value.activeTranslations.size
                state.value.lastUpdate = new Date()
            }
        } catch (error) {
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

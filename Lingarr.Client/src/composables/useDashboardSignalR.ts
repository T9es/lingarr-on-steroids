import { ref } from 'vue'
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

    const connect = async () => {
        try {
            const hub = await signalR.connect('translationRequests', '/hubs/translation-requests')

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
        getActiveTranslations
    }
}

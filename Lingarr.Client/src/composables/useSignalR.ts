import { reactive } from 'vue'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { EventCallbacks, Hub, SignalRState, SignalRStore } from '@/ts/composables/signalR'

let signalRInstance: SignalRStore | null = null

export const createSignalRStore = (): SignalRStore => {
    const state = reactive<SignalRState>({
        hubs: {}
    })

    const connectionPromises = new Map<string, Promise<void>>()
    const joinedGroups = new Map<string, Map<string, { group: string }>>()

    const getJoinedGroups = (hubName: string): Map<string, { group: string }> => {
        const existing = joinedGroups.get(hubName)
        if (existing) {
            return existing
        }

        const groups = new Map<string, { group: string }>()
        joinedGroups.set(hubName, groups)
        return groups
    }

    const rejoinGroups = async (hubName: string): Promise<void> => {
        const hubConnection = state.hubs[hubName]
        if (!hubConnection?.connection) {
            return
        }

        for (const groupName of getJoinedGroups(hubName).values()) {
            try {
                await hubConnection.connection.invoke('JoinGroup', groupName)
            } catch (err) {
                console.error(`Error rejoining group ${groupName.group} in hub ${hubName}:`, err)
            }
        }
    }

    const connect = async (hubName: string, url: string): Promise<Hub> => {
        if (!state.hubs[hubName]) {
            if (!connectionPromises.has(hubName)) {
                const startPromise = (async () => {
                    const connection = new HubConnectionBuilder()
                        .withUrl(url)
                        .configureLogging(LogLevel.None)
                        .withAutomaticReconnect()
                        .build()

                    state.hubs[hubName] = {
                        connection,
                        isConnected: false,
                        lastError: null
                    }

                    connection.onreconnecting(() => {
                        state.hubs[hubName].isConnected = false
                    })

                    connection.onreconnected(() => {
                        state.hubs[hubName].isConnected = true
                        void rejoinGroups(hubName)
                    })

                    connection.onclose(() => {
                        state.hubs[hubName].isConnected = false
                    })

                    try {
                        await connection.start()
                        state.hubs[hubName].isConnected = true
                    } catch (error) {
                        state.hubs[hubName].lastError = error as Error
                        console.error(`SignalR ${hubName} Connection Error:`, error)
                    } finally {
                        connectionPromises.delete(hubName)
                    }
                })()

                connectionPromises.set(hubName, startPromise)
            }

            await connectionPromises.get(hubName)
        }

        const hubConnection = state.hubs[hubName]

        return {
            joinGroup: async (groupName: { group: string }): Promise<void> => {
                if (hubConnection.connection) {
                    try {
                        await hubConnection.connection.invoke('JoinGroup', groupName)
                        getJoinedGroups(hubName).set(groupName.group, groupName)
                    } catch (err) {
                        console.error(
                            `Error joining group ${groupName.group} in hub ${hubName}:`,
                            err
                        )
                    }
                }
            },
            leaveGroup: async (groupName: { group: string }): Promise<void> => {
                getJoinedGroups(hubName).delete(groupName.group)
                if (hubConnection.connection) {
                    try {
                        await hubConnection.connection.invoke('LeaveGroup', groupName)
                    } catch (err) {
                        console.error(
                            `Error leaving group ${groupName.group} in hub ${hubName}:`,
                            err
                        )
                    }
                }
            },
            send: async (event: string, ...args: unknown[]): Promise<void> => {
                if (hubConnection.connection) {
                    try {
                        await hubConnection.connection.invoke(event, ...args)
                    } catch (err) {
                        console.error(`Error sending ${event} to hub ${hubName}:`, err)
                    }
                }
            },
            on: <K extends keyof EventCallbacks>(event: K, callback: EventCallbacks[K]): void => {
                if (hubConnection.connection) {
                    hubConnection.connection.on(event, callback)
                }
            },
            off: <K extends keyof EventCallbacks>(event: K, callback: EventCallbacks[K]): void => {
                if (hubConnection.connection) {
                    hubConnection.connection.off(event, callback)
                }
            }
        }
    }

    return {
        state,
        connect
    }
}

export const useSignalR = (): SignalRStore => {
    if (!signalRInstance) {
        signalRInstance = createSignalRStore()
    }
    return signalRInstance
}

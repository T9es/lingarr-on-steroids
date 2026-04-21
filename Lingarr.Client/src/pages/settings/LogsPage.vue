<template>
    <div class="bg-secondary w-full p-4">
        <div class="border-secondary bg-primary text-secondary-content mb-4 border-b-2 font-bold">
            <div class="flex items-center justify-between px-4 py-3">
                <h1 class="text-xl">{{ translate('settings.logs.systemLogs') }}</h1>
                <div class="flex items-center space-x-3">
                    <div class="flex items-center space-x-4">
                        <select
                            v-model="filterOptions.logLevel"
                            class="bg-secondary text-accent-content border-secondary rounded border px-2 py-1 text-sm">
                            <option value="all">
                                {{ translate('settings.logs.allLevels') }}
                            </option>
                            <option value="information">
                                {{ translate('settings.logs.information') }}
                            </option>
                            <option value="warning">
                                {{ translate('settings.logs.warning') }}
                            </option>
                            <option value="error">{{ translate('settings.logs.error') }}</option>
                        </select>

                        <input
                            v-model="searchQuery"
                            type="text"
                            :placeholder="translate('settings.logs.searchPlaceholder')"
                            class="bg-secondary text-accent-content border-secondary w-48 rounded border px-2 py-1 text-sm transition-all focus:w-64" />
                    </div>

                    <div class="flex space-x-2">
                        <button
                            class="bg-accent hover:bg-accent/80 text-primary-content cursor-pointer rounded px-3 py-1 text-sm font-medium transition"
                            @click="exportLogs">
                            {{ translate('settings.logs.export') }}
                        </button>
                        <button
                            class="text-primary-content cursor-pointer rounded px-3 py-1 text-sm font-medium transition"
                            :class="
                                isPaused
                                    ? 'bg-success hover:bg-success/80'
                                    : 'bg-warning hover:bg-warning/80'
                            "
                            @click="togglePause">
                            {{
                                isPaused
                                    ? translate('settings.logs.resume')
                                    : translate('settings.logs.pause')
                            }}
                        </button>
                        <button
                            class="bg-error hover:bg-error/80 text-primary-content cursor-pointer rounded px-3 py-1 text-sm font-medium transition"
                            @click="clearLogs">
                            {{ translate('settings.logs.clear') }}
                        </button>
                    </div>
                </div>
            </div>
        </div>

        <div
            class="border-secondary bg-primary text-secondary-content grid grid-cols-12 border-b-2 font-bold">
            <div class="col-span-1 px-4 py-2">
                {{ translate('settings.logs.time') }}
            </div>
            <div class="col-span-1 px-4 py-2">
                {{ translate('settings.logs.level') }}
            </div>
            <div class="col-span-3 px-4 py-2">
                {{ translate('settings.logs.source') }}
            </div>
            <div class="col-span-5 px-4 py-2 md:col-span-7">
                {{ translate('settings.logs.message') }}
            </div>
        </div>

        <div
            ref="logContainer"
            class="bg-primary text-accent-content h-[70vh] overflow-x-hidden overflow-y-auto font-mono text-sm">
            <div v-if="filteredLogs.length === 0" class="flex h-full items-center justify-center">
                <div class="text-secondary-content/60 text-center">
                    <div class="mb-2 text-lg">📋</div>
                    <div>{{ translate('settings.logs.waitingForLogs') }}</div>
                </div>
            </div>

            <div class="log-list">
                <div v-for="log in displayedLogs" :key="log.uniqueId" class="log-entry">
                    <div
                        class="hover:bg-secondary/20 border-secondary/30 grid grid-cols-12 border-b py-2 transition-colors">
                        <div class="text-secondary-content/70 col-span-1 px-4">
                            {{ log.formattedTime }}
                        </div>
                        <div class="col-span-1 px-4">
                            <span
                                :class="getLogLevelBadgeClass(log.logLevel)"
                                class="rounded px-2 py-1 text-xs font-medium">
                                {{ log.logLevel.toUpperCase() }}
                            </span>
                        </div>
                        <div class="col-span-3 px-4 text-blue-300">
                            {{ log.formattedSource }}
                        </div>
                        <div
                            class="col-span-5 px-4 md:col-span-7"
                            v-html="log.formattedMessageHtml"></div>
                    </div>

                    <div
                        v-if="log.stackTrace"
                        class="border-secondary/30 bg-error/5 ml-6 border-b py-2 pr-4 pl-12 text-xs">
                        <pre class="whitespace-pre-wrap">{{ log.stackTrace }}</pre>
                    </div>
                </div>
            </div>
        </div>

        <div
            class="border-secondary bg-primary text-secondary-content mt-4 flex justify-between border-t-2 px-4 py-2 text-sm">
            <div class="flex items-center gap-4">
                <div>{{ translate('settings.logs.totalEntries') }}: {{ filteredLogs.length }}</div>
                <div class="flex items-center gap-2">
                    <label>{{ translate('settings.logs.maxLogs') }}:</label>
                    <select v-model="maxLogs" class="bg-secondary rounded px-1">
                        <option :value="500">500</option>
                        <option :value="1000">1000</option>
                        <option :value="2000">2000</option>
                        <option :value="5000">5000</option>
                    </select>
                </div>
            </div>
            <div>
                {{ translate('settings.logs.autoScroll') }}:
                <span :class="autoScroll ? 'text-success' : 'text-error'">
                    {{
                        autoScroll
                            ? translate('settings.logs.enabled')
                            : translate('settings.logs.disabled')
                    }}
                </span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { IFilterOptions, ILogEntry } from '@/ts'
import services from '@/services'

interface ILogEntryNormalized extends ILogEntry {
    uniqueId: string
    searchText: string
    formattedMessageHtml: string
    logLevelNormalized: string
}

const DISPLAY_WINDOW_SIZE = 300
const FLUSH_TIMEOUT_MS = 33
const RECONNECT_DELAY_MS = 5000

const logs = ref<ILogEntryNormalized[]>([])
const pendingLogs = ref<ILogEntryNormalized[]>([])
const autoScroll = ref(true)
const isPaused = ref(false)
const maxLogs = ref(1000)
const searchQuery = ref('')
const logContainer = ref<HTMLElement | null>(null)
const filterOptions = ref<IFilterOptions>({
    logLevel: 'all'
})

let eventSource: EventSource | null = null
let reconnectTimer: ReturnType<typeof setTimeout> | null = null
let rafFlushId: number | null = null
let fallbackFlushTimer: ReturnType<typeof setTimeout> | null = null
let logIdCounter = 0
let incomingLogs: ILogEntryNormalized[] = []

const normalizedSearchQuery = computed(() => searchQuery.value.trim().toLowerCase())

const filteredLogs = computed(() => {
    return logs.value.filter((log) => {
        if (filterOptions.value.logLevel !== 'all') {
            if (log.logLevelNormalized !== filterOptions.value.logLevel.toLowerCase()) {
                return false
            }
        }

        if (normalizedSearchQuery.value && !log.searchText.includes(normalizedSearchQuery.value)) {
            return false
        }

        return true
    })
})

const displayedLogs = computed(() => {
    const startIndex = Math.max(0, filteredLogs.value.length - DISPLAY_WINDOW_SIZE)
    return filteredLogs.value.slice(startIndex)
})

const escapeHtml = (unsafe: string): string => {
    return unsafe
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;')
}

const formatLogMessage = (message: string): string => {
    let formattedMessage = escapeHtml(message)

    formattedMessage = formattedMessage
        .replace(/\|Green\|([^|]*)\|\/Green\|/g, '<span class="text-green-500">$1</span>')
        .replace(/\|Red\|([^|]*)\|\/Red\|/g, '<span class="text-red-500">$1</span>')
        .replace(/\|Orange\|([^|]*)\|\/Orange\|/g, '<span class="text-orange-500">$1</span>')

    formattedMessage = formattedMessage.replace(
        /&#039;([A-Z_]+)&#039;/g,
        '<span class="text-accent">\'$1\'</span>'
    )

    return formattedMessage
}

const buildNormalizedLogEntry = (log: ILogEntry): ILogEntryNormalized => {
    const message = log.message || ''
    const source = log.formattedSource || ''
    const category = log.category || ''
    const level = log.logLevel || 'Information'

    return {
        ...log,
        logLevel: level,
        uniqueId: `${Date.now()}-${logIdCounter++}`,
        searchText: `${message} ${source} ${category} ${level}`.toLowerCase(),
        formattedMessageHtml: formatLogMessage(message),
        logLevelNormalized: level.toLowerCase()
    }
}

const getLogLevelBadgeClass = (level: string): string => {
    const levelLower = level.toLowerCase()
    if (levelLower.includes('error')) return 'bg-red-500/20 text-red-500'
    if (levelLower.includes('warning')) return 'bg-orange-500/20 text-orange-500'
    if (levelLower.includes('information')) return 'bg-green-500/20 text-green-500'
    return 'bg-info/20 text-info'
}

const scrollToBottom = async () => {
    if (autoScroll.value && logContainer.value && !isPaused.value) {
        await nextTick()
        logContainer.value.scrollTop = logContainer.value.scrollHeight
    }
}

const appendLogs = (currentLogs: ILogEntryNormalized[], newLogs: ILogEntryNormalized[]) => {
    const merged = [...currentLogs, ...newLogs]
    if (merged.length > maxLogs.value) {
        return merged.slice(merged.length - maxLogs.value)
    }

    return merged
}

const flushIncomingLogs = () => {
    if (incomingLogs.length === 0) {
        return
    }

    const batch = incomingLogs
    incomingLogs = []

    if (isPaused.value) {
        pendingLogs.value = appendLogs(pendingLogs.value, batch)
        return
    }

    logs.value = appendLogs(logs.value, batch)
    void scrollToBottom()
}

const clearScheduledFlush = () => {
    if (rafFlushId !== null) {
        window.cancelAnimationFrame(rafFlushId)
        rafFlushId = null
    }

    if (fallbackFlushTimer !== null) {
        clearTimeout(fallbackFlushTimer)
        fallbackFlushTimer = null
    }
}

const scheduleFlush = () => {
    if (rafFlushId !== null || fallbackFlushTimer !== null) {
        return
    }

    rafFlushId = window.requestAnimationFrame(() => {
        clearScheduledFlush()
        flushIncomingLogs()
    })

    fallbackFlushTimer = setTimeout(() => {
        clearScheduledFlush()
        flushIncomingLogs()
    }, FLUSH_TIMEOUT_MS)
}

const enqueueLog = (log: ILogEntryNormalized) => {
    incomingLogs.push(log)
    scheduleFlush()
}

const connectStream = () => {
    eventSource = services.logs.getStream()

    eventSource.onmessage = (event) => {
        try {
            const logData = JSON.parse(event.data) as ILogEntry
            enqueueLog(buildNormalizedLogEntry(logData))
        } catch (error) {
            const fallbackEntry = buildNormalizedLogEntry({
                logLevel: 'Error',
                message: `Failed to process log data: ${typeof event.data === 'string' ? `${event.data.substring(0, 100)}...` : 'Invalid format'}`,
                formattedTime: new Date().toTimeString().split(' ')[0],
                formattedDate: new Date().toDateString(),
                formattedSource: 'System',
                category: 'System',
                stackTrace: error instanceof Error ? error.stack : undefined
            })

            enqueueLog(fallbackEntry)
        }
    }

    eventSource.onerror = () => {
        enqueueLog(
            buildNormalizedLogEntry({
                logLevel: 'Error',
                message: 'Log stream connection error. Attempting to reconnect in 5 seconds...',
                formattedTime: new Date().toTimeString().split(' ')[0],
                formattedDate: new Date().toLocaleDateString(),
                formattedSource: 'System',
                category: 'System'
            })
        )

        if (eventSource) {
            eventSource.close()
            eventSource = null
        }

        if (reconnectTimer) {
            clearTimeout(reconnectTimer)
        }

        reconnectTimer = setTimeout(() => {
            reconnectTimer = null
            connectStream()
        }, RECONNECT_DELAY_MS)
    }
}

const togglePause = () => {
    isPaused.value = !isPaused.value
    if (!isPaused.value && pendingLogs.value.length > 0) {
        logs.value = appendLogs(logs.value, pendingLogs.value)
        pendingLogs.value = []
        void scrollToBottom()
    }
}

const clearLogs = () => {
    logs.value = []
    pendingLogs.value = []
    incomingLogs = []
}

const exportLogs = () => {
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
    const filename = `system-logs-${timestamp}.txt`

    let exportContent = 'System Logs Export\n'
    exportContent += `Generated: ${new Date().toLocaleString()}\n`
    exportContent += `Total Entries: ${filteredLogs.value.length}\n`
    exportContent += `${'='.repeat(80)}\n\n`

    filteredLogs.value.forEach((log) => {
        exportContent += `[${log.formattedDate} ${log.formattedTime}] [${log.logLevel}] [${log.category}] ${log.message}\n`

        if (log.stackTrace) {
            exportContent += `Stack Trace:\n${log.stackTrace}\n`
        }

        exportContent += '\n'
    })

    const blob = new Blob([exportContent], { type: 'text/plain' })
    const url = window.URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = filename
    document.body.appendChild(link)
    link.click()
    document.body.removeChild(link)
    window.URL.revokeObjectURL(url)
}

watch(
    filterOptions,
    () => {
        void scrollToBottom()
    },
    { deep: true }
)

watch(maxLogs, (newValue) => {
    if (logs.value.length > newValue) {
        logs.value = logs.value.slice(logs.value.length - newValue)
    }

    if (pendingLogs.value.length > newValue) {
        pendingLogs.value = pendingLogs.value.slice(pendingLogs.value.length - newValue)
    }
})

onMounted(() => {
    connectStream()
})

onUnmounted(() => {
    clearScheduledFlush()

    if (eventSource) {
        eventSource.close()
        eventSource = null
    }

    if (reconnectTimer) {
        clearTimeout(reconnectTimer)
        reconnectTimer = null
    }
})
</script>

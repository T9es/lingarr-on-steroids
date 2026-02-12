<template>
    <div class="bg-secondary w-full p-4">
        <div class="border-secondary bg-primary text-secondary-content mb-4 border-b-2 font-bold">
            <div class="flex items-center justify-between px-4 py-3">
                <h1 class="text-xl">{{ translate('settings.logs.systemLogs') }}</h1>
                <div class="flex items-center space-x-3">
                    <!-- Filters -->
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

                        <!-- Text Search -->
                        <input 
                            v-model="searchQuery"
                            type="text"
                            :placeholder="translate('settings.logs.searchPlaceholder')"
                            class="bg-secondary text-accent-content border-secondary rounded border px-2 py-1 text-sm w-48 focus:w-64 transition-all"
                        />
                    </div>

                    <div class="flex space-x-2">
                        <button
                            class="bg-accent hover:bg-accent/80 cursor-pointer rounded px-3 py-1 text-sm font-medium text-white transition"
                            @click="exportLogs">
                            {{ translate('settings.logs.export') }}
                        </button>
                        <button
                            class="cursor-pointer rounded px-3 py-1 text-sm font-medium text-white transition"
                            :class="isPaused ? 'bg-success hover:bg-success/80' : 'bg-warning hover:bg-warning/80'"
                            @click="togglePause">
                            {{
                                isPaused
                                    ? translate('settings.logs.resume')
                                    : translate('settings.logs.pause')
                            }}
                        </button>
                        <button
                            class="bg-error hover:bg-error/80 cursor-pointer rounded px-3 py-1 text-sm font-medium text-white transition"
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
            class="bg-primary text-accent-content h-[70vh] overflow-y-auto overflow-x-hidden font-mono text-sm">
            <div v-if="filteredLogs.length === 0" class="flex h-full items-center justify-center">
                <div class="text-center text-gray-500">
                    <div class="mb-2 text-lg">📋</div>
                    <div>{{ translate('settings.logs.waitingForLogs') }}</div>
                </div>
            </div>

            <!-- Log Entries -->
            <div class="log-list">
                <div v-for="(log, index) in filteredLogs" :key="log.uniqueId || index" class="log-entry">
                    <div
                        class="hover:bg-secondary/20 border-secondary/30 grid grid-cols-12 border-b py-2 transition-colors">
                        <div class="col-span-1 px-4 text-gray-400">
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
                            v-html="formatLogMessage(log.message)"></div>
                    </div>

                    <div
                        v-if="log.stackTrace"
                        class="border-secondary/30 bg-error/5 ml-6 border-b py-2 pr-4 pl-12 text-xs">
                        <pre class="whitespace-pre-wrap">{{ log.stackTrace }}</pre>
                    </div>
                </div>
            </div>
        </div>

        <!-- Footer Stats -->
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
import { ref, onMounted, onUnmounted, nextTick, computed, watch } from 'vue'
import { ILogEntry, IFilterOptions } from '@/ts'
import services from '@/services'

// Add unique ID to logs for transition key
interface ILogEntryWithId extends ILogEntry {
    uniqueId?: string
}

const logs = ref<ILogEntryWithId[]>([])
const autoScroll = ref(true)
const isPaused = ref(false)
const maxLogs = ref(1000)
const searchQuery = ref('')
const pendingLogs = ref<ILogEntryWithId[]>([])
const logContainer = ref<HTMLElement | null>(null)
const filterOptions = ref<IFilterOptions>({
    logLevel: 'all'
})
let eventSource: EventSource | null = null

const filteredLogs = computed(() => {
    return logs.value.filter((log) => {
        // Filter by Level
        if (filterOptions.value.logLevel !== 'all') {
            const logLevel = log.logLevel.toLowerCase()
            if (logLevel !== filterOptions.value.logLevel.toLowerCase()) {
                return false
            }
        }
        
        // Filter by Search Query
        if (searchQuery.value) {
            const query = searchQuery.value.toLowerCase()
            const matchesMessage = log.message.toLowerCase().includes(query)
            const matchesSource = log.formattedSource.toLowerCase().includes(query)
            if (!matchesMessage && !matchesSource) {
                return false
            }
        }
        
        return true
    })
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
    // Escape HTML first to prevent XSS
    let formattedMessage = escapeHtml(message)

    // Replace color tags
    formattedMessage = formattedMessage
        .replace(/\|Green\|([^|]*)\|\/Green\|/g, '<span class="text-green-500">$1</span>')
        .replace(/\|Red\|([^|]*)\|\/Red\|/g, '<span class="text-red-500">$1</span>')
        .replace(/\|Orange\|([^|]*)\|\/Orange\|/g, '<span class="text-orange-500">$1</span>')

    // Highlight environment variables
    formattedMessage = formattedMessage.replace(
        /&#039;([A-Z_]+)&#039;/g,
        '<span class="text-accent">\'$1\'</span>'
    )

    return formattedMessage
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

const togglePause = () => {
    isPaused.value = !isPaused.value
    if (!isPaused.value) {
        // Resume: flush pending logs
        if (pendingLogs.value.length > 0) {
            logs.value.push(...pendingLogs.value)
            pendingLogs.value = []
            
            // Trim if needed
            if (logs.value.length > maxLogs.value) {
                logs.value = logs.value.slice(logs.value.length - maxLogs.value)
            }
            scrollToBottom()
        }
    }
}

const clearLogs = () => {
    logs.value = []
    pendingLogs.value = []
}

const exportLogs = () => {
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-')
    const filename = `system-logs-${timestamp}.txt`

    let exportContent = `System Logs Export\n`
    exportContent += `Generated: ${new Date().toLocaleString()}\n`
    exportContent += `Total Entries: ${filteredLogs.value.length}\n`
    exportContent += `${'='.repeat(80)}\n\n`

    filteredLogs.value.forEach((log) => {
        exportContent += `[${log.formattedDate} ${log.formattedTime}] [${log.logLevel}] [${log.category}] ${log.message}\n`

        // Include stack trace
        if (log.stackTrace) {
            exportContent += `Stack Trace:\n${log.stackTrace}\n`
        }

        exportContent += `\n`
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
        scrollToBottom()
    },
    { deep: true }
)

watch(maxLogs, (newValue) => {
    if (logs.value.length > newValue) {
        logs.value = logs.value.slice(logs.value.length - newValue)
    }
})

onMounted(() => {
    eventSource = services.logs.getStream()

    eventSource.onmessage = (event) => {
        try {
            const logData = JSON.parse(event.data) as ILogEntryWithId
            logData.uniqueId = Math.random().toString(36).substring(7)
            
            if (isPaused.value) {
                pendingLogs.value.push(logData)
                // Limit pending logs too to avoid memory issues
                if (pendingLogs.value.length > maxLogs.value) {
                    pendingLogs.value.shift()
                }
            } else {
                logs.value.push(logData)
                if (logs.value.length > maxLogs.value) {
                    logs.value.shift()
                }
                scrollToBottom()
            }
        } catch (error) {
            console.error('Error processing log entry:', error)
            console.error('Problematic data:', event.data)

            const fallbackEntry: ILogEntryWithId = {
                logLevel: 'Error',
                message: `Failed to process log data: ${typeof event.data === 'string' ? event.data.substring(0, 100) + '...' : 'Invalid format'}`,
                formattedTime: new Date().toTimeString().split(' ')[0],
                formattedDate: new Date().toDateString(),
                formattedSource: 'System',
                category: 'System',
                stackTrace: error instanceof Error ? error.stack : undefined,
                uniqueId: Math.random().toString(36).substring(7)
            }
            
            if (!isPaused.value) {
                logs.value.push(fallbackEntry)
                scrollToBottom()
            }
        }
    }

    eventSource.onerror = (error) => {
        console.error('EventSource error:', error)
        const errorLog: ILogEntryWithId = {
            logLevel: 'error',
            message: `Log stream connection error. Attempting to reconnect in 5 seconds...`,
            formattedTime: new Date().toTimeString().split(' ')[0],
            formattedDate: new Date().toLocaleDateString(),
            formattedSource: 'System',
            category: 'System',
            uniqueId: Math.random().toString(36).substring(7)
        }
        
        logs.value.push(errorLog)
        
        // reconnect
        if (eventSource) {
            eventSource.close()
            setTimeout(() => {
                eventSource = services.logs.getStream()
            }, 5000)
        }
    }
})

onUnmounted(() => {
    if (eventSource) {
        eventSource.close()
        eventSource = null
    }
})
</script>

<style scoped>
.log-list-enter-active,
.log-list-leave-active {
    transition: all 0.3s ease;
}
.log-list-enter-from {
    opacity: 0;
    transform: translateY(20px);
}
.log-list-leave-to {
    opacity: 0;
    transform: translateY(-20px);
}
</style>

<template>
    <div class="grid grid-flow-row auto-rows-max grid-cols-1 gap-4 p-4">
        <CardComponent :title="translate('settings.integrity.title')">
            <template #description>
                {{ translate('settings.integrity.description') }}
            </template>
            <template #content>
                <div class="flex flex-col items-center space-y-6">
                    <!-- Action Button -->
                    <div class="flex items-center justify-center">
                        <button
                            :disabled="isRunning"
                            class="bg-accent hover:bg-accent/80 disabled:bg-secondary text-primary-content disabled:text-primary-content/50 rounded px-6 py-3 font-semibold transition-colors disabled:cursor-not-allowed"
                            @click="startBulkCheck">
                            <span v-if="isRunning" class="flex items-center">
                                <svg
                                    class="mr-2 h-5 w-5 animate-spin"
                                    xmlns="http://www.w3.org/2000/svg"
                                    fill="none"
                                    viewBox="0 0 24 24">
                                    <circle
                                        class="opacity-25"
                                        cx="12"
                                        cy="12"
                                        r="10"
                                        stroke="currentColor"
                                        stroke-width="4"></circle>
                                    <path
                                        class="opacity-75"
                                        fill="currentColor"
                                        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                </svg>
                                {{ translate('settings.integrity.running') }}
                            </span>
                            <span v-else>
                                {{ translate('settings.integrity.startButton') }}
                            </span>
                        </button>
                    </div>

                    <!-- Progress Section -->
                    <div v-if="hasStarted" class="w-full max-w-2xl space-y-4">
                        <!-- Progress Bar -->
                        <div class="w-full">
                            <div class="mb-2 flex justify-between text-sm">
                                <span>{{ translate('settings.integrity.progress') }}</span>
                                <span>{{ Math.round(stats.progressPercent) }}%</span>
                            </div>
                            <div
                                class="bg-secondary-content/20 h-4 w-full overflow-hidden rounded-full">
                                <div
                                    class="bg-accent h-full transition-all duration-300"
                                    :style="{ width: `${stats.progressPercent}%` }"></div>
                            </div>
                        </div>

                        <!-- Stats Grid -->
                        <div class="grid grid-cols-2 gap-4 md:grid-cols-4">
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div class="text-2xl font-bold">{{ stats.processedCount }}</div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.processed') }}
                                </div>
                            </div>
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div class="text-2xl font-bold text-green-500">
                                    {{ stats.validCount }}
                                </div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.valid') }}
                                </div>
                            </div>
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div class="text-2xl font-bold text-yellow-500">
                                    {{ stats.corruptCount }}
                                </div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.corrupt') }}
                                </div>
                            </div>
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div class="text-2xl font-bold text-blue-500">
                                    {{ stats.queuedCount }}
                                </div>
                                <div class="text-sm opacity-70">
                                    {{ translate('settings.integrity.stats.queued') }}
                                </div>
                            </div>
                        </div>

                        <!-- Totals -->
                        <div class="text-center text-sm opacity-70">
                            {{ translate('settings.integrity.stats.total') }}: {{ stats.total }} ({{
                                stats.totalMovies
                            }}
                            {{ translate('settings.integrity.stats.movies') }},
                            {{ stats.totalEpisodes }}
                            {{ translate('settings.integrity.stats.episodes') }})
                        </div>

                        <!-- Completion Message -->
                        <div
                            v-if="stats.isComplete"
                            class="rounded border border-green-500/30 bg-green-500/10 p-4 text-center text-green-400">
                            {{ translate('settings.integrity.completed') }}
                        </div>

                        <!-- Error Message -->
                        <div
                            v-if="stats.error"
                            class="rounded border border-red-500/30 bg-red-500/10 p-4 text-center text-red-400">
                            {{ stats.error }}
                        </div>
                    </div>
                </div>
            </template>
        </CardComponent>

        <!-- Subtitle Source Issues Section -->
        <CardComponent title="Subtitle Source Issues">
            <template #description>
                Detects potentially incomplete source subtitles (Forced/Signs-only) based on entry
                count. Subtitles with fewer than 50 entries may need re-translation with a different
                source.
            </template>
            <template #content>
                <div class="flex flex-col space-y-6">
                    <!-- Action Button -->
                    <div class="flex items-center justify-center">
                        <button
                            :disabled="subtitleTypeHasStarted"
                            class="bg-accent hover:bg-accent/80 disabled:bg-secondary text-primary-content disabled:text-primary-content/50 rounded px-6 py-3 font-semibold transition-colors disabled:cursor-not-allowed"
                            @click="startSubtitleTypeValidation">
                            <span v-if="subtitleTypeHasStarted" class="flex items-center">
                                <svg
                                    class="mr-2 h-5 w-5 animate-spin"
                                    xmlns="http://www.w3.org/2000/svg"
                                    fill="none"
                                    viewBox="0 0 24 24">
                                    <circle
                                        class="opacity-25"
                                        cx="12"
                                        cy="12"
                                        r="10"
                                        stroke="currentColor"
                                        stroke-width="4"></circle>
                                    <path
                                        class="opacity-75"
                                        fill="currentColor"
                                        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                </svg>
                                {{ Math.round(subtitleTypeValidationStats.progressPercent) }}% ({{
                                    subtitleTypeValidationStats.processedCount
                                }}/{{ subtitleTypeValidationStats.total }})
                            </span>
                            <span v-else>Check Subtitle Sources</span>
                        </button>
                    </div>

                    <!-- Results -->
                    <div v-if="subtitleTypeResult" class="w-full space-y-4">
                        <!-- Stats Grid -->
                        <div class="grid w-full grid-cols-2 gap-4">
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div class="text-2xl font-bold">
                                    {{ subtitleTypeResult.totalScanned }}
                                </div>
                                <div class="text-sm opacity-70">Translations Scanned</div>
                            </div>
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div
                                    class="text-2xl font-bold"
                                    :class="
                                        subtitleTypeResult.incompleteCount > 0
                                            ? 'text-yellow-500'
                                            : 'text-green-500'
                                    ">
                                    {{ subtitleTypeResult.incompleteCount }}
                                </div>
                                <div class="text-sm opacity-70">Incomplete Subtitles</div>
                            </div>
                        </div>

                        <!-- Flagged Items List -->
                        <div
                            v-if="
                                subtitleTypeResult.flaggedItems &&
                                subtitleTypeResult.flaggedItems.length > 0
                            "
                            class="w-full space-y-3">
                            <div class="flex items-center justify-between">
                                <h4 class="font-semibold">Flagged Incomplete Subtitles</h4>
                                <button
                                    class="bg-accent hover:bg-accent/80 text-primary-content rounded px-4 py-2 text-sm font-semibold"
                                    @click="requeueAllIncomplete">
                                    Auto-fix All
                                </button>
                            </div>
                            <div class="bg-secondary/30 max-h-96 w-full overflow-y-auto rounded">
                                <div
                                    v-for="item in subtitleTypeResult.flaggedItems"
                                    :key="item.translationId"
                                    class="border-secondary/50 border-b last:border-0">
                                    <div
                                        class="hover:bg-secondary/50 flex cursor-pointer items-center justify-between p-3"
                                        @click="toggleSubtitleTypeExpand(item.translationId)">
                                        <div class="flex-1 overflow-hidden">
                                            <div class="truncate font-medium">
                                                {{ item.mediaTitle }}
                                            </div>
                                            <div class="truncate text-xs opacity-50">
                                                {{ item.subtitlePath }}
                                            </div>
                                            <div class="mt-1 flex items-center gap-2">
                                                <span class="text-xs text-yellow-500">
                                                    {{ item.entryCount }} entries (threshold: 50)
                                                </span>
                                                <span
                                                    v-if="item.isQueued"
                                                    class="rounded bg-blue-500/20 px-2 py-0.5 text-xs text-blue-400">
                                                    Already queued
                                                </span>
                                                <span
                                                    v-if="item.dismissed"
                                                    class="bg-secondary-content/20 text-secondary-content/70 rounded px-2 py-0.5 text-xs">
                                                    Dismissed
                                                </span>
                                            </div>
                                        </div>
                                        <div class="ml-2 flex items-center gap-1">
                                            <button
                                                v-if="!item.isQueued && !item.dismissed"
                                                class="rounded bg-green-500/20 px-2 py-1 text-xs text-green-400 hover:bg-green-500/30"
                                                @click.stop="acceptSubtitleType(item)">
                                                Accept
                                            </button>
                                            <button
                                                v-if="!item.isQueued && !item.dismissed"
                                                class="bg-accent/20 text-accent hover:bg-accent/30 rounded px-2 py-1 text-xs"
                                                @click.stop="requeueSubtitleType(item)">
                                                Auto-fix
                                            </button>
                                            <button
                                                v-if="!item.isQueued && !item.dismissed"
                                                class="rounded bg-blue-500/20 px-2 py-1 text-xs text-blue-400 hover:bg-blue-500/30"
                                                @click.stop="openManualSelect(item)">
                                                Manual Select
                                            </button>
                                            <button
                                                v-if="!item.isQueued && !item.dismissed"
                                                class="bg-secondary-content/20 text-secondary-content/70 hover:bg-secondary-content/30 rounded px-2 py-1 text-xs"
                                                @click.stop="dismissSubtitleType(item)">
                                                Dismiss
                                            </button>
                                        </div>
                                    </div>
                                    <!-- Expandable details -->
                                    <div
                                        v-if="
                                            expandedSubtitleTypeItems.includes(
                                                item.translationId
                                            ) && item.warning
                                        "
                                        class="bg-tertiary/50 border-secondary/50 border-t p-3 text-xs">
                                        <div class="mb-1 font-semibold opacity-70">Warning:</div>
                                        <div class="text-yellow-400">{{ item.warning }}</div>
                                        <div class="mt-2 mb-1 font-semibold opacity-70">
                                            Recommended Action:
                                        </div>
                                        <div class="text-accent">{{ item.recommendedAction }}</div>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Success Message -->
                        <div
                            v-if="subtitleTypeResult.incompleteCount === 0"
                            class="w-full rounded border border-green-500/30 bg-green-500/10 p-4 text-center text-green-400">
                            All source subtitles have sufficient entries!
                        </div>
                    </div>
                </div>
            </template>
        </CardComponent>

        <!-- Verify ASS Integrity Section -->
        <CardComponent title="Verify ASS Integrity">
            <template #description>
                Scans translated subtitles for ASS/SSA artifacts, including drawing residue and
                damaged leaked tags. Run after major updates.
            </template>
            <template #content>
                <div class="flex flex-col space-y-6">
                    <!-- Action Button -->
                    <div class="flex items-center justify-center">
                        <button
                            :disabled="assHasStarted"
                            class="bg-accent hover:bg-accent/80 disabled:bg-secondary text-primary-content disabled:text-primary-content/50 rounded px-6 py-3 font-semibold transition-colors disabled:cursor-not-allowed"
                            @click="startAssVerification">
                            <span v-if="assHasStarted" class="flex items-center">
                                <svg
                                    class="mr-2 h-5 w-5 animate-spin"
                                    xmlns="http://www.w3.org/2000/svg"
                                    fill="none"
                                    viewBox="0 0 24 24">
                                    <circle
                                        class="opacity-25"
                                        cx="12"
                                        cy="12"
                                        r="10"
                                        stroke="currentColor"
                                        stroke-width="4"></circle>
                                    <path
                                        class="opacity-75"
                                        fill="currentColor"
                                        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                </svg>
                                {{ Math.round(assValidationStats.progressPercent) }}% ({{
                                    assValidationStats.processedCount
                                }}/{{ assValidationStats.total }})
                            </span>
                            <span v-else>Verify ASS Integrity</span>
                        </button>
                    </div>

                    <!-- Persistent Results -->
                    <div v-if="assResult" class="w-full space-y-4">
                        <!-- Stats Grid -->
                        <div class="grid w-full grid-cols-2 gap-4">
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div class="text-2xl font-bold">
                                    {{ assResult.totalFilesScanned }}
                                </div>
                                <div class="text-sm opacity-70">Files Scanned</div>
                            </div>
                            <div class="bg-secondary/30 rounded p-4 text-center">
                                <div
                                    class="text-2xl font-bold"
                                    :class="
                                        assResult.filesWithDrawings > 0
                                            ? 'text-yellow-500'
                                            : 'text-green-500'
                                    ">
                                    {{ assResult.filesWithDrawings }}
                                </div>
                                <div class="text-sm opacity-70">Files with Issues</div>
                            </div>
                        </div>

                        <!-- Flagged Items List -->
                        <div
                            v-if="assResult.flaggedItems && assResult.flaggedItems.length > 0"
                            class="w-full space-y-3">
                            <div class="flex items-center justify-between">
                                <h4 class="font-semibold">Flagged Files</h4>
                                <button
                                    class="bg-accent hover:bg-accent/80 text-primary-content rounded px-4 py-2 text-sm font-semibold"
                                    @click="requeueAll">
                                    Requeue All for Translation
                                </button>
                            </div>
                            <div class="bg-secondary/30 max-h-96 w-full overflow-y-auto rounded">
                                <div
                                    v-for="item in assResult.flaggedItems"
                                    :key="item.subtitlePath"
                                    class="border-secondary/50 border-b last:border-0">
                                    <div
                                        class="hover:bg-secondary/50 flex cursor-pointer items-center justify-between p-3"
                                        @click="toggleExpand(item.subtitlePath)">
                                        <div class="flex-1 overflow-hidden">
                                            <div class="truncate font-medium">
                                                {{ item.mediaTitle }}
                                            </div>
                                            <div class="truncate text-xs opacity-50">
                                                {{ item.subtitlePath }}
                                            </div>
                                            <div class="mt-1 flex flex-wrap gap-1">
                                                <span
                                                    v-for="issueLabel in getAssIssueLabels(
                                                        item.issueTypes
                                                    )"
                                                    :key="issueLabel"
                                                    class="rounded bg-yellow-500/15 px-2 py-0.5 text-xs text-yellow-400">
                                                    {{ issueLabel }}
                                                </span>
                                            </div>
                                            <div class="mt-1 truncate text-xs opacity-70">
                                                {{ getAssIssueSummary(item) }}
                                            </div>
                                            <div class="flex items-center gap-2">
                                                <span class="text-xs text-yellow-500">
                                                    {{ item.suspiciousLineCount }} suspicious entries
                                                    (click to view)
                                                </span>
                                                <span
                                                    v-if="item.isQueued"
                                                    class="rounded bg-blue-500/20 px-2 py-0.5 text-xs text-blue-400">
                                                    Already queued
                                                </span>
                                            </div>
                                        </div>
                                        <button
                                            v-if="!item.isQueued"
                                            class="ml-2 text-sm opacity-50 hover:opacity-100"
                                            @click.stop="dismissItem(item)">
                                            Dismiss
                                        </button>
                                    </div>
                                    <!-- Expandable suspicious lines -->
                                    <div
                                        v-if="
                                            expandedItems.includes(item.subtitlePath) &&
                                            item.suspiciousLines
                                        "
                                        class="bg-tertiary/50 border-secondary/50 border-t p-3 text-xs">
                                        <div class="mb-2 font-semibold opacity-70">
                                            Suspicious entries:
                                        </div>
                                        <div
                                            v-for="(line, idx) in item.suspiciousLines"
                                            :key="idx"
                                            class="truncate py-1 font-mono text-yellow-400">
                                            {{ line }}
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Success Message -->
                        <div
                            v-if="assResult.filesWithDrawings === 0"
                            class="w-full rounded border border-green-500/30 bg-green-500/10 p-4 text-center text-green-400">
                            All files passed verification!
                        </div>
                    </div>
                </div>
            </template>
        </CardComponent>
    </div>

    <!-- Subtitle Selector Modal -->
    <SubtitleSelectorModal
        :is-open="isSubtitleModalOpen"
        :media-id="selectedMedia?.mediaId ?? 0"
        :media-type="selectedMedia?.mediaType ?? ''"
        :media-title="selectedMedia?.mediaTitle ?? ''"
        source-language="eng"
        @close="closeSubtitleModal"
        @success="handleSubtitleModalSuccess"
        @error="handleSubtitleModalError" />
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CardComponent from '@/components/common/CardComponent.vue'
import SubtitleSelectorModal from '@/components/features/settings/SubtitleSelectorModal.vue'
import { useSignalR } from '@/composables/useSignalR'
import { Hub } from '@/ts'
import axios from 'axios'

const { translate } = useI18n()
const signalR = useSignalR()
const hubConnection = ref<Hub>()

interface BulkIntegrityStats {
    total: number
    totalMovies: number
    totalEpisodes: number
    processedCount: number
    validCount: number
    corruptCount: number
    queuedCount: number
    errorCount: number
    isComplete: boolean
    error: string | null
    progressPercent: number
}

const isRunning = ref(false)
const hasStarted = ref(false)
const stats = reactive<BulkIntegrityStats>({
    total: 0,
    totalMovies: 0,
    totalEpisodes: 0,
    processedCount: 0,
    validCount: 0,
    corruptCount: 0,
    queuedCount: 0,
    errorCount: 0,
    isComplete: false,
    error: null,
    progressPercent: 0
})

// Subtitle Type Validation
interface SubtitleTypeCheckResult {
    translationId: number
    mediaTitle: string
    subtitlePath: string
    entryCount: number
    isComplete: boolean
    warning: string
    recommendedAction: string
    mediaType: string
    mediaId: number
    isQueued: boolean
    dismissed: boolean
}

interface SubtitleTypeCheckSummary {
    totalScanned: number
    incompleteCount: number
    flaggedItems: SubtitleTypeCheckResult[]
}

// Subtitle Type Validation Stats
interface SubtitleTypeValidationStats {
    total: number
    processedCount: number
    incompleteCount: number
    isComplete: boolean
    isRunning: boolean
    error: string | null
    progressPercent: number
}

const subtitleTypeValidationStats = reactive<SubtitleTypeValidationStats>({
    total: 0,
    processedCount: 0,
    incompleteCount: 0,
    isComplete: false,
    isRunning: false,
    error: null,
    progressPercent: 0
})

const subtitleTypeHasStarted = ref(false)
const subtitleTypeResult = ref<SubtitleTypeCheckSummary | null>(null)
const expandedSubtitleTypeItems = ref<number[]>([])

// Subtitle Selector Modal
const isSubtitleModalOpen = ref(false)
const selectedMedia = ref<SubtitleTypeCheckResult | null>(null)

const openManualSelect = (item: SubtitleTypeCheckResult) => {
    selectedMedia.value = item
    isSubtitleModalOpen.value = true
}

const closeSubtitleModal = () => {
    isSubtitleModalOpen.value = false
    selectedMedia.value = null
}

const handleSubtitleModalSuccess = async (message: string) => {
    // Mark the item as queued
    if (selectedMedia.value && subtitleTypeResult.value?.flaggedItems) {
        const flaggedItem = subtitleTypeResult.value.flaggedItems.find(
            (i) => i.translationId === selectedMedia.value?.translationId
        )
        if (flaggedItem) {
            flaggedItem.isQueued = true
        }

        // Update persisted result
        await axios.post('/api/setting', {
            key: 'subtitle_type_validation_last_result',
            value: JSON.stringify(subtitleTypeResult.value)
        })
    }

    // Show success notification (could be implemented with a toast)
    console.log('Translation queued:', message)
    alert(message)
}

const handleSubtitleModalError = (message: string) => {
    console.error('Failed to queue translation:', message)
    alert('Error: ' + message)
}

const toggleSubtitleTypeExpand = (translationId: number) => {
    if (expandedSubtitleTypeItems.value.includes(translationId)) {
        expandedSubtitleTypeItems.value = expandedSubtitleTypeItems.value.filter(
            (id) => id !== translationId
        )
    } else {
        expandedSubtitleTypeItems.value.push(translationId)
    }
}

const handleSubtitleTypeValidationProgress = (newStats: SubtitleTypeValidationStats) => {
    Object.assign(subtitleTypeValidationStats, newStats)
    if (newStats.isComplete) {
        subtitleTypeHasStarted.value = false
    }
}

const startSubtitleTypeValidation = async () => {
    try {
        subtitleTypeHasStarted.value = true
        Object.assign(subtitleTypeValidationStats, {
            total: 0,
            processedCount: 0,
            incompleteCount: 0,
            isComplete: false,
            isRunning: true,
            error: null,
            progressPercent: 0
        })

        await axios.post('/api/subtitle/validate-subtitle-types')
    } catch (error) {
        console.error('Failed to start subtitle type validation:', error)
        subtitleTypeHasStarted.value = false
        subtitleTypeValidationStats.isRunning = false
    }
}

const dismissSubtitleType = async (item: SubtitleTypeCheckResult) => {
    if (!subtitleTypeResult.value?.flaggedItems) return

    // Mark as dismissed in the list
    const flaggedItem = subtitleTypeResult.value.flaggedItems.find(
        (i) => i.translationId === item.translationId
    )
    if (flaggedItem) {
        flaggedItem.dismissed = true
    }

    // Update persisted result
    await axios.post('/api/setting', {
        key: 'subtitle_type_validation_last_result',
        value: JSON.stringify(subtitleTypeResult.value)
    })
}

const acceptSubtitleType = async (item: SubtitleTypeCheckResult) => {
    // Same as dismiss - user accepts this as-is
    await dismissSubtitleType(item)
}

const requeueSubtitleType = async (item: SubtitleTypeCheckResult) => {
    if (!subtitleTypeResult.value?.flaggedItems) return

    try {
        // Requeue the media for translation
        await axios.post('/api/translate/media', {
            mediaId: item.mediaId,
            mediaType: item.mediaType
        })

        // Mark as queued
        const flaggedItem = subtitleTypeResult.value.flaggedItems.find(
            (i) => i.translationId === item.translationId
        )
        if (flaggedItem) {
            flaggedItem.isQueued = true
        }

        // Update persisted result
        await axios.post('/api/setting', {
            key: 'subtitle_type_validation_last_result',
            value: JSON.stringify(subtitleTypeResult.value)
        })
    } catch (error) {
        console.error('Failed to requeue item:', error)
    }
}

const requeueAllIncomplete = async () => {
    if (!subtitleTypeResult.value?.flaggedItems) return

    try {
        // Only requeue items that are not already in queue or dismissed
        const itemsToRequeue = subtitleTypeResult.value.flaggedItems.filter(
            (item) => !item.isQueued && !item.dismissed
        )

        for (const item of itemsToRequeue) {
            await axios.post('/api/translate/media', {
                mediaId: item.mediaId,
                mediaType: item.mediaType
            })
        }

        // Mark all as queued
        subtitleTypeResult.value.flaggedItems = subtitleTypeResult.value.flaggedItems.map(
            (item) => ({
                ...item,
                isQueued: true
            })
        )

        // Update persisted result
        await axios.post('/api/setting', {
            key: 'subtitle_type_validation_last_result',
            value: JSON.stringify(subtitleTypeResult.value)
        })
    } catch (error) {
        console.error('Failed to requeue items:', error)
    }
}

const startBulkCheck = async () => {
    try {
        isRunning.value = true
        hasStarted.value = true

        // Reset stats
        Object.assign(stats, {
            total: 0,
            totalMovies: 0,
            totalEpisodes: 0,
            processedCount: 0,
            validCount: 0,
            corruptCount: 0,
            queuedCount: 0,
            errorCount: 0,
            isComplete: false,
            error: null,
            progressPercent: 0
        })

        await axios.post('/api/media/bulk-integrity-check')
    } catch (error) {
        console.error('Failed to start bulk integrity check:', error)
        stats.error = 'Failed to start integrity check'
        isRunning.value = false
    }
}

const handleProgress = (newStats: BulkIntegrityStats) => {
    Object.assign(stats, newStats)
    if (newStats.isComplete) {
        isRunning.value = false
    }
}

onMounted(async () => {
    // Check if a job is already running and restore state
    try {
        const response = await axios.get('/api/media/bulk-integrity-status')
        if (response.data && response.data.isRunning) {
            hasStarted.value = true
            isRunning.value = true
            Object.assign(stats, response.data)
        } else if (response.data && response.data.isComplete) {
            // Show completed state if job finished while page was closed
            hasStarted.value = true
            isRunning.value = false
            Object.assign(stats, response.data)
        }
    } catch (error) {
        console.debug('No existing integrity check status')
    }

    // Load persisted ASS verification result
    try {
        const assResponse = await axios.get('/api/setting/subtitle_ass_verification_last_result')
        // API returns the value directly as a string, not as {value: ...}
        if (assResponse.data) {
            assResult.value = JSON.parse(assResponse.data)
        }
    } catch (error) {
        // 400 is expected when no scan has been run yet
        console.debug('No existing ASS verification result')
    }

    // Load persisted subtitle type validation result
    try {
        const subtitleTypeResponse = await axios.get(
            '/api/setting/subtitle_type_validation_last_result'
        )
        if (subtitleTypeResponse.data) {
            subtitleTypeResult.value = JSON.parse(subtitleTypeResponse.data)
        }
    } catch (error) {
        console.debug('No existing subtitle type validation result')
    }

    try {
        const assStatusResponse = await axios.get('/api/subtitle/verify-ass/status')
        if (assStatusResponse.data.isRunning) {
            Object.assign(assValidationStats, assStatusResponse.data)
            assHasStarted.value = true
        } else {
            const assResultResponse = await axios.get(
                '/api/setting/subtitle_ass_verification_last_result'
            )
            if (assResultResponse.data) {
                assResult.value = JSON.parse(assResultResponse.data)
            }
        }
    } catch (error) {
        console.debug('No existing ASS verification result')
    }

    try {
        const subtitleTypeStatusResponse = await axios.get(
            '/api/subtitle/validate-subtitle-types/status'
        )
        if (subtitleTypeStatusResponse.data.isRunning) {
            Object.assign(subtitleTypeValidationStats, subtitleTypeStatusResponse.data)
            subtitleTypeHasStarted.value = true
        }
    } catch (error) {
        console.debug('No existing subtitle type validation status')
    }

    hubConnection.value = await signalR.connect('JobProgress', '/signalr/JobProgress')
    await hubConnection.value.joinGroup({ group: 'JobProgress' })
    hubConnection.value.on('BulkIntegrityProgress', handleProgress)
    hubConnection.value.on('AssVerificationProgress', handleAssVerificationProgress)
    hubConnection.value.on('SubtitleTypeValidationProgress', handleSubtitleTypeValidationProgress)
})

onUnmounted(() => {
    hubConnection.value?.off('BulkIntegrityProgress', handleProgress)
    hubConnection.value?.off('AssVerificationProgress', handleAssVerificationProgress)
    hubConnection.value?.off('SubtitleTypeValidationProgress', handleSubtitleTypeValidationProgress)
})

// ASS Verification
interface AssVerificationItem {
    mediaId: number
    mediaType: string
    mediaTitle: string
    subtitlePath: string
    suspiciousLineCount: number
    suspiciousLines: string[]
    issueTypes?: string[]
    issueSummary?: string
    dismissed: boolean
    isQueued: boolean
}

interface AssVerificationResult {
    totalFilesScanned: number
    filesWithDrawings: number
    flaggedItems: AssVerificationItem[]
}

interface AssVerificationStats {
    total: number
    processedCount: number
    isComplete: boolean
    isRunning: boolean
    error: string | null
    progressPercent: number
}

const assValidationStats = reactive<AssVerificationStats>({
    total: 0,
    processedCount: 0,
    isComplete: false,
    isRunning: false,
    error: null,
    progressPercent: 0
})

const assHasStarted = ref(false)
const assResult = ref<AssVerificationResult | null>(null)
const expandedItems = ref<string[]>([])

const assIssueLabels: Record<string, string> = {
    drawing_artifact: 'Drawing residue',
    unexpected_ass_tags: 'Unexpected ASS/SSA tags',
    ass_tag_mismatch: 'ASS/SSA tag mismatch'
}

const getAssIssueLabels = (issueTypes?: string[]) => {
    if (!issueTypes || issueTypes.length === 0) {
        return ['ASS/SSA artifact']
    }

    return issueTypes.map((issueType) => assIssueLabels[issueType] ?? issueType)
}

const getAssIssueSummary = (item: AssVerificationItem) => {
    return item.issueSummary || getAssIssueLabels(item.issueTypes).join(', ')
}

const toggleExpand = (path: string) => {
    if (expandedItems.value.includes(path)) {
        expandedItems.value = expandedItems.value.filter((p) => p !== path)
    } else {
        expandedItems.value.push(path)
    }
}

const handleAssVerificationProgress = (newStats: AssVerificationStats) => {
    Object.assign(assValidationStats, newStats)
    if (newStats.isComplete) {
        assHasStarted.value = false
    }
}

const startAssVerification = async () => {
    try {
        assHasStarted.value = true
        Object.assign(assValidationStats, {
            total: 0,
            processedCount: 0,
            isComplete: false,
            isRunning: true,
            error: null,
            progressPercent: 0
        })

        await axios.post('/api/subtitle/verify-ass')
    } catch (error) {
        console.error('Failed to start ASS verification:', error)
        assHasStarted.value = false
        assValidationStats.isRunning = false
    }
}

const requeueAll = async () => {
    if (!assResult.value?.flaggedItems) return

    try {
        // Only requeue items that are not already in queue
        const itemsToRequeue = assResult.value.flaggedItems.filter((item) => !item.isQueued)

        for (const item of itemsToRequeue) {
            // MediaType should be string like 'Movie' or 'Episode'
            await axios.post('/api/translate/media', {
                mediaId: item.mediaId,
                mediaType: item.mediaType
            })
        }

        // Mark requeued items as isQueued instead of removing them
        assResult.value.flaggedItems = assResult.value.flaggedItems.map((item) => ({
            ...item,
            isQueued: true
        }))

        // Update persisted result
        await axios.post('/api/setting', {
            key: 'subtitle_ass_verification_last_result',
            value: JSON.stringify(assResult.value)
        })
    } catch (error) {
        console.error('Failed to requeue items:', error)
    }
}

const dismissItem = async (item: AssVerificationItem) => {
    if (!assResult.value?.flaggedItems) return

    // Remove from list
    assResult.value.flaggedItems = assResult.value.flaggedItems.filter(
        (i) => i.subtitlePath !== item.subtitlePath
    )
    assResult.value.filesWithDrawings = assResult.value.flaggedItems.length

    // Update persisted result
    await axios.post('/api/setting', {
        key: 'subtitle_ass_verification_last_result',
        value: JSON.stringify(assResult.value)
    })
}
</script>

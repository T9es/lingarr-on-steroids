<template>
    <div class="space-y-4 p-4">
        <p v-if="errorMessage" class="text-error text-sm">
            {{ errorMessage }}
        </p>
        <p v-if="successMessage" class="text-success text-sm">
            {{ successMessage }}
        </p>

        <div class="grid gap-4 xl:grid-cols-[380px_minmax(0,1fr)]">
            <div class="space-y-4">
                <CardComponent title="Create or Select Batch">
                    <template #description>
                        Choose a target language and default remux behavior, then create or update
                        the active upload batch.
                    </template>

                    <div class="space-y-3">
                        <div>
                            <label class="text-primary-content/80 mb-1 block text-sm">
                                Batch Name
                            </label>
                            <input
                                v-model="batchForm.name"
                                class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                                type="text"
                                placeholder="Weekend subtitles" />
                        </div>

                        <div>
                            <label class="text-primary-content/80 mb-1 block text-sm">
                                Target Language
                            </label>
                            <select
                                v-if="languages.length > 0"
                                v-model="batchForm.targetLanguage"
                                @change="markTargetLanguageTouched"
                                class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2">
                                <option value="">Select target language</option>
                                <option
                                    v-for="language in languages"
                                    :key="language.code"
                                    :value="language.code">
                                    {{ language.name }} ({{ language.code.toUpperCase() }})
                                </option>
                            </select>
                            <input
                                v-else
                                v-model="batchForm.targetLanguage"
                                @input="markTargetLanguageTouched"
                                class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                                type="text"
                                placeholder="en" />
                            <p class="text-primary-content/60 mt-1 text-xs">
                                Defaulted from global target language. Changing this batch does not
                                change global settings.
                            </p>
                        </div>

                        <label class="flex items-center gap-2 text-sm">
                            <input v-model="batchForm.defaultRemuxEnabled" type="checkbox" />
                            <span>Enable remux by default for media files</span>
                        </label>

                        <div class="flex flex-wrap gap-2">
                            <button
                                class="bg-accent text-secondary-content rounded-md px-3 py-2 text-sm font-medium"
                                :disabled="batchActionInProgress"
                                @click="createBatch">
                                {{ batchActionInProgress ? 'Working...' : 'Create Batch' }}
                            </button>
                            <button
                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                                :disabled="batchActionInProgress || !selectedBatchId"
                                @click="updateSelectedBatch">
                                {{ batchActionInProgress ? 'Saving...' : 'Save Batch' }}
                            </button>
                            <button
                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                                :disabled="batchActionInProgress"
                                @click="resetBatchForm">
                                Clear
                            </button>
                        </div>
                    </div>

                    <div v-if="batches.length === 0" class="text-primary-content/60 mt-4 text-sm">
                        No upload batches yet.
                    </div>

                    <div v-else class="mt-4 max-h-72 space-y-2 overflow-y-auto pr-1">
                        <button
                            v-for="batch in batches"
                            :key="batch.id"
                            class="bg-secondary border-accent/20 hover:border-accent/60 w-full rounded-md border p-2.5 text-left transition-colors"
                            :class="{ 'border-accent': batch.id === selectedBatchId }"
                            @click="selectBatch(batch.id)">
                            <div class="flex items-center justify-between gap-2">
                                <div class="min-w-0">
                                    <p class="text-primary-content truncate text-sm font-medium">
                                        {{ batch.name }}
                                    </p>
                                    <p class="text-primary-content/60 mt-1 text-xs">
                                        {{ batch.targetLanguage.toUpperCase() }} -
                                        {{ batch.fileCount }} file{{
                                            batch.fileCount === 1 ? '' : 's'
                                        }}
                                    </p>
                                </div>
                                <BadgeComponent :variant="getBatchBadgeVariant(batch.status)">
                                    {{ formatBatchStatus(batch.status) }}
                                </BadgeComponent>
                            </div>
                        </button>
                    </div>
                </CardComponent>

                <CardComponent title="Upload Files">
                    <template #description>
                        Drop subtitle or media files here. Upload progress and queue status update
                        while files are transferred.
                    </template>

                    <input
                        ref="fileInputRef"
                        class="hidden"
                        type="file"
                        multiple
                        @change="handleFilePicker" />

                    <div
                        class="border-accent/30 bg-secondary/40 rounded-md border border-dashed px-4 py-8"
                        :class="{ 'border-accent bg-secondary/70': isDropActive }"
                        @dragenter.prevent="isDropActive = true"
                        @dragover.prevent="isDropActive = true"
                        @dragleave.prevent="isDropActive = false"
                        @drop.prevent="handleDrop">
                        <p class="text-primary-content text-sm font-medium">
                            Drag and drop subtitles or media files
                        </p>
                        <p class="text-primary-content/60 mt-1 text-xs">
                            Supported: SRT, ASS, SSA, VTT, MKV, MP4, AVI, M4V, WEBM, MOV, WMV
                        </p>
                        <div class="mt-3 flex flex-wrap items-center gap-2">
                            <button
                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                                :disabled="uploading"
                                @click="openFilePicker">
                                {{ uploading ? 'Uploading...' : 'Choose Files' }}
                            </button>
                            <span class="text-primary-content/60 text-xs">
                                {{
                                    selectedBatchId
                                        ? 'Uploads go to the selected batch.'
                                        : uploadTargetLanguageBlocker
                                          ? 'Choose files to queue them. Upload starts after selecting a target language.'
                                          : 'A batch is created if needed.'
                                }}
                            </span>
                        </div>
                        <p v-if="uploadTargetLanguageBlocker" class="text-warning mt-2 text-xs">
                            {{ uploadTargetLanguageBlocker }}
                        </p>
                    </div>

                    <div v-if="uploadQueue.length" class="mt-4 space-y-2">
                        <div class="flex items-center justify-between text-xs">
                            <span class="text-primary-content/70">
                                {{ uploadProgressPercent }}% uploaded
                            </span>
                            <span class="text-primary-content/70">
                                {{ formatSpeed(uploadSpeedBytesPerSecond) }}
                            </span>
                        </div>
                        <div class="bg-secondary h-2 rounded">
                            <div
                                class="bg-accent h-2 rounded transition-all"
                                :style="{ width: `${uploadProgressPercent}%` }"></div>
                        </div>

                        <div class="max-h-56 space-y-2 overflow-y-auto">
                            <div
                                v-for="item in uploadQueue"
                                :key="item.id"
                                class="bg-secondary border-accent/15 rounded-md border px-3 py-2">
                                <div class="flex items-center justify-between gap-2">
                                    <p class="text-primary-content truncate text-sm">
                                        {{ item.name }}
                                    </p>
                                    <BadgeComponent :variant="getQueueBadgeVariant(item.status)">
                                        {{ item.status }}
                                    </BadgeComponent>
                                </div>
                                <div class="text-primary-content/60 mt-1 text-xs">
                                    {{ formatBytes(item.loadedBytes) }} /
                                    {{ formatBytes(item.totalBytes) }} ({{ item.percent }}%)
                                </div>
                                <div class="bg-primary/70 mt-2 h-1.5 rounded">
                                    <div
                                        class="bg-accent h-1.5 rounded transition-all"
                                        :style="{ width: `${item.percent}%` }"></div>
                                </div>
                                <p v-if="item.error" class="text-error mt-2 text-xs">
                                    {{ item.error }}
                                </p>
                            </div>
                        </div>
                    </div>
                </CardComponent>
            </div>

            <div class="space-y-4">
                <CardComponent :title="selectedBatch ? selectedBatch.name : 'Review Files'">
                    <template #description>
                        Configure source language/stream options per file, then save or reprobe
                        before processing.
                    </template>

                    <div v-if="!selectedBatch" class="text-primary-content/60 text-sm">
                        Select a batch to configure files and start processing.
                    </div>

                    <div v-else class="space-y-4">
                        <div class="bg-secondary border-accent/15 rounded-md border p-3">
                            <div class="flex flex-wrap items-center gap-2">
                                <BadgeComponent
                                    :variant="getBatchBadgeVariant(selectedBatch.status)">
                                    {{ formatBatchStatus(selectedBatch.status) }}
                                </BadgeComponent>
                                <span class="text-primary-content/70 text-xs">
                                    {{ selectedBatch.fileCount }} files
                                </span>
                                <span class="text-primary-content/70 text-xs">
                                    Completed: {{ selectedBatch.completedFileCount }}
                                </span>
                                <span class="text-primary-content/70 text-xs">
                                    Failed: {{ selectedBatch.failedFileCount }}
                                </span>
                                <span class="text-primary-content/70 text-xs">
                                    Active: {{ selectedBatch.activeFileCount }}
                                </span>
                            </div>

                            <div class="mt-3 flex flex-wrap gap-2">
                                <button
                                    class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                                    :disabled="batchActionInProgress"
                                    @click="refreshSelectedBatch">
                                    Refresh
                                </button>
                                <button
                                    class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                                    :disabled="
                                        batchActionInProgress ||
                                        selectedBatch.status === UPLOAD_BATCH_STATUS.PROCESSING
                                    "
                                    @click="startSelectedBatch">
                                    Start Batch
                                </button>
                                <button
                                    class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                                    :disabled="
                                        batchActionInProgress ||
                                        selectedBatch.status !== UPLOAD_BATCH_STATUS.PROCESSING
                                    "
                                    @click="cancelSelectedBatch">
                                    Cancel Batch
                                </button>
                                <button
                                    class="border-accent/30 text-primary-content rounded-md border px-3 py-2 text-sm"
                                    :disabled="batchActionInProgress"
                                    @click="deleteSelectedBatch">
                                    Delete Batch
                                </button>
                            </div>
                        </div>

                        <div
                            v-if="selectedBatch.files.length === 0"
                            class="text-primary-content/60 text-sm">
                            No files uploaded into this batch yet.
                        </div>

                        <div v-else class="max-h-[640px] space-y-3 overflow-y-auto pr-1">
                            <div
                                v-for="file in selectedBatch.files"
                                :key="file.id"
                                class="bg-secondary border-accent/15 rounded-md border p-3">
                                <div class="flex flex-wrap items-center justify-between gap-2">
                                    <div class="min-w-0">
                                        <p
                                            class="text-primary-content truncate text-sm font-medium">
                                            {{ file.originalFileName }}
                                        </p>
                                        <p class="text-primary-content/60 mt-1 text-xs">
                                            {{ formatBytes(file.fileSizeBytes) }}
                                        </p>
                                    </div>
                                    <div class="flex flex-wrap gap-2">
                                        <BadgeComponent
                                            :variant="
                                                file.fileKind === UPLOAD_BATCH_FILE_KIND.MEDIA
                                                    ? 'info'
                                                    : 'default'
                                            ">
                                            {{ formatFileKind(file.fileKind) }}
                                        </BadgeComponent>
                                        <BadgeComponent :variant="getFileBadgeVariant(file.status)">
                                            {{ formatFileStatus(file.status) }}
                                        </BadgeComponent>
                                        <BadgeComponent
                                            v-if="file.excludeFromTranslation"
                                            variant="warning">
                                            Excluded
                                        </BadgeComponent>
                                    </div>
                                </div>

                                <div class="mt-3 grid gap-3 xl:grid-cols-[220px_minmax(0,1fr)]">
                                    <div>
                                        <label class="text-primary-content/80 mb-1 block text-xs">
                                            Source Language Override
                                        </label>
                                        <select
                                            v-model="getFileDraft(file).selectedSourceLanguage"
                                            class="bg-primary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2 text-sm">
                                            <option value="">Auto / detected</option>
                                            <option
                                                v-for="language in languages"
                                                :key="`source-${file.id}-${language.code}`"
                                                :value="language.code">
                                                {{ language.name }} ({{
                                                    language.code.toUpperCase()
                                                }})
                                            </option>
                                        </select>
                                        <p class="text-primary-content/50 mt-1 text-[11px]">
                                            Detected:
                                            {{
                                                file.detectedSourceLanguage
                                                    ? file.detectedSourceLanguage.toUpperCase()
                                                    : 'unknown'
                                            }}
                                        </p>
                                    </div>

                                    <div v-if="file.fileKind === UPLOAD_BATCH_FILE_KIND.MEDIA">
                                        <label class="text-primary-content/80 mb-1 block text-xs">
                                            Embedded Subtitle Stream
                                        </label>
                                        <select
                                            :value="getSelectedStreamModel(file)"
                                            class="bg-primary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2 text-sm"
                                            @change="setSelectedStream(file, $event)">
                                            <option value="">No stream selected</option>
                                            <option
                                                v-for="stream in file.subtitleStreams.filter(
                                                    (item) => item.isTextBased
                                                )"
                                                :key="stream.id"
                                                :value="stream.streamIndex">
                                                #{{ stream.streamIndex }}
                                                {{
                                                    stream.language
                                                        ? stream.language.toUpperCase()
                                                        : 'und'
                                                }}
                                                {{ stream.title || stream.codecName }}
                                            </option>
                                        </select>
                                        <p class="text-primary-content/50 mt-1 text-[11px]">
                                            {{
                                                file.subtitleStreams.filter(
                                                    (item) => item.isTextBased
                                                ).length
                                            }}
                                            text-based stream(s)
                                        </p>
                                    </div>
                                </div>

                                <div class="mt-3 flex flex-wrap items-center gap-4 text-sm">
                                    <label class="flex items-center gap-2">
                                        <input
                                            v-model="getFileDraft(file).excludeFromTranslation"
                                            type="checkbox" />
                                        <span>Exclude from translation</span>
                                    </label>
                                    <label
                                        v-if="file.fileKind === UPLOAD_BATCH_FILE_KIND.MEDIA"
                                        class="flex items-center gap-2">
                                        <input
                                            v-model="getFileDraft(file).embedTranslatedSubtitle"
                                            type="checkbox" />
                                        <span>Embed/remux translated subtitle</span>
                                    </label>
                                </div>

                                <div class="mt-3 flex flex-wrap gap-2">
                                    <button
                                        class="bg-accent text-secondary-content rounded-md px-3 py-2 text-xs font-medium"
                                        :disabled="isFileActionLoading(file.id)"
                                        @click="saveFile(file)">
                                        {{
                                            isFileActionLoading(file.id) ? 'Saving...' : 'Save File'
                                        }}
                                    </button>
                                    <button
                                        class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-xs"
                                        :disabled="isFileActionLoading(file.id)"
                                        @click="reprobeFile(file)">
                                        Reprobe
                                    </button>
                                </div>

                                <div class="mt-3">
                                    <UploadFileStatusMessage :file="file" />
                                </div>
                            </div>
                        </div>
                    </div>
                </CardComponent>

                <CardComponent title="Artifacts">
                    <template #description>
                        Download or delete generated output files separately from original uploads.
                    </template>

                    <div v-if="!selectedBatch" class="text-primary-content/60 text-sm">
                        Select a batch to view its artifacts.
                    </div>
                    <div
                        v-else-if="selectedArtifacts.length === 0"
                        class="text-primary-content/60 text-sm">
                        No artifacts available yet.
                    </div>

                    <div v-else class="grid gap-3 xl:grid-cols-2">
                        <div class="bg-secondary border-accent/15 rounded-md border p-3">
                            <h3 class="text-primary-content text-sm font-semibold">Output Files</h3>
                            <div
                                v-if="outputArtifacts.length === 0"
                                class="text-primary-content/60 mt-2 text-xs">
                                No generated outputs yet.
                            </div>
                            <div v-else class="mt-2 max-h-[320px] space-y-2 overflow-y-auto pr-1">
                                <div
                                    v-for="artifact in outputArtifacts"
                                    :key="artifact.id"
                                    class="bg-primary border-accent/10 rounded-md border p-2">
                                    <div class="flex flex-wrap items-center justify-between gap-2">
                                        <div class="min-w-0">
                                            <p class="text-primary-content truncate text-sm">
                                                {{ artifact.fileName }}
                                            </p>
                                            <p class="text-primary-content/60 text-xs">
                                                {{ formatArtifactKind(artifact.kind) }} -
                                                {{ formatBytes(artifact.fileSizeBytes) }}
                                            </p>
                                        </div>
                                        <div class="flex gap-2">
                                            <button
                                                v-if="artifact.isDownloadable"
                                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-2 py-1 text-xs"
                                                @click="downloadArtifact(artifact)">
                                                Download
                                            </button>
                                            <button
                                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-2 py-1 text-xs"
                                                @click="deleteArtifact(artifact)">
                                                Delete
                                            </button>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <div class="bg-secondary border-accent/15 rounded-md border p-3">
                            <h3 class="text-primary-content text-sm font-semibold">
                                Original Files
                            </h3>
                            <div
                                v-if="originalArtifacts.length === 0"
                                class="text-primary-content/60 mt-2 text-xs">
                                No original uploads tracked.
                            </div>
                            <div v-else class="mt-2 max-h-[320px] space-y-2 overflow-y-auto pr-1">
                                <div
                                    v-for="artifact in originalArtifacts"
                                    :key="artifact.id"
                                    class="bg-primary border-accent/10 rounded-md border p-2">
                                    <div class="flex flex-wrap items-center justify-between gap-2">
                                        <div class="min-w-0">
                                            <p class="text-primary-content truncate text-sm">
                                                {{ artifact.fileName }}
                                            </p>
                                            <p class="text-primary-content/60 text-xs">
                                                {{ formatArtifactKind(artifact.kind) }} -
                                                {{ formatBytes(artifact.fileSizeBytes) }}
                                            </p>
                                        </div>
                                        <div class="flex gap-2">
                                            <button
                                                v-if="artifact.isDownloadable"
                                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-2 py-1 text-xs"
                                                @click="downloadArtifact(artifact)">
                                                Download
                                            </button>
                                            <button
                                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-2 py-1 text-xs"
                                                @click="deleteArtifact(artifact)">
                                                Delete
                                            </button>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </CardComponent>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import axios from 'axios'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import services from '@/services'
import CardComponent from '@/components/common/CardComponent.vue'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import UploadFileStatusMessage from '@/components/features/upload-workspace/UploadFileStatusMessage.vue'
import { useSettingStore } from '@/store/setting'
import { createUploadQueueItemId } from '@/utils/uploadQueue'
import {
    ICreateUploadChunkSessionRequest,
    ILanguage,
    SETTINGS,
    IUploadArtifact,
    IUploadBatch,
    IUploadBatchFile,
    IUpdateUploadBatchFileRequest,
    UPLOAD_ARTIFACT_KIND,
    UPLOAD_BATCH_FILE_KIND,
    UPLOAD_BATCH_FILE_STATUS,
    UPLOAD_BATCH_STATUS
} from '@/ts'

interface BatchFormModel {
    name: string
    targetLanguage: string
    defaultRemuxEnabled: boolean
}

interface UploadQueueItem {
    id: string
    name: string
    size: number
    status: 'Queued' | 'Creating upload' | 'Uploading' | 'Finalizing' | 'Uploaded' | 'Failed'
    loadedBytes: number
    totalBytes: number
    percent: number
    error: string
}

interface UploadFileDraft {
    selectedSourceLanguage: string
    excludeFromTranslation: boolean
    embedTranslatedSubtitle: boolean
    selectedEmbeddedStreamIndex: number | null
}

const batchActionInProgress = ref(false)
const uploading = ref(false)
const isDropActive = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const selectedBatchId = ref<number | null>(null)
const uploadSpeedBytesPerSecond = ref(0)
const uploadQueue = ref<UploadQueueItem[]>([])
const fileInputRef = ref<HTMLInputElement | null>(null)
const targetLanguageTouched = ref(false)
const settingStore = useSettingStore()

const DEFAULT_CHUNK_SIZE_BYTES = 8 * 1024 * 1024
let uploadSpeedLastLoadedBytes = 0
let uploadSpeedLastMeasuredAt = Date.now()

const batches = ref<IUploadBatch[]>([])
const languages = ref<ILanguage[]>([])
const fileDrafts = ref<Record<number, UploadFileDraft>>({})
const fileLoadingState = ref<Record<number, boolean>>({})

const defaultBatchForm = (): BatchFormModel => ({
    name: '',
    targetLanguage: '',
    defaultRemuxEnabled: true
})

const batchForm = ref<BatchFormModel>(defaultBatchForm())

const selectedBatch = computed<IUploadBatch | null>(() => {
    if (!selectedBatchId.value) {
        return null
    }

    return batches.value.find((item) => item.id === selectedBatchId.value) || null
})

const selectedArtifacts = computed<IUploadArtifact[]>(() => {
    if (!selectedBatch.value) {
        return []
    }

    const map = new Map<number, IUploadArtifact>()

    selectedBatch.value.artifacts.forEach((artifact) => {
        map.set(artifact.id, artifact)
    })

    selectedBatch.value.files.forEach((file) => {
        file.artifacts.forEach((artifact) => {
            map.set(artifact.id, artifact)
        })
    })

    return [...map.values()].sort((left, right) => right.id - left.id)
})

const outputArtifacts = computed(() => {
    return selectedArtifacts.value.filter(
        (artifact) => artifact.kind !== UPLOAD_ARTIFACT_KIND.ORIGINAL_UPLOAD
    )
})

const originalArtifacts = computed(() => {
    return selectedArtifacts.value.filter(
        (artifact) => artifact.kind === UPLOAD_ARTIFACT_KIND.ORIGINAL_UPLOAD
    )
})

const uploadProgressPercent = computed(() => {
    const totalBytes = uploadQueue.value.reduce(
        (sum, item) => sum + Math.max(item.totalBytes, 0),
        0
    )
    if (totalBytes <= 0) {
        return 0
    }

    const loadedBytes = uploadQueue.value.reduce(
        (sum, item) => sum + Math.max(Math.min(item.loadedBytes, item.totalBytes), 0),
        0
    )
    return Math.min(100, Math.round((loadedBytes / totalBytes) * 100))
})

const uploadTargetLanguageBlocker = computed(() => {
    const hasSelectedBatch = Boolean(selectedBatchId.value)
    const hasTargetLanguage = Boolean(batchForm.value.targetLanguage.trim())
    if (!hasSelectedBatch && !hasTargetLanguage) {
        return 'Set a target language in Subtitle settings or choose one for this batch.'
    }
    return ''
})

const setMessage = (success: string, error = '') => {
    successMessage.value = success
    errorMessage.value = error
}

const setError = (message: string) => {
    errorMessage.value = message
    successMessage.value = ''
}

const normalizeLanguageValue = (value: string) => value.trim().toLowerCase()

const toErrorMessage = (error: unknown, fallback: string): string => {
    if (axios.isAxiosError(error)) {
        const responseDataRaw = error.response?.data
        if (typeof responseDataRaw === 'string' && responseDataRaw.trim()) {
            return responseDataRaw
        }

        const responseData = responseDataRaw as
            | {
                  title?: string
                  message?: string
                  detail?: string
                  errors?: Record<string, string[]>
              }
            | undefined

        if (responseData?.message) {
            return responseData.message
        }
        if (responseData?.title) {
            return responseData.title
        }
        if (responseData?.detail) {
            return responseData.detail
        }
        if (responseData?.errors) {
            const firstKey = Object.keys(responseData.errors)[0]
            const firstMessage = responseData.errors[firstKey]?.[0]
            if (firstMessage) {
                return firstMessage
            }
        }
        if (error.response?.statusText) {
            return error.response.statusText
        }
        if (error.code === 'ERR_NETWORK') {
            return 'Network error. Check your connection and try again.'
        }
        if (error.code === 'ECONNABORTED') {
            return 'The upload timed out before the server responded.'
        }
        if (typeof error.message === 'string' && error.message.trim()) {
            return error.message
        }
    }

    const response = error as
        | {
              data?:
                  | string
                  | {
                        title?: string
                        message?: string
                        detail?: string
                        errors?: Record<string, string[]>
                    }
              message?: string
              statusText?: string
          }
        | undefined

    if (typeof response?.data === 'string' && response.data.trim()) {
        return response.data
    }

    const responseData = response?.data as
        | {
              title?: string
              message?: string
              detail?: string
              errors?: Record<string, string[]>
          }
        | undefined

    if (responseData?.message) {
        return responseData.message
    }
    if (responseData?.title) {
        return responseData.title
    }
    if (responseData?.detail) {
        return responseData.detail
    }
    if (responseData?.errors) {
        const firstKey = Object.keys(responseData.errors)[0]
        const firstMessage = responseData.errors[firstKey]?.[0]
        if (firstMessage) {
            return firstMessage
        }
    }
    if (response?.statusText) {
        return response.statusText
    }
    if (response?.message?.trim()) {
        return response.message
    }
    if (error instanceof Error && error.message.trim()) {
        return error.message
    }
    if (typeof error === 'string' && error.trim()) {
        return error
    }
    return fallback
}

const getGlobalTargetLanguageDefault = (): string => {
    const rawTargetLanguages = settingStore.getSetting(SETTINGS.TARGET_LANGUAGES) as
        | ILanguage[]
        | string
        | null
        | undefined

    if (Array.isArray(rawTargetLanguages)) {
        return normalizeLanguageValue(rawTargetLanguages[0]?.code || '')
    }

    if (typeof rawTargetLanguages === 'string' && rawTargetLanguages.trim().length > 0) {
        try {
            const parsed = JSON.parse(rawTargetLanguages) as ILanguage[]
            if (Array.isArray(parsed)) {
                return normalizeLanguageValue(parsed[0]?.code || '')
            }
        } catch {
            return ''
        }
    }

    return ''
}

const applyDefaultTargetLanguage = (force: boolean) => {
    if (selectedBatchId.value) {
        return
    }

    if (!force && targetLanguageTouched.value) {
        return
    }

    const defaultTargetLanguage = getGlobalTargetLanguageDefault()
    if (!defaultTargetLanguage) {
        if (force) {
            batchForm.value.targetLanguage = ''
        }
        return
    }

    batchForm.value.targetLanguage = defaultTargetLanguage
}

const markTargetLanguageTouched = () => {
    targetLanguageTouched.value = true
}

const getTotalLoadedBytes = (): number => {
    return uploadQueue.value.reduce(
        (sum, item) => sum + Math.max(Math.min(item.loadedBytes, item.totalBytes), 0),
        0
    )
}

const resetUploadSpeedTracking = () => {
    uploadSpeedBytesPerSecond.value = 0
    uploadSpeedLastLoadedBytes = getTotalLoadedBytes()
    uploadSpeedLastMeasuredAt = Date.now()
}

const refreshUploadSpeed = () => {
    const now = Date.now()
    const totalLoadedBytes = getTotalLoadedBytes()
    const elapsedSeconds = Math.max((now - uploadSpeedLastMeasuredAt) / 1000, 0.001)
    const loadedDelta = Math.max(totalLoadedBytes - uploadSpeedLastLoadedBytes, 0)

    uploadSpeedBytesPerSecond.value = loadedDelta / elapsedSeconds
    uploadSpeedLastLoadedBytes = totalLoadedBytes
    uploadSpeedLastMeasuredAt = now
}

const createUploadQueue = (files: File[]): UploadQueueItem[] => {
    return files.map((file) => ({
        id: createUploadQueueItemId(file),
        name: file.name,
        size: file.size,
        status: 'Queued',
        loadedBytes: 0,
        totalBytes: file.size,
        percent: 0,
        error: ''
    }))
}

const setQueueItemProgress = (
    queueItem: UploadQueueItem,
    loadedBytes: number,
    totalBytes?: number
) => {
    const normalizedTotal = Math.max(
        totalBytes ?? queueItem.totalBytes ?? queueItem.size,
        queueItem.size,
        1
    )
    const normalizedLoaded = Math.min(Math.max(loadedBytes, 0), normalizedTotal)

    queueItem.totalBytes = normalizedTotal
    queueItem.loadedBytes = normalizedLoaded
    queueItem.percent = Math.min(100, Math.round((normalizedLoaded / normalizedTotal) * 100))
    refreshUploadSpeed()
}

const setQueueItemStatus = (
    queueItem: UploadQueueItem,
    status: UploadQueueItem['status'],
    error = ''
) => {
    queueItem.status = status
    queueItem.error = error
}

const getChunkSizeBytes = (chunkSizeBytes: number | undefined): number => {
    if (!chunkSizeBytes || !Number.isFinite(chunkSizeBytes) || chunkSizeBytes <= 0) {
        return DEFAULT_CHUNK_SIZE_BYTES
    }

    return Math.floor(chunkSizeBytes)
}

const uploadSingleFile = async (
    file: File,
    queueItem: UploadQueueItem,
    batchId: number
): Promise<void> => {
    let uploadId: string | null = null

    try {
        setQueueItemStatus(queueItem, 'Creating upload')
        setQueueItemProgress(queueItem, 0, file.size)

        const request: ICreateUploadChunkSessionRequest = {
            fileName: file.name,
            fileSizeBytes: file.size,
            contentType: file.type || null,
            lastModifiedUtc: new Date(file.lastModified).toISOString()
        }
        const chunkSession = await services.uploadWorkspace.createChunkSession(batchId, request)
        uploadId = chunkSession.uploadId

        const chunkSize = getChunkSizeBytes(chunkSession.chunkSizeBytes)
        const totalBytes = file.size
        const chunkCount = Math.max(1, Math.ceil(totalBytes / chunkSize))
        let committedBytes = 0

        for (let chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++) {
            const chunkStart = chunkIndex * chunkSize
            const chunkEnd = Math.min(chunkStart + chunkSize, totalBytes)
            const chunk = file.slice(chunkStart, chunkEnd)

            setQueueItemStatus(queueItem, 'Uploading')
            await services.uploadWorkspace.uploadChunk(
                batchId,
                uploadId,
                chunkIndex,
                chunk,
                (snapshot) => {
                    setQueueItemProgress(
                        queueItem,
                        committedBytes + Math.min(snapshot.loadedBytes, chunk.size),
                        totalBytes
                    )
                }
            )

            committedBytes += chunk.size
            setQueueItemProgress(queueItem, committedBytes, totalBytes)
        }

        setQueueItemStatus(queueItem, 'Finalizing')
        await services.uploadWorkspace.completeChunkSession(batchId, uploadId)

        setQueueItemProgress(queueItem, file.size, file.size)
        setQueueItemStatus(queueItem, 'Uploaded')
    } catch (error) {
        setQueueItemStatus(
            queueItem,
            'Failed',
            toErrorMessage(error, `Upload failed for ${file.name}.`)
        )

        if (uploadId) {
            try {
                await services.uploadWorkspace.cancelChunkSession(batchId, uploadId)
            } catch {
                // Best-effort cancellation only
            }
        }
    }
}

const formatBytes = (value: number): string => {
    if (!Number.isFinite(value) || value <= 0) {
        return '0 B'
    }

    const units = ['B', 'KB', 'MB', 'GB', 'TB']
    let index = 0
    let size = value

    while (size >= 1024 && index < units.length - 1) {
        size /= 1024
        index++
    }

    const precision = size >= 100 ? 0 : size >= 10 ? 1 : 2
    return `${size.toFixed(precision)} ${units[index]}`
}

const formatSpeed = (bytesPerSecond: number): string => {
    if (!Number.isFinite(bytesPerSecond) || bytesPerSecond <= 0) {
        return '0 B/s'
    }
    return `${formatBytes(bytesPerSecond)}/s`
}

const formatBatchStatus = (status: string): string => {
    switch (status) {
        case UPLOAD_BATCH_STATUS.DRAFT:
            return 'Draft'
        case UPLOAD_BATCH_STATUS.READY:
            return 'Ready'
        case UPLOAD_BATCH_STATUS.PROCESSING:
            return 'Processing'
        case UPLOAD_BATCH_STATUS.COMPLETED:
            return 'Completed'
        case UPLOAD_BATCH_STATUS.FAILED:
            return 'Failed'
        case UPLOAD_BATCH_STATUS.CANCELLED:
            return 'Cancelled'
        case UPLOAD_BATCH_STATUS.EXPIRED:
            return 'Expired'
        default:
            return status
    }
}

const formatFileStatus = (status: string): string => {
    switch (status) {
        case UPLOAD_BATCH_FILE_STATUS.NEEDS_CONFIGURATION:
            return 'Needs Configuration'
        default:
            return status
    }
}

const formatFileKind = (kind: string): string => {
    switch (kind) {
        case UPLOAD_BATCH_FILE_KIND.SUBTITLE:
            return 'Subtitle'
        case UPLOAD_BATCH_FILE_KIND.MEDIA:
            return 'Media'
        default:
            return kind
    }
}

const formatArtifactKind = (kind: string): string => {
    switch (kind) {
        case UPLOAD_ARTIFACT_KIND.ORIGINAL_UPLOAD:
            return 'Original Upload'
        case UPLOAD_ARTIFACT_KIND.EXTRACTED_SUBTITLE:
            return 'Extracted Subtitle'
        case UPLOAD_ARTIFACT_KIND.TRANSLATED_SUBTITLE:
            return 'Translated Subtitle'
        case UPLOAD_ARTIFACT_KIND.REMUXED_MEDIA:
            return 'Remuxed Media'
        default:
            return kind
    }
}

const getBatchBadgeVariant = (
    status: string
): 'default' | 'success' | 'warning' | 'error' | 'info' => {
    switch (status) {
        case UPLOAD_BATCH_STATUS.COMPLETED:
            return 'success'
        case UPLOAD_BATCH_STATUS.PROCESSING:
            return 'info'
        case UPLOAD_BATCH_STATUS.FAILED:
            return 'error'
        case UPLOAD_BATCH_STATUS.CANCELLED:
        case UPLOAD_BATCH_STATUS.EXPIRED:
            return 'warning'
        default:
            return 'default'
    }
}

const getFileBadgeVariant = (
    status: string
): 'default' | 'success' | 'warning' | 'error' | 'info' => {
    switch (status) {
        case UPLOAD_BATCH_FILE_STATUS.READY:
        case UPLOAD_BATCH_FILE_STATUS.COMPLETED:
            return 'success'
        case UPLOAD_BATCH_FILE_STATUS.PROCESSING:
        case UPLOAD_BATCH_FILE_STATUS.QUEUED:
            return 'info'
        case UPLOAD_BATCH_FILE_STATUS.FAILED:
            return 'error'
        case UPLOAD_BATCH_FILE_STATUS.CANCELLED:
        case UPLOAD_BATCH_FILE_STATUS.NEEDS_CONFIGURATION:
            return 'warning'
        default:
            return 'default'
    }
}

const getQueueBadgeVariant = (
    status: UploadQueueItem['status']
): 'default' | 'success' | 'warning' | 'error' | 'info' => {
    switch (status) {
        case 'Uploaded':
            return 'success'
        case 'Creating upload':
        case 'Uploading':
        case 'Finalizing':
            return 'info'
        case 'Failed':
            return 'error'
        default:
            return 'default'
    }
}

const openFilePicker = () => {
    fileInputRef.value?.click()
}

const resetBatchForm = () => {
    batchForm.value = defaultBatchForm()
    targetLanguageTouched.value = false
    applyDefaultTargetLanguage(true)
}

const syncBatchFormWithSelection = (batch: IUploadBatch | null) => {
    if (!batch) {
        targetLanguageTouched.value = false
        applyDefaultTargetLanguage(true)
        return
    }

    batchForm.value = {
        name: batch.name,
        targetLanguage: batch.targetLanguage,
        defaultRemuxEnabled: batch.defaultRemuxEnabled
    }
    targetLanguageTouched.value = false
}

const syncFileDrafts = (files: IUploadBatchFile[]) => {
    const nextDrafts: Record<number, UploadFileDraft> = {}
    files.forEach((file) => {
        nextDrafts[file.id] = {
            selectedSourceLanguage: file.selectedSourceLanguage || '',
            excludeFromTranslation: file.excludeFromTranslation,
            embedTranslatedSubtitle: file.embedTranslatedSubtitle,
            selectedEmbeddedStreamIndex: file.selectedEmbeddedStreamIndex
        }
    })
    fileDrafts.value = nextDrafts
}

const upsertBatch = (batch: IUploadBatch) => {
    const index = batches.value.findIndex((item) => item.id === batch.id)
    if (index === -1) {
        batches.value = [batch, ...batches.value]
    } else {
        batches.value[index] = batch
    }
}

const sortBatches = () => {
    batches.value = [...batches.value].sort((left, right) => {
        return new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime()
    })
}

const loadLanguages = async () => {
    try {
        languages.value = await services.translate.getLanguages<ILanguage[]>()
    } catch {
        languages.value = []
    }
}

const loadBatches = async () => {
    try {
        batches.value = await services.uploadWorkspace.listBatches()
        sortBatches()

        if (selectedBatchId.value) {
            const exists = batches.value.some((item) => item.id === selectedBatchId.value)
            if (!exists) {
                selectedBatchId.value = batches.value[0]?.id || null
            }
        } else {
            selectedBatchId.value = batches.value[0]?.id || null
        }
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to load upload batches.'))
    }
}

const selectBatch = async (batchId: number) => {
    selectedBatchId.value = batchId
    await refreshSelectedBatch()
}

const refreshSelectedBatch = async () => {
    if (!selectedBatchId.value) {
        return
    }

    try {
        const batch = await services.uploadWorkspace.getBatch(selectedBatchId.value)
        upsertBatch(batch)
        sortBatches()
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to load the selected batch.'))
    }
}

const createBatch = async (): Promise<number | null> => {
    if (!batchForm.value.targetLanguage.trim()) {
        setError('Target language is required to create a batch.')
        return null
    }

    batchActionInProgress.value = true

    try {
        const created = await services.uploadWorkspace.createBatch({
            name: batchForm.value.name.trim() || null,
            targetLanguage: normalizeLanguageValue(batchForm.value.targetLanguage),
            defaultRemuxEnabled: batchForm.value.defaultRemuxEnabled
        })

        upsertBatch(created)
        sortBatches()
        selectedBatchId.value = created.id
        setMessage(`Created batch "${created.name}".`)
        return created.id
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to create batch.'))
        return null
    } finally {
        batchActionInProgress.value = false
    }
}

const updateSelectedBatch = async () => {
    if (!selectedBatchId.value) {
        return
    }

    if (!batchForm.value.targetLanguage.trim()) {
        setError('Target language is required.')
        return
    }

    batchActionInProgress.value = true

    try {
        const updated = await services.uploadWorkspace.updateBatch(selectedBatchId.value, {
            name: batchForm.value.name.trim() || 'Upload Batch',
            targetLanguage: normalizeLanguageValue(batchForm.value.targetLanguage),
            defaultRemuxEnabled: batchForm.value.defaultRemuxEnabled
        })

        upsertBatch(updated)
        sortBatches()
        setMessage('Batch updated.')
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to update batch.'))
    } finally {
        batchActionInProgress.value = false
    }
}

const deleteSelectedBatch = async () => {
    if (!selectedBatchId.value) {
        return
    }

    const confirmed = window.confirm('Delete this upload batch and all stored files?')
    if (!confirmed) {
        return
    }

    batchActionInProgress.value = true

    try {
        await services.uploadWorkspace.deleteBatch(selectedBatchId.value)
        const removedId = selectedBatchId.value
        batches.value = batches.value.filter((item) => item.id !== removedId)
        selectedBatchId.value = batches.value[0]?.id || null
        setMessage('Batch deleted.')
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to delete batch.'))
    } finally {
        batchActionInProgress.value = false
    }
}

const startSelectedBatch = async () => {
    if (!selectedBatchId.value) {
        return
    }

    batchActionInProgress.value = true

    try {
        const result = await services.uploadWorkspace.startBatch<number | { queuedCount?: number }>(
            selectedBatchId.value
        )
        const queuedCount = typeof result === 'number' ? result : result?.queuedCount || 0
        await refreshSelectedBatch()
        setMessage(`Batch started. Queued ${queuedCount} request(s).`)
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to start batch.'))
    } finally {
        batchActionInProgress.value = false
    }
}

const cancelSelectedBatch = async () => {
    if (!selectedBatchId.value) {
        return
    }

    batchActionInProgress.value = true

    try {
        await services.uploadWorkspace.cancelBatch(selectedBatchId.value)
        await refreshSelectedBatch()
        setMessage('Batch cancellation requested.')
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to cancel batch.'))
    } finally {
        batchActionInProgress.value = false
    }
}

const ensureBatchForUpload = async (): Promise<number | null> => {
    if (selectedBatchId.value) {
        return selectedBatchId.value
    }

    return await createBatch()
}

const uploadFiles = async (files: File[]) => {
    if (files.length === 0) {
        return
    }

    try {
        uploadQueue.value = createUploadQueue(files)
        resetUploadSpeedTracking()
        uploading.value = true
        isDropActive.value = false

        if (uploadTargetLanguageBlocker.value) {
            const blockerMessage = uploadTargetLanguageBlocker.value
            uploadQueue.value.forEach((item) => {
                setQueueItemStatus(item, 'Failed', blockerMessage)
            })
            setError(blockerMessage)
            return
        }

        const batchId = await ensureBatchForUpload()
        if (!batchId) {
            const batchError = errorMessage.value || 'Failed to create or select an upload batch.'
            uploadQueue.value.forEach((item) => {
                setQueueItemStatus(item, 'Failed', batchError)
            })
            return
        }

        let failedCount = 0
        for (let index = 0; index < files.length; index++) {
            const queueItem = uploadQueue.value[index]
            if (!queueItem) {
                continue
            }

            await uploadSingleFile(files[index], queueItem, batchId)
            if (queueItem.status === 'Failed') {
                failedCount++
            }
        }

        await loadBatches()
        selectedBatchId.value = batchId
        await refreshSelectedBatch()

        if (failedCount > 0) {
            const uploadedCount = files.length - failedCount
            setMessage(
                `${uploadedCount} file${uploadedCount === 1 ? '' : 's'} uploaded, ${failedCount} failed.`
            )
        } else {
            setMessage('Upload finished.')
        }
    } catch (error) {
        setError(toErrorMessage(error, 'Upload failed.'))
    } finally {
        uploadSpeedBytesPerSecond.value = 0
        uploading.value = false
    }
}

const handleFilePicker = async (event: Event) => {
    const input = event.target as HTMLInputElement
    const files = input.files ? [...input.files] : []
    input.value = ''
    await uploadFiles(files)
}

const handleDrop = async (event: DragEvent) => {
    isDropActive.value = false
    const files = event.dataTransfer?.files ? [...event.dataTransfer.files] : []
    await uploadFiles(files)
}

const isFileActionLoading = (fileId: number): boolean => {
    return Boolean(fileLoadingState.value[fileId])
}

const setFileActionLoading = (fileId: number, value: boolean) => {
    fileLoadingState.value = {
        ...fileLoadingState.value,
        [fileId]: value
    }
}

const getFileDraft = (file: IUploadBatchFile): UploadFileDraft => {
    const existing = fileDrafts.value[file.id]
    if (existing) {
        return existing
    }

    const draft: UploadFileDraft = {
        selectedSourceLanguage: file.selectedSourceLanguage || '',
        excludeFromTranslation: file.excludeFromTranslation,
        embedTranslatedSubtitle: file.embedTranslatedSubtitle,
        selectedEmbeddedStreamIndex: file.selectedEmbeddedStreamIndex
    }

    fileDrafts.value[file.id] = draft
    return draft
}

const getSelectedStreamModel = (file: IUploadBatchFile): string => {
    const value = getFileDraft(file).selectedEmbeddedStreamIndex
    return value == null ? '' : value.toString()
}

const setSelectedStream = (file: IUploadBatchFile, event: Event) => {
    const element = event.target as HTMLSelectElement
    const rawValue = element.value.trim()
    const nextValue = rawValue === '' ? null : Number.parseInt(rawValue, 10)
    getFileDraft(file).selectedEmbeddedStreamIndex =
        nextValue === null || Number.isNaN(nextValue) ? null : nextValue
}

const saveFile = async (file: IUploadBatchFile) => {
    if (!selectedBatchId.value) {
        return
    }

    const draft = getFileDraft(file)
    const request: IUpdateUploadBatchFileRequest = {
        selectedSourceLanguage: draft.selectedSourceLanguage
            ? normalizeLanguageValue(draft.selectedSourceLanguage)
            : null,
        excludeFromTranslation: draft.excludeFromTranslation,
        embedTranslatedSubtitle: draft.embedTranslatedSubtitle,
        selectedEmbeddedStreamIndex: draft.selectedEmbeddedStreamIndex
    }

    setFileActionLoading(file.id, true)

    try {
        await services.uploadWorkspace.updateFile(selectedBatchId.value, file.id, request)
        await refreshSelectedBatch()
        setMessage(`Updated ${file.originalFileName}.`)
    } catch (error) {
        setError(toErrorMessage(error, `Failed to update ${file.originalFileName}.`))
    } finally {
        setFileActionLoading(file.id, false)
    }
}

const reprobeFile = async (file: IUploadBatchFile) => {
    if (!selectedBatchId.value) {
        return
    }

    setFileActionLoading(file.id, true)

    try {
        await services.uploadWorkspace.reprobeFile(selectedBatchId.value, file.id)
        await refreshSelectedBatch()
        setMessage(`Reprobed ${file.originalFileName}.`)
    } catch (error) {
        setError(toErrorMessage(error, `Failed to reprobe ${file.originalFileName}.`))
    } finally {
        setFileActionLoading(file.id, false)
    }
}

const downloadArtifact = async (artifact: IUploadArtifact) => {
    try {
        const blob = await services.uploadWorkspace.downloadArtifact(artifact.id)
        const url = URL.createObjectURL(blob)
        const anchor = document.createElement('a')
        anchor.href = url
        anchor.download = artifact.fileName
        document.body.appendChild(anchor)
        anchor.click()
        document.body.removeChild(anchor)
        URL.revokeObjectURL(url)
    } catch {
        if (artifact.downloadUrl) {
            window.open(artifact.downloadUrl, '_blank')
            return
        }

        setError(`Failed to download ${artifact.fileName}.`)
    }
}

const deleteArtifact = async (artifact: IUploadArtifact) => {
    const confirmed = window.confirm(`Delete artifact "${artifact.fileName}"?`)
    if (!confirmed) {
        return
    }

    try {
        await services.uploadWorkspace.deleteArtifact(artifact.id)
        await refreshSelectedBatch()
        setMessage(`Deleted artifact ${artifact.fileName}.`)
    } catch (error) {
        setError(toErrorMessage(error, `Failed to delete ${artifact.fileName}.`))
    }
}

watch(
    selectedBatch,
    (batch) => {
        syncBatchFormWithSelection(batch)
        syncFileDrafts(batch?.files || [])
    },
    { immediate: true }
)

watch(
    () => settingStore.getSetting(SETTINGS.TARGET_LANGUAGES),
    () => {
        applyDefaultTargetLanguage(false)
    },
    { deep: true }
)

let refreshTimer: number | null = null

onMounted(async () => {
    if (!settingStore.getSetting(SETTINGS.TARGET_LANGUAGES)) {
        await settingStore.applySettingsOnLoad()
    }

    await Promise.all([loadLanguages(), loadBatches()])
    applyDefaultTargetLanguage(true)

    if (selectedBatchId.value) {
        await refreshSelectedBatch()
    }

    refreshTimer = window.setInterval(async () => {
        if (!selectedBatch.value || uploading.value) {
            return
        }

        const hasActiveFiles = selectedBatch.value.files.some(
            (file) =>
                file.status === UPLOAD_BATCH_FILE_STATUS.QUEUED ||
                file.status === UPLOAD_BATCH_FILE_STATUS.PROCESSING
        )

        if (selectedBatch.value.status === UPLOAD_BATCH_STATUS.PROCESSING || hasActiveFiles) {
            await refreshSelectedBatch()
        }
    }, 5000)
})

onUnmounted(() => {
    if (refreshTimer) {
        window.clearInterval(refreshTimer)
    }
})
</script>

<template>
    <div class="grid gap-4 p-4 xl:grid-cols-[380px_minmax(0,1fr)]">
        <div class="space-y-4">
            <CardComponent title="Upload Workspace">
                <template #description>
                    Configure the managed storage root, retention, and batch limits used by browser
                    uploads.
                </template>

                <div class="space-y-3">
                    <div>
                        <label class="text-primary-content/80 mb-1 block text-sm">Storage Root</label>
                        <div class="flex gap-2">
                            <input
                                v-model="settings.upload_workspace_storage_root"
                                class="bg-secondary border-accent/20 text-primary-content min-w-0 flex-1 rounded-md border px-3 py-2"
                                type="text" />
                            <button
                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2"
                                @click="isDirectoryModalOpen = true">
                                Browse
                            </button>
                        </div>
                    </div>

                    <div>
                        <label class="text-primary-content/80 mb-1 block text-sm">Retention (days)</label>
                        <input
                            v-model="settings.upload_workspace_retention_days"
                            class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                            type="number"
                            min="1" />
                    </div>

                    <div>
                        <label class="text-primary-content/80 mb-1 block text-sm">
                            Reserved Upload Worker Slots
                        </label>
                        <input
                            v-model="settings.upload_workspace_reserved_worker_slots"
                            class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                            type="number"
                            min="0" />
                    </div>

                    <div>
                        <label class="text-primary-content/80 mb-1 block text-sm">Max Batch Size</label>
                        <input
                            v-model="settings.upload_workspace_max_batch_size"
                            class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                            type="number"
                            min="1" />
                    </div>

                    <div>
                        <label class="text-primary-content/80 mb-1 block text-sm">
                            Max File Size (bytes)
                        </label>
                        <input
                            v-model="settings.upload_workspace_max_file_size_bytes"
                            class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                            type="number"
                            min="1" />
                    </div>

                    <button
                        class="bg-accent text-secondary-content rounded-md px-3 py-2 text-sm font-medium"
                        :disabled="savingSettings"
                        @click="saveSettings">
                        {{ savingSettings ? 'Saving...' : 'Save Workspace Settings' }}
                    </button>
                </div>
            </CardComponent>

            <CardComponent title="Upload Batches">
                <template #description>
                    Create upload batches, pick one target language, and push files directly from
                    your browser.
                </template>

                <div class="space-y-3">
                    <div>
                        <label class="text-primary-content/80 mb-1 block text-sm">Batch Name</label>
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
                            class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                            type="text"
                            placeholder="en" />
                    </div>

                    <label class="flex items-center gap-2 text-sm">
                        <input v-model="batchForm.defaultRemuxEnabled" type="checkbox" />
                        <span>Enable remux by default for media files</span>
                    </label>

                    <div class="flex gap-2">
                        <button
                            class="bg-accent text-secondary-content rounded-md px-3 py-2 text-sm font-medium"
                            :disabled="batchActionInProgress"
                            @click="createBatch">
                            {{ batchActionInProgress ? 'Working...' : 'Create Batch' }}
                        </button>
                        <button
                            class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                            :disabled="batchActionInProgress"
                            @click="resetBatchForm">
                            Clear
                        </button>
                    </div>
                </div>

                <input
                    ref="fileInputRef"
                    class="hidden"
                    type="file"
                    multiple
                    @change="handleFilePicker" />

                <div
                    class="border-accent/30 bg-secondary/40 mt-4 rounded-md border border-dashed p-4"
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
                    <div class="mt-3 flex items-center gap-2">
                        <button
                            class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                            :disabled="uploading"
                            @click="openFilePicker">
                            {{ uploading ? 'Uploading...' : 'Choose Files' }}
                        </button>
                        <span class="text-primary-content/60 text-xs">
                            {{ selectedBatchId ? 'Uploads go to the selected batch.' : 'A batch is created if needed.' }}
                        </span>
                    </div>
                </div>

                <div v-if="uploadQueue.length" class="space-y-2">
                    <div class="mt-3 flex items-center justify-between text-xs">
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

                    <div class="max-h-48 space-y-2 overflow-y-auto">
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
                                {{ formatBytes(item.size) }}
                            </div>
                        </div>
                    </div>
                </div>

                <div v-if="batches.length === 0" class="text-primary-content/60 mt-4 text-sm">
                    No upload batches yet.
                </div>

                <div v-else class="mt-4 max-h-[460px] space-y-2 overflow-y-auto pr-1">
                    <button
                        v-for="batch in batches"
                        :key="batch.id"
                        class="bg-secondary border-accent/20 hover:border-accent/50 w-full rounded-md border p-3 text-left transition-colors"
                        :class="{ 'border-accent': batch.id === selectedBatchId }"
                        @click="selectBatch(batch.id)">
                        <div class="flex items-center justify-between gap-2">
                            <div class="min-w-0">
                                <p class="text-primary-content truncate text-sm font-medium">
                                    {{ batch.name }}
                                </p>
                                <p class="text-primary-content/60 mt-1 text-xs">
                                    {{ batch.targetLanguage.toUpperCase() }} •
                                    {{ batch.fileCount }} file{{ batch.fileCount === 1 ? '' : 's' }}
                                </p>
                            </div>
                            <BadgeComponent :variant="getBatchBadgeVariant(batch.status)">
                                {{ formatBatchStatus(batch.status) }}
                            </BadgeComponent>
                        </div>
                    </button>
                </div>
            </CardComponent>
        </div>

        <CardComponent :title="selectedBatch ? selectedBatch.name : 'Batch Review'">
            <template #description>
                Review each uploaded file, override source language or stream selections, and manage
                output artifacts.
            </template>

            <div v-if="!selectedBatch" class="text-primary-content/60 text-sm">
                Select a batch to configure files and start processing.
            </div>

            <div v-else class="space-y-4">
                <div class="bg-secondary border-accent/15 rounded-md border p-3">
                    <div class="grid gap-3 xl:grid-cols-3">
                        <div>
                            <label class="text-primary-content/80 mb-1 block text-sm">Name</label>
                            <input
                                v-model="batchForm.name"
                                class="bg-primary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                                type="text" />
                        </div>

                        <div>
                            <label class="text-primary-content/80 mb-1 block text-sm">
                                Target Language
                            </label>
                            <select
                                v-if="languages.length > 0"
                                v-model="batchForm.targetLanguage"
                                class="bg-primary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2">
                                <option value="">Select target language</option>
                                <option
                                    v-for="language in languages"
                                    :key="`target-${language.code}`"
                                    :value="language.code">
                                    {{ language.name }} ({{ language.code.toUpperCase() }})
                                </option>
                            </select>
                            <input
                                v-else
                                v-model="batchForm.targetLanguage"
                                class="bg-primary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                                type="text" />
                        </div>

                        <label class="flex items-center gap-2 pt-6 text-sm">
                            <input v-model="batchForm.defaultRemuxEnabled" type="checkbox" />
                            <span>Default remux for media</span>
                        </label>
                    </div>

                    <div class="mt-3 flex flex-wrap gap-2">
                        <button
                            class="bg-accent text-secondary-content rounded-md px-3 py-2 text-sm font-medium"
                            :disabled="batchActionInProgress"
                            @click="updateSelectedBatch">
                            {{ batchActionInProgress ? 'Saving...' : 'Save Batch' }}
                        </button>
                        <button
                            class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                            :disabled="batchActionInProgress"
                            @click="refreshSelectedBatch">
                            Refresh
                        </button>
                        <button
                            class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                            :disabled="batchActionInProgress || selectedBatch.status === UPLOAD_BATCH_STATUS.PROCESSING"
                            @click="startSelectedBatch">
                            Start Batch
                        </button>
                        <button
                            class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-sm"
                            :disabled="batchActionInProgress || selectedBatch.status !== UPLOAD_BATCH_STATUS.PROCESSING"
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

                    <div class="text-primary-content/60 mt-3 flex flex-wrap gap-2 text-xs">
                        <BadgeComponent :variant="getBatchBadgeVariant(selectedBatch.status)">
                            {{ formatBatchStatus(selectedBatch.status) }}
                        </BadgeComponent>
                        <span>{{ selectedBatch.fileCount }} files</span>
                        <span>Completed: {{ selectedBatch.completedFileCount }}</span>
                        <span>Failed: {{ selectedBatch.failedFileCount }}</span>
                        <span>Active: {{ selectedBatch.activeFileCount }}</span>
                    </div>
                </div>

                <div v-if="selectedBatch.files.length === 0" class="text-primary-content/60 text-sm">
                    No files uploaded into this batch yet.
                </div>

                <div v-else class="max-h-[560px] space-y-3 overflow-y-auto pr-1">
                    <div
                        v-for="file in selectedBatch.files"
                        :key="file.id"
                        class="bg-secondary border-accent/15 rounded-md border p-3">
                        <div class="flex flex-wrap items-center justify-between gap-2">
                            <div class="min-w-0">
                                <p class="text-primary-content truncate text-sm font-medium">
                                    {{ file.originalFileName }}
                                </p>
                                <p class="text-primary-content/60 mt-1 text-xs">
                                    {{ formatBytes(file.fileSizeBytes) }}
                                </p>
                            </div>
                            <div class="flex flex-wrap gap-2">
                                <BadgeComponent
                                    :variant="file.fileKind === UPLOAD_BATCH_FILE_KIND.MEDIA ? 'info' : 'default'">
                                    {{ formatFileKind(file.fileKind) }}
                                </BadgeComponent>
                                <BadgeComponent :variant="getFileBadgeVariant(file.status)">
                                    {{ formatFileStatus(file.status) }}
                                </BadgeComponent>
                                <BadgeComponent v-if="file.excludeFromTranslation" variant="warning">
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
                                        {{ language.name }} ({{ language.code.toUpperCase() }})
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
                                        v-for="stream in file.subtitleStreams.filter((item) => item.isTextBased)"
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
                                    {{ file.subtitleStreams.filter((item) => item.isTextBased).length }}
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
                                {{ isFileActionLoading(file.id) ? 'Saving...' : 'Save File' }}
                            </button>
                            <button
                                class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2 text-xs"
                                :disabled="isFileActionLoading(file.id)"
                                @click="reprobeFile(file)">
                                Reprobe
                            </button>
                        </div>

                        <p v-if="file.probeError" class="text-warning mt-2 text-xs">
                            Probe: {{ file.probeError }}
                        </p>
                        <p v-if="file.lastError" class="text-error mt-1 text-xs">
                            Error: {{ file.lastError }}
                        </p>
                    </div>
                </div>

                <div class="bg-secondary border-accent/15 rounded-md border p-3">
                    <h3 class="text-primary-content text-sm font-semibold">Artifacts</h3>

                    <div v-if="selectedArtifacts.length === 0" class="text-primary-content/60 mt-2 text-sm">
                        No artifacts available yet.
                    </div>

                    <div v-else class="mt-2 max-h-[320px] space-y-2 overflow-y-auto pr-1">
                        <div
                            v-for="artifact in selectedArtifacts"
                            :key="artifact.id"
                            class="bg-primary border-accent/10 rounded-md border p-2">
                            <div class="flex flex-wrap items-center justify-between gap-2">
                                <div class="min-w-0">
                                    <p class="text-primary-content truncate text-sm">
                                        {{ artifact.fileName }}
                                    </p>
                                    <p class="text-primary-content/60 text-xs">
                                        {{ formatArtifactKind(artifact.kind) }} •
                                        {{ formatBytes(artifact.fileSizeBytes) }}
                                    </p>
                                </div>
                                <div class="flex gap-2">
                                    <button
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

            <p v-if="errorMessage" class="text-error text-sm">
                {{ errorMessage }}
            </p>
            <p v-if="successMessage" class="text-success text-sm">
                {{ successMessage }}
            </p>
        </CardComponent>
    </div>

    <DirectoryModal
        :is-open="isDirectoryModalOpen"
        @close="isDirectoryModalOpen = false"
        @select="handleDirectorySelection" />
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import services from '@/services'
import CardComponent from '@/components/common/CardComponent.vue'
import DirectoryModal from '@/components/features/settings/DirectoryModal.vue'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import {
    ILanguage,
    IUploadArtifact,
    IUploadBatch,
    IUploadBatchFile,
    IUpdateUploadBatchFileRequest,
    UPLOAD_ARTIFACT_KIND,
    UPLOAD_BATCH_FILE_KIND,
    UPLOAD_BATCH_FILE_STATUS,
    UPLOAD_BATCH_STATUS
} from '@/ts'

type UploadWorkspaceSettings = Record<string, string>

interface BatchFormModel {
    name: string
    targetLanguage: string
    defaultRemuxEnabled: boolean
}

interface UploadQueueItem {
    id: string
    name: string
    size: number
    status: 'Queued' | 'Uploading' | 'Uploaded' | 'Failed'
}

interface UploadFileDraft {
    selectedSourceLanguage: string
    excludeFromTranslation: boolean
    embedTranslatedSubtitle: boolean
    selectedEmbeddedStreamIndex: number | null
}

const isDirectoryModalOpen = ref(false)
const savingSettings = ref(false)
const batchActionInProgress = ref(false)
const uploading = ref(false)
const isDropActive = ref(false)
const errorMessage = ref('')
const successMessage = ref('')
const selectedBatchId = ref<number | null>(null)
const uploadProgressPercent = ref(0)
const uploadSpeedBytesPerSecond = ref(0)
const uploadQueue = ref<UploadQueueItem[]>([])
const fileInputRef = ref<HTMLInputElement | null>(null)

const settings = ref<UploadWorkspaceSettings>({
    upload_workspace_storage_root: '',
    upload_workspace_retention_days: '7',
    upload_workspace_reserved_worker_slots: '1',
    upload_workspace_max_batch_size: '100',
    upload_workspace_max_file_size_bytes: '2147483648'
})

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

const settingKeys = Object.keys(settings.value)

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
    const response = error as {
        data?: {
            title?: string
            message?: string
            detail?: string
            errors?: Record<string, string[]>
        }
        statusText?: string
    }

    if (response?.data?.message) {
        return response.data.message
    }
    if (response?.data?.title) {
        return response.data.title
    }
    if (response?.data?.detail) {
        return response.data.detail
    }
    if (response?.data?.errors) {
        const firstKey = Object.keys(response.data.errors)[0]
        const firstMessage = response.data.errors[firstKey]?.[0]
        if (firstMessage) {
            return firstMessage
        }
    }
    if (response?.statusText) {
        return response.statusText
    }
    return fallback
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

const getBatchBadgeVariant = (status: string): 'default' | 'success' | 'warning' | 'error' | 'info' => {
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

const getFileBadgeVariant = (status: string): 'default' | 'success' | 'warning' | 'error' | 'info' => {
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
        case 'Uploading':
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
}

const syncBatchFormWithSelection = (batch: IUploadBatch | null) => {
    if (!batch) {
        return
    }

    batchForm.value = {
        name: batch.name,
        targetLanguage: batch.targetLanguage,
        defaultRemuxEnabled: batch.defaultRemuxEnabled
    }
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

const loadSettings = async () => {
    settings.value = await services.setting.getSettings<UploadWorkspaceSettings>(settingKeys)
}

const saveSettings = async () => {
    savingSettings.value = true
    successMessage.value = ''
    errorMessage.value = ''

    try {
        await Promise.all(
            Object.entries(settings.value).map(([key, value]) => services.setting.setSetting(key, value))
        )
        setMessage('Upload workspace settings saved.')
    } catch (error) {
        setError(toErrorMessage(error, 'Failed to save upload workspace settings.'))
    } finally {
        savingSettings.value = false
    }
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

const setQueueStatus = (status: UploadQueueItem['status']) => {
    uploadQueue.value = uploadQueue.value.map((item) => {
        return {
            ...item,
            status
        }
    })
}

const uploadFiles = async (files: File[]) => {
    if (files.length === 0) {
        return
    }

    const batchId = await ensureBatchForUpload()
    if (!batchId) {
        return
    }

    uploadQueue.value = files.map((file) => ({
        id: `${file.name}-${file.lastModified}`,
        name: file.name,
        size: file.size,
        status: 'Queued'
    }))

    uploadProgressPercent.value = 0
    uploadSpeedBytesPerSecond.value = 0
    uploading.value = true
    isDropActive.value = false

    try {
        await services.uploadWorkspace.uploadFiles(batchId, files, (snapshot) => {
            uploadProgressPercent.value = snapshot.percent
            uploadSpeedBytesPerSecond.value = snapshot.speedBytesPerSecond
            setQueueStatus('Uploading')
        })

        setQueueStatus('Uploaded')
        uploadProgressPercent.value = 100

        await loadBatches()
        selectedBatchId.value = batchId
        await refreshSelectedBatch()
        setMessage('Upload finished.')
    } catch (error) {
        setQueueStatus('Failed')
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

const handleDirectorySelection = (path: string) => {
    settings.value.upload_workspace_storage_root = path
    isDirectoryModalOpen.value = false
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

let refreshTimer: number | null = null

onMounted(async () => {
    await Promise.all([loadSettings(), loadLanguages(), loadBatches()])

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

        if (
            selectedBatch.value.status === UPLOAD_BATCH_STATUS.PROCESSING ||
            hasActiveFiles
        ) {
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

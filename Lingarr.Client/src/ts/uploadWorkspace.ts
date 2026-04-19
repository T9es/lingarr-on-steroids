export const UPLOAD_BATCH_STATUS = {
    DRAFT: 'Draft',
    READY: 'Ready',
    PROCESSING: 'Processing',
    COMPLETED: 'Completed',
    FAILED: 'Failed',
    CANCELLED: 'Cancelled',
    EXPIRED: 'Expired'
} as const

export type UploadBatchStatus = (typeof UPLOAD_BATCH_STATUS)[keyof typeof UPLOAD_BATCH_STATUS]

export const UPLOAD_BATCH_FILE_STATUS = {
    UPLOADED: 'Uploaded',
    NEEDS_CONFIGURATION: 'NeedsConfiguration',
    READY: 'Ready',
    QUEUED: 'Queued',
    PROCESSING: 'Processing',
    COMPLETED: 'Completed',
    FAILED: 'Failed',
    CANCELLED: 'Cancelled'
} as const

export type UploadBatchFileStatus =
    (typeof UPLOAD_BATCH_FILE_STATUS)[keyof typeof UPLOAD_BATCH_FILE_STATUS]

export const UPLOAD_BATCH_FILE_KIND = {
    SUBTITLE: 'Subtitle',
    MEDIA: 'Media'
} as const

export type UploadBatchFileKind =
    (typeof UPLOAD_BATCH_FILE_KIND)[keyof typeof UPLOAD_BATCH_FILE_KIND]

export const UPLOAD_ARTIFACT_KIND = {
    ORIGINAL_UPLOAD: 'OriginalUpload',
    EXTRACTED_SUBTITLE: 'ExtractedSubtitle',
    TRANSLATED_SUBTITLE: 'TranslatedSubtitle',
    REMUXED_MEDIA: 'RemuxedMedia'
} as const

export type UploadArtifactKind = (typeof UPLOAD_ARTIFACT_KIND)[keyof typeof UPLOAD_ARTIFACT_KIND]

export interface IUploadBatchFileSubtitleStream {
    id: number
    streamIndex: number
    language: string | null
    title: string | null
    codecName: string
    isTextBased: boolean
    isDefault: boolean
    isForced: boolean
}

export interface IUploadArtifact {
    id: number
    uploadBatchFileId: number | null
    kind: UploadArtifactKind
    fileName: string
    fileSizeBytes: number
    contentType: string | null
    isDownloadable: boolean
    createdAt: string
    expiresAt: string | null
    downloadUrl: string
}

export interface IUploadBatchFile {
    id: number
    title: string
    originalFileName: string
    fileKind: UploadBatchFileKind
    status: UploadBatchFileStatus
    fileSizeBytes: number
    detectedSourceLanguage: string | null
    selectedSourceLanguage: string | null
    excludeFromTranslation: boolean
    embedTranslatedSubtitle: boolean
    selectedEmbeddedStreamIndex: number | null
    selectedEmbeddedStreamLanguage: string | null
    selectedEmbeddedStreamTitle: string | null
    selectedEmbeddedStreamCodec: string | null
    currentTranslationRequestId: number | null
    probeCompletedAt: string | null
    startedAt: string | null
    completedAt: string | null
    probeError: string | null
    lastError: string | null
    subtitleStreams: IUploadBatchFileSubtitleStream[]
    artifacts: IUploadArtifact[]
}

export interface IUploadBatch {
    id: number
    name: string
    targetLanguage: string
    status: UploadBatchStatus
    defaultRemuxEnabled: boolean
    fileCount: number
    completedFileCount: number
    failedFileCount: number
    activeFileCount: number
    createdAt: string
    startedAt: string | null
    completedAt: string | null
    expiresAt: string | null
    failureReason: string | null
    files: IUploadBatchFile[]
    artifacts: IUploadArtifact[]
}

export interface ICreateUploadBatchRequest {
    name?: string | null
    targetLanguage: string
    defaultRemuxEnabled: boolean
}

export interface IUpdateUploadBatchRequest {
    name: string
    targetLanguage: string
    defaultRemuxEnabled: boolean
}

export interface IUpdateUploadBatchFileRequest {
    selectedSourceLanguage: string | null
    excludeFromTranslation: boolean
    embedTranslatedSubtitle: boolean
    selectedEmbeddedStreamIndex: number | null
}

export interface IUploadProgressSnapshot {
    loadedBytes: number
    totalBytes: number
    percent: number
    speedBytesPerSecond: number
}

export type UploadProgressCallback = (snapshot: IUploadProgressSnapshot) => void

import { AxiosError, AxiosProgressEvent, AxiosResponse, AxiosStatic } from 'axios'
import {
    ICreateUploadBatchRequest,
    ICreateUploadChunkSessionRequest,
    ICreateUploadChunkSessionResponse,
    IUpdateUploadBatchFileRequest,
    IUpdateUploadBatchRequest,
    IUploadArtifact,
    IUploadBatch,
    IUploadBatchFile,
    IUploadChunkResponse,
    IUploadWorkspaceService,
    UploadProgressCallback
} from '@/ts'

const filterArtifacts = (artifacts: IUploadArtifact[], fileId?: number): IUploadArtifact[] => {
    const filtered =
        typeof fileId === 'number'
            ? artifacts.filter((artifact) => artifact.uploadBatchFileId === fileId)
            : artifacts

    return [...filtered].sort((left, right) => right.id - left.id)
}

const toProgressSnapshot = (
    event: AxiosProgressEvent,
    startedAt: number,
    lastLoaded: number,
    lastMeasuredAt: number,
    totalBytesOverride?: number
): { snapshot: Parameters<UploadProgressCallback>[0]; loaded: number; measuredAt: number } => {
    const now = Date.now()
    const loaded = Math.max(0, event.loaded || 0)
    const total = Math.max(0, totalBytesOverride ?? event.total ?? 0)
    const percent = total > 0 ? Math.min(100, Math.round((loaded / total) * 100)) : 0
    const intervalSeconds = Math.max((now - lastMeasuredAt) / 1000, 0.001)
    const speedBytesPerSecond = Math.max(0, (loaded - lastLoaded) / intervalSeconds)

    return {
        snapshot: {
            loadedBytes: loaded,
            totalBytes: total,
            percent,
            speedBytesPerSecond: Number.isFinite(speedBytesPerSecond)
                ? speedBytesPerSecond
                : Math.max(0, loaded / Math.max((now - startedAt) / 1000, 0.001))
        },
        loaded,
        measuredAt: now
    }
}

const toRejection = (error: AxiosError) => {
    return error.response ?? error
}

const service = (http: AxiosStatic, resource = '/api/uploadworkspace'): IUploadWorkspaceService => {
    const batchesResource = `${resource}/batches`
    const artifactsResource = `${resource}/artifacts`

    return {
        createBatch(request: ICreateUploadBatchRequest): Promise<IUploadBatch> {
            return new Promise((resolve, reject) => {
                http.post(batchesResource, request)
                    .then((response: AxiosResponse<IUploadBatch>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        listBatches(): Promise<IUploadBatch[]> {
            return new Promise((resolve, reject) => {
                http.get(batchesResource)
                    .then((response: AxiosResponse<IUploadBatch[]>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        getBatch(batchId: number): Promise<IUploadBatch> {
            return new Promise((resolve, reject) => {
                http.get(`${batchesResource}/${batchId}`)
                    .then((response: AxiosResponse<IUploadBatch>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        updateBatch(batchId: number, request: IUpdateUploadBatchRequest): Promise<IUploadBatch> {
            return new Promise((resolve, reject) => {
                http.put(`${batchesResource}/${batchId}`, request)
                    .then((response: AxiosResponse<IUploadBatch>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        deleteBatch(batchId: number): Promise<void> {
            return new Promise((resolve, reject) => {
                http.delete(`${batchesResource}/${batchId}`)
                    .then(() => resolve())
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        createChunkSession(
            batchId: number,
            request: ICreateUploadChunkSessionRequest
        ): Promise<ICreateUploadChunkSessionResponse> {
            return new Promise((resolve, reject) => {
                http.post(`${batchesResource}/${batchId}/files/chunked`, request)
                    .then((response: AxiosResponse<ICreateUploadChunkSessionResponse>) =>
                        resolve(response.data)
                    )
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        uploadChunk(
            batchId: number,
            uploadId: string,
            chunkIndex: number,
            blob: Blob,
            onProgress?: UploadProgressCallback
        ): Promise<IUploadChunkResponse> {
            return new Promise((resolve, reject) => {
                const startedAt = Date.now()
                let lastLoaded = 0
                let lastMeasuredAt = startedAt

                http.put(
                    `${batchesResource}/${batchId}/files/chunked/${encodeURIComponent(uploadId)}/chunks/${chunkIndex}`,
                    blob,
                    {
                        headers: {
                            'Content-Type': blob.type || 'application/octet-stream'
                        },
                        onUploadProgress: (event: AxiosProgressEvent) => {
                            if (!onProgress) {
                                return
                            }

                            const progress = toProgressSnapshot(
                                event,
                                startedAt,
                                lastLoaded,
                                lastMeasuredAt,
                                blob.size
                            )
                            lastLoaded = progress.loaded
                            lastMeasuredAt = progress.measuredAt
                            onProgress(progress.snapshot)
                        }
                    }
                )
                    .then((response: AxiosResponse<IUploadChunkResponse>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        completeChunkSession(batchId: number, uploadId: string): Promise<IUploadBatch> {
            return new Promise((resolve, reject) => {
                http.post(`${batchesResource}/${batchId}/files/chunked/${encodeURIComponent(uploadId)}/complete`)
                    .then((response: AxiosResponse<IUploadBatch>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        cancelChunkSession(batchId: number, uploadId: string): Promise<void> {
            return new Promise((resolve, reject) => {
                http.delete(`${batchesResource}/${batchId}/files/chunked/${encodeURIComponent(uploadId)}`)
                    .then(() => resolve())
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        uploadFiles(
            batchId: number,
            files: File[],
            onProgress?: UploadProgressCallback
        ): Promise<IUploadBatch> {
            return new Promise((resolve, reject) => {
                const formData = new FormData()
                files.forEach((file) => formData.append('files', file, file.name))

                const startedAt = Date.now()
                let lastLoaded = 0
                let lastMeasuredAt = startedAt

                http.post(`${batchesResource}/${batchId}/files`, formData, {
                    headers: {
                        'Content-Type': 'multipart/form-data'
                    },
                    onUploadProgress: (event: AxiosProgressEvent) => {
                        if (!onProgress) {
                            return
                        }

                        const progress = toProgressSnapshot(event, startedAt, lastLoaded, lastMeasuredAt)
                        lastLoaded = progress.loaded
                        lastMeasuredAt = progress.measuredAt
                        onProgress(progress.snapshot)
                    }
                })
                    .then((response: AxiosResponse<IUploadBatch>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        reprobeFile(batchId: number, fileId: number): Promise<IUploadBatchFile> {
            return new Promise((resolve, reject) => {
                http.post(`${batchesResource}/${batchId}/files/${fileId}/reprobe`)
                    .then((response: AxiosResponse<IUploadBatchFile>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        updateFile(
            batchId: number,
            fileId: number,
            request: IUpdateUploadBatchFileRequest
        ): Promise<IUploadBatchFile> {
            return new Promise((resolve, reject) => {
                http.put(`${batchesResource}/${batchId}/files/${fileId}`, request)
                    .then((response: AxiosResponse<IUploadBatchFile>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        startBatch<T = number>(batchId: number): Promise<T> {
            return new Promise((resolve, reject) => {
                http.post(`${batchesResource}/${batchId}/start`)
                    .then((response: AxiosResponse<T>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        cancelBatch<T = boolean>(batchId: number): Promise<T> {
            return new Promise((resolve, reject) => {
                http.post(`${batchesResource}/${batchId}/cancel`)
                    .then((response: AxiosResponse<T>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        listArtifacts(batchId: number, fileId?: number): Promise<IUploadArtifact[]> {
            return new Promise((resolve, reject) => {
                http.get(`${batchesResource}/${batchId}/artifacts`)
                    .then((response: AxiosResponse<IUploadArtifact[]>) =>
                        resolve(filterArtifacts(response.data, fileId))
                    )
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        downloadArtifact(artifactId: number): Promise<Blob> {
            return new Promise((resolve, reject) => {
                http.get(`${artifactsResource}/${artifactId}/download`, {
                    responseType: 'blob'
                })
                    .then((response: AxiosResponse<Blob>) => resolve(response.data))
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        },

        deleteArtifact(artifactId: number): Promise<void> {
            return new Promise((resolve, reject) => {
                http.delete(`${artifactsResource}/${artifactId}`)
                    .then(() => resolve())
                    .catch((error: AxiosError) => reject(toRejection(error)))
            })
        }
    }
}

export const uploadWorkspaceService = (axios: AxiosStatic): IUploadWorkspaceService => {
    return service(axios)
}

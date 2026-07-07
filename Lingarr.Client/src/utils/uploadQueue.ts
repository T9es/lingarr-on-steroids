let fallbackUploadQueueCounter = 0

const createUploadQueueItemNonce = (): string => {
    const randomUUID = globalThis.crypto?.randomUUID
    if (typeof randomUUID === 'function') {
        return randomUUID.call(globalThis.crypto)
    }

    fallbackUploadQueueCounter += 1
    return `${Date.now().toString(36)}-${fallbackUploadQueueCounter.toString(36)}`
}

export const createUploadQueueItemId = (file: Pick<File, 'lastModified' | 'name'>): string => {
    return `${file.name}-${file.lastModified}-${createUploadQueueItemNonce()}`
}

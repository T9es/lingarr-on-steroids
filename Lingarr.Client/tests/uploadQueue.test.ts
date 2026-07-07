import assert from 'node:assert/strict'
import test from 'node:test'
import { createUploadQueueItemId } from '../src/utils/uploadQueue.ts'

const restoreCrypto = (descriptor?: PropertyDescriptor) => {
    if (descriptor) {
        Object.defineProperty(globalThis, 'crypto', descriptor)
        return
    }

    Reflect.deleteProperty(globalThis, 'crypto')
}

test('createUploadQueueItemId falls back when crypto.randomUUID is unavailable', () => {
    const originalCryptoDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'crypto')

    Object.defineProperty(globalThis, 'crypto', {
        configurable: true,
        value: {}
    })

    try {
        const id = createUploadQueueItemId({
            lastModified: 1713618000000,
            name: 'one.mkv'
        } as Pick<File, 'lastModified' | 'name'>)

        assert.match(id, /^one\.mkv-1713618000000-/)
    } finally {
        restoreCrypto(originalCryptoDescriptor)
    }
})

test('createUploadQueueItemId uses crypto.randomUUID when available', () => {
    const originalCryptoDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'crypto')

    Object.defineProperty(globalThis, 'crypto', {
        configurable: true,
        value: {
            randomUUID: () => 'uuid-123'
        }
    })

    try {
        const id = createUploadQueueItemId({
            lastModified: 1713618000000,
            name: 'one.mkv'
        } as Pick<File, 'lastModified' | 'name'>)

        assert.equal(id, 'one.mkv-1713618000000-uuid-123')
    } finally {
        restoreCrypto(originalCryptoDescriptor)
    }
})

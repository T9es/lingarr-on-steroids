<template>
    <div
        v-if="message"
        class="rounded-md border px-3 py-2 text-xs"
        :class="
            message.variant === 'error'
                ? 'border-error/40 bg-error/10'
                : 'border-warning/40 bg-warning/10'
        ">
        <p
            class="font-semibold"
            :class="message.variant === 'error' ? 'text-error' : 'text-warning'">
            {{ message.title }}
        </p>
        <p class="text-primary-content/80 mt-1">
            {{ message.description }}
        </p>
        <p v-if="message.detail" class="text-primary-content/70 mt-1 break-words">
            {{ message.detail }}
        </p>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { IUploadBatchFile, UPLOAD_BATCH_FILE_KIND, UPLOAD_BATCH_FILE_STATUS } from '@/ts'

interface UploadStatusMessage {
    title: string
    description: string
    detail: string
    variant: 'warning' | 'error'
}

const props = defineProps<{
    file: IUploadBatchFile
}>()

const containsText = (value: string | null | undefined, fragment: string): boolean => {
    if (!value) {
        return false
    }

    return value.toLowerCase().includes(fragment.toLowerCase())
}

const message = computed<UploadStatusMessage | null>(() => {
    const { file } = props
    const probeError = file.probeError?.trim() || ''
    const lastError = file.lastError?.trim() || ''
    const combinedErrorText = `${probeError} ${lastError}`.trim()

    if (
        file.fileKind === UPLOAD_BATCH_FILE_KIND.MEDIA &&
        containsText(probeError, 'No text-based subtitle streams')
    ) {
        return {
            title: 'Unsupported embedded subtitles',
            description:
                'This file only has image-based subtitle streams such as PGS. Upload an SRT/ASS/VTT subtitle file or media with text-based subtitles.',
            detail: probeError,
            variant: 'warning'
        }
    }

    const needsSourceLanguageOverride =
        file.fileKind === UPLOAD_BATCH_FILE_KIND.SUBTITLE &&
        file.status === UPLOAD_BATCH_FILE_STATUS.NEEDS_CONFIGURATION &&
        !file.detectedSourceLanguage &&
        !file.selectedSourceLanguage

    if (needsSourceLanguageOverride) {
        return {
            title: 'Source language needed',
            description:
                'Choose a source language override for this subtitle file before starting the batch.',
            detail: combinedErrorText,
            variant: 'warning'
        }
    }

    if (
        file.fileKind === UPLOAD_BATCH_FILE_KIND.MEDIA &&
        containsText(combinedErrorText, 'No matching source-language stream')
    ) {
        return {
            title: 'No matching text subtitle stream',
            description:
                'Select another embedded subtitle stream or adjust source language settings so a text-based stream can be used.',
            detail: combinedErrorText,
            variant: 'warning'
        }
    }

    const isFailure = file.status === UPLOAD_BATCH_FILE_STATUS.FAILED || Boolean(combinedErrorText)
    if (isFailure) {
        return {
            title: 'Translation failed',
            description: 'The file could not be processed. Review the error details and retry.',
            detail: combinedErrorText,
            variant: 'error'
        }
    }

    return null
})
</script>

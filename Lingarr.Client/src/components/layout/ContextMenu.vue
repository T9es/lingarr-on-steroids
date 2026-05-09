<template>
    <div class="relative items-center transition duration-300 ease-in-out select-none">
        <!-- Context -->
        <TooltipComponent ref="tooltip" alignment="left">
            <div ref="clickOutside" @click="toggle">
                <slot :isExtracting="isExtracting || isOcrProcessing"></slot>
            </div>
        </TooltipComponent>
        <!-- Menu -->
        <div
            v-show="isOpen"
            ref="excludeClickOutside"
            class="border-accent bg-primary absolute top-8 right-0 z-10 w-56 rounded-md border bg-clip-border shadow-lg">
            <div class="px-3 py-1" role="menu" aria-orientation="vertical">
                <!-- Embedded Options -->
                <div v-if="embeddedSubtitle" class="border-accent mb-1 border-b pb-1">
                    <span class="text-primary-content text-xs" role="menuitem">
                        {{ translate('embedded.title') }}
                    </span>
                    <div
                        v-if="embeddedSubtitle.isTextBased && !embeddedSubtitle.isExtracted"
                        class="text-primary-content flex text-sm"
                        role="menuitem"
                        @click="handleJustExtract">
                        <span class="h-full w-full cursor-pointer py-2 hover:brightness-150">
                            {{ translate('embedded.justExtract') || 'Just Extract' }}
                            <LoaderCircleIcon
                                v-if="isExtracting"
                                class="ml-2 inline h-3 w-3 animate-spin" />
                        </span>
                    </div>
                    <div
                        v-else-if="embeddedSubtitle.isTextBased"
                        class="flex cursor-pointer text-sm text-green-400"
                        role="menuitem"
                        @mouseenter="isReextractHovered = true"
                        @mouseleave="isReextractHovered = false"
                        @click="handleReExtract">
                        <span class="h-full w-full py-2 hover:brightness-150">
                            <template v-if="isExtracting">
                                {{ translate('embedded.extracting') || 'Extracting...' }}
                                <LoaderCircleIcon class="ml-2 inline h-3 w-3 animate-spin" />
                            </template>
                            <template v-else-if="isReextractHovered">
                                {{ translate('embedded.extractAgain') || 'Extract again?' }}
                            </template>
                            <template v-else>{{ translate('embedded.extracted') }} ✓</template>
                        </span>
                    </div>
                    <div
                        v-if="!embeddedSubtitle.isTextBased"
                        class="text-primary-content flex text-sm"
                        role="menuitem"
                        @click="handleOcr">
                        <span class="h-full w-full cursor-pointer py-2 hover:brightness-150">
                            {{ ocrActionLabel }}
                            <LoaderCircleIcon
                                v-if="isOcrProcessing"
                                class="ml-2 inline h-3 w-3 animate-spin" />
                        </span>
                    </div>
                    <div
                        v-if="embeddedSubtitle.ocrExtractedPath"
                        class="text-primary-content flex text-sm"
                        role="menuitem"
                        @click="handleOcrPreview">
                        <span class="h-full w-full cursor-pointer py-2 hover:brightness-150">
                            {{ translate('embedded.ocrPreview') }}
                        </span>
                    </div>
                    <div
                        v-if="embeddedSubtitle.ocrStatus === 'BlockedLowQuality'"
                        class="text-primary-content flex text-sm"
                        role="menuitem"
                        @click="handleOcrApprove">
                        <span class="h-full w-full cursor-pointer py-2 hover:brightness-150">
                            {{ translate('embedded.ocrApprove') }}
                        </span>
                    </div>
                </div>

                <span class="text-primary-content text-xs" role="menuitem">Translate to ...</span>
                <div
                    v-for="language in languages"
                    :key="language.code"
                    class="text-primary-content mb-1 flex text-sm"
                    role="menuitem"
                    @click="selectOption(language)">
                    <span class="h-full w-full cursor-pointer py-2 hover:brightness-150">
                        {{ language.name }}
                    </span>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, Ref, computed, ComputedRef } from 'vue'
import { IEpisode, ILanguage, IMovie, ISubtitle, MediaType, IEmbeddedSubtitle } from '@/ts'
import { useSettingStore } from '@/store/setting'
import { useTranslateStore } from '@/store/translate'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import useClickOutside from '@/composables/useClickOutside'
import TooltipComponent from '@/components/common/TooltipComponent.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'

const emit = defineEmits(['update:toggle'])
const props = defineProps<{
    media: IMovie | IEpisode
    subtitle?: ISubtitle
    embeddedSubtitle?: IEmbeddedSubtitle
    mediaType: MediaType
}>()
const settingsStore = useSettingStore()
const translateStore = useTranslateStore()
const { translate } = useI18n()

const tooltip = ref<InstanceType<typeof TooltipComponent> | null>(null)
const isOpen: Ref<boolean> = ref(false)
const clickOutside: Ref = ref(null)
const excludeClickOutside: Ref = ref(null)
const isExtracting = ref(false)
const isOcrProcessing = ref(false)
const isReextractHovered = ref(false)

const languages: ComputedRef<ILanguage[]> = computed(
    () => settingsStore.getSetting('target_languages') as ILanguage[]
)

const ocrActionLabel = computed(() => {
    const sub = props.embeddedSubtitle
    if (!sub?.isOcrSupported) return translate('embedded.ocrUnsupported')
    if (sub.ocrStatus === 'Queued') return translate('embedded.ocrQueued')
    if (sub.ocrStatus === 'Processing') return translate('embedded.ocrProcessing')
    if (sub.isOcrUsable) return translate('embedded.ocrReady')
    if (sub.ocrStatus === 'Failed') return translate('embedded.ocrRetry')
    if (sub.ocrStatus === 'BlockedLowQuality') return translate('embedded.ocrBlocked')
    return translate('embedded.ocrRun')
})

function toggle() {
    emit('update:toggle')
    isOpen.value = !isOpen.value
}

async function extractSubtitle(force = false): Promise<boolean> {
    const sub = props.embeddedSubtitle
    if (!sub) return false

    // If already extracted and not forcing re-extraction, return success
    if (sub.isExtracted && !force) return true

    // Don't allow extraction of image-based subtitles
    if (!sub.isTextBased) {
        alert(translate('embedded.imageBased'))
        return false
    }

    // Simple lock check if needed, but isExtracting ref should suffice for UI
    if (isExtracting.value) return false

    try {
        isExtracting.value = true
        const typeStr = (props.mediaType.toLowerCase() === 'movie' ? 'movie' : 'episode') as
            | 'movie'
            | 'episode'
        const result = await services.subtitle.extractSubtitle(
            typeStr,
            props.media.id,
            sub.streamIndex
        )

        if (result.success) {
            sub.isExtracted = true
            sub.extractedPath = result.extractedPath
            return true
        } else {
            alert(`${translate('embedded.extractFailed')}: ${result.error}`)
            return false
        }
    } catch (error) {
        console.error('Extraction failed:', error)
        alert(translate('embedded.extractFailed'))
        return false
    } finally {
        isExtracting.value = false
    }
}

async function handleJustExtract() {
    const success = await extractSubtitle()
    if (success) {
        alert(translate('embedded.extractSuccess'))
    }
    toggle()
}

async function handleReExtract() {
    const confirmed = confirm(
        translate('embedded.reextractConfirm') ||
            'Re-extract this subtitle? The existing file will be overwritten.'
    )
    if (!confirmed) return

    const success = await extractSubtitle(true) // force = true
    if (success) {
        alert(translate('embedded.extractSuccess'))
    }
}

async function handleOcr() {
    const sub = props.embeddedSubtitle
    if (!sub || sub.isTextBased || !sub.isOcrSupported || isOcrProcessing.value) return
    if (sub.ocrStatus === 'Queued' || sub.ocrStatus === 'Processing' || sub.isOcrUsable) return

    try {
        isOcrProcessing.value = true
        const typeStr = (props.mediaType.toLowerCase() === 'movie' ? 'movie' : 'episode') as
            | 'movie'
            | 'episode'
        const result = await services.subtitle.queueOcr(typeStr, props.media.id, sub.streamIndex)
        sub.ocrStatus = result.status as typeof sub.ocrStatus
        sub.ocrError = result.error
        sub.ocrCueCount = result.cueCount
        sub.ocrQualityScore = result.qualityScore
        sub.ocrIssueSummary = result.issueSummary
    } catch (error) {
        console.error('OCR queue failed:', error)
        alert(translate('embedded.ocrFailed'))
    } finally {
        isOcrProcessing.value = false
        toggle()
    }
}

async function handleOcrApprove() {
    const sub = props.embeddedSubtitle
    if (!sub) return

    try {
        const typeStr = (props.mediaType.toLowerCase() === 'movie' ? 'movie' : 'episode') as
            | 'movie'
            | 'episode'
        const result = await services.subtitle.approveOcr(typeStr, props.media.id, sub.streamIndex)
        sub.ocrStatus = result.status as typeof sub.ocrStatus
        sub.isOcrUsable = result.success
        sub.ocrError = result.error
        sub.ocrCueCount = result.cueCount
        sub.ocrQualityScore = result.qualityScore
        sub.ocrIssueSummary = result.issueSummary
    } catch (error) {
        console.error('OCR approval failed:', error)
        alert(translate('embedded.ocrApproveFailed'))
    } finally {
        toggle()
    }
}

async function handleOcrPreview() {
    const sub = props.embeddedSubtitle
    if (!sub) return

    try {
        const typeStr = (props.mediaType.toLowerCase() === 'movie' ? 'movie' : 'episode') as
            | 'movie'
            | 'episode'
        const preview = await services.subtitle.previewOcr(typeStr, props.media.id, sub.streamIndex)
        const samples = preview.lines
            .slice(0, 8)
            .map((line) => `${line.position}. ${line.text}`)
            .join('\n')
        alert(
            `${translate('embedded.ocrPreview')}\n${translate('embedded.ocrQuality')}: ${
                preview.qualityScore ?? '-'
            }\n${translate('embedded.ocrCueCount')}: ${preview.cueCount ?? '-'}\n${
                preview.issueSummary ?? ''
            }\n\n${samples}`
        )
    } catch (error) {
        console.error('OCR preview failed:', error)
        alert(translate('embedded.ocrPreviewFailed'))
    } finally {
        toggle()
    }
}

async function selectOption(target: ILanguage) {
    let subToTranslate = props.subtitle

    if (props.embeddedSubtitle) {
        if (!props.embeddedSubtitle.isTextBased && !props.embeddedSubtitle.isOcrUsable) {
            alert(translate('embedded.ocrRequired'))
            toggle()
            return
        }

        // If embedded, ensure extracted first
        if (props.embeddedSubtitle.isTextBased && !props.embeddedSubtitle.isExtracted) {
            const success = await extractSubtitle()
            if (!success) {
                toggle()
                return // Extraction failed, abort translation
            }
        }

        // Create a temporary ISubtitle for the store
        const sourcePath = props.embeddedSubtitle.isOcrUsable
            ? props.embeddedSubtitle.ocrExtractedPath
            : props.embeddedSubtitle.extractedPath
        if (sourcePath) {
            subToTranslate = {
                path: sourcePath,
                language: props.embeddedSubtitle.language || 'unknown',
                fileName:
                    props.embeddedSubtitle.title || `Stream ${props.embeddedSubtitle.streamIndex}`,
                format: props.embeddedSubtitle.isOcrUsable ? '.srt' : props.embeddedSubtitle.codecName,
                caption: props.embeddedSubtitle.title || ''
            }
        }
    }

    if (subToTranslate) {
        translateStore.translateSubtitle(
            props.media.id,
            subToTranslate,
            subToTranslate.language,
            target,
            props.mediaType
        )
        toggle()
        tooltip.value?.showTooltip()
    }
}

useClickOutside(
    clickOutside,
    () => {
        isOpen.value = false
    },
    excludeClickOutside
)
</script>

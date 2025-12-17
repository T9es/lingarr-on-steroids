<template>
    <div class="relative items-center transition duration-300 ease-in-out select-none">
        <!-- Context -->
        <TooltipComponent ref="tooltip" alignment="left">
            <div ref="clickOutside" @click="toggle">
                <slot :isExtracting="isExtracting"></slot>
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
                    <span class="text-xs" role="menuitem">{{ translate('embedded.title') }}</span>
                    <div
                        v-if="!embeddedSubtitle.isExtracted"
                        class="flex text-sm"
                        role="menuitem"
                        @click="handleJustExtract">
                        <span class="h-full w-full cursor-pointer py-2 hover:brightness-150">
                            {{ translate('embedded.justExtract') || 'Just Extract' }}
                            <LoaderCircleIcon
                                v-if="isExtracting"
                                class="ml-2 inline h-3 w-3 animate-spin" />
                        </span>
                    </div>
                    <div v-else class="flex text-sm text-green-400" role="menuitem">
                        <span class="h-full w-full py-2">
                            {{ translate('embedded.extracted') }} ✓
                        </span>
                    </div>
                </div>

                <span class="text-xs" role="menuitem">Translate to ...</span>
                <div
                    v-for="language in languages"
                    :key="language.code"
                    class="mb-1 flex text-sm"
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

const languages: ComputedRef<ILanguage[]> = computed(
    () => settingsStore.getSetting('target_languages') as ILanguage[]
)

function toggle() {
    emit('update:toggle')
    isOpen.value = !isOpen.value
}

async function extractSubtitle(): Promise<boolean> {
    const sub = props.embeddedSubtitle
    if (!sub || sub.isExtracted) return true

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

async function selectOption(target: ILanguage) {
    let subToTranslate = props.subtitle

    if (props.embeddedSubtitle) {
        // If embedded, ensure extracted first
        if (!props.embeddedSubtitle.isExtracted) {
            const success = await extractSubtitle()
            if (!success) {
                toggle()
                return // Extraction failed, abort translation
            }
        }

        // Create a temporary ISubtitle for the store
        if (props.embeddedSubtitle.extractedPath) {
            subToTranslate = {
                path: props.embeddedSubtitle.extractedPath,
                language: props.embeddedSubtitle.language || 'unknown',
                fileName:
                    props.embeddedSubtitle.title || `Stream ${props.embeddedSubtitle.streamIndex}`,
                format: props.embeddedSubtitle.codecName,
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

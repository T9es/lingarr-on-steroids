<template>
    <ModalComponent :is-open="isOpen" @close="$emit('close')">
        <template #header>
            <div class="flex items-center gap-3">
                <img
                    v-if="posterPath"
                    :src="`/api/image/${posterPath}`"
                    class="h-16 w-12 rounded object-cover"
                    @error="($event.target as HTMLImageElement).style.display = 'none'" />
                <div>
                    <h2 class="text-lg font-semibold text-primary-content">{{ title || 'Configure Test' }}</h2>
                    <p v-if="year" class="text-secondary-content text-sm">{{ year }}</p>
                </div>
            </div>
        </template>

        <div class="space-y-4">
            <div>
                <label class="mb-2 block text-sm font-medium text-primary-content">
                    Subtitle to translate
                </label>
                <select
                    v-model="selectedSubtitle"
                    class="bg-primary border-accent text-primary-content w-full rounded border px-3 py-2 text-sm">
                    <option
                        v-for="sub in availableSubtitles"
                        :key="isEmbedded(sub) ? `emb-${sub.streamIndex}` : sub.path"
                        :value="sub"
                        :disabled="isEmbedded(sub) && !sub.isTextBased">
                        {{ getSubtitleLabel(sub) }}
                        {{ isEmbedded(sub) && !sub.isTextBased ? ' (image-based, not selectable)' : '' }}
                    </option>
                </select>
                <p v-if="isEmbedded(selectedSubtitle as SubtitleOption)" class="text-accent text-xs mt-1">
                    Embedded subtitle will be extracted automatically
                </p>
            </div>

            <div>
                <label class="mb-2 block text-sm font-medium text-primary-content">
                    {{ translate('translationTest.linesToTranslate') }}
                </label>
                <div class="flex flex-wrap gap-2">
                    <button
                        @click="lineMode = 'first'"
                        :class="
                            lineMode === 'first'
                                ? 'bg-accent text-primary-content'
                                : 'bg-tertiary text-primary-content'
                        "
                        class="rounded px-3 py-2 text-sm transition">
                        {{ translate('translationTest.firstNLines') }}
                    </button>
                    <button
                        @click="lineMode = 'range'"
                        :class="
                            lineMode === 'range'
                                ? 'bg-accent text-primary-content'
                                : 'bg-tertiary text-primary-content'
                        "
                        class="rounded px-3 py-2 text-sm transition">
                        {{ translate('translationTest.linesRange') }}
                    </button>
                    <button
                        @click="lineMode = 'all'"
                        :class="
                            lineMode === 'all'
                                ? 'bg-accent text-primary-content'
                                : 'bg-tertiary text-primary-content'
                        "
                        class="rounded px-3 py-2 text-sm transition">
                        {{ translate('translationTest.allLines') }} ({{ totalLines }})
                    </button>
                </div>

                <div v-if="lineMode === 'first'" class="mt-3">
                    <input
                        v-model.number="firstN"
                        type="number"
                        min="1"
                        :max="totalLines"
                        class="bg-primary border-accent text-primary-content w-24 rounded border px-3 py-2 text-sm" />
                    <span class="text-secondary-content ml-2 text-sm">
                        {{ translate('translationTest.ofLines', { count: totalLines }) }}
                    </span>
                </div>

                <div v-if="lineMode === 'range'" class="mt-3 flex flex-wrap items-center gap-2">
                    <input
                        v-model.number="startLine"
                        type="number"
                        min="1"
                        :max="totalLines"
                        class="bg-primary border-accent text-primary-content w-20 rounded border px-3 py-2 text-sm" />
                    <span class="text-secondary-content">{{ translate('translationTest.to') }}</span>
                    <input
                        v-model.number="endLine"
                        type="number"
                        min="1"
                        :max="totalLines"
                        class="bg-primary border-accent text-primary-content w-20 rounded border px-3 py-2 text-sm" />
                    <button
                        @click="showVisualPicker = true"
                        class="bg-accent text-primary-content rounded px-3 py-2 text-sm">
                        {{ translate('translationTest.visualSelect') }}
                    </button>
                </div>
            </div>

            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label class="mb-2 block text-sm font-medium text-primary-content">
                        {{ translate('translationTest.sourceLanguage') }}
                    </label>
                    <select
                        v-model="sourceLanguage"
                        class="bg-primary border-accent text-primary-content w-full rounded border px-3 py-2 text-sm">
                        <option
                            v-for="lang in availableSourceLanguages"
                            :key="lang.code"
                            :value="lang.code">
                            {{ lang.name }}
                        </option>
                    </select>
                </div>
                <div>
                    <label class="mb-2 block text-sm font-medium text-primary-content">
                        {{ translate('translationTest.targetLanguage') }}
                    </label>
                    <select
                        v-model="targetLanguage"
                        class="bg-primary border-accent text-primary-content w-full rounded border px-3 py-2 text-sm">
                        <option
                            v-for="lang in availableTargetLanguages"
                            :key="lang.code"
                            :value="lang.code">
                            {{ lang.name }}
                        </option>
                    </select>
                </div>
            </div>
        </div>

        <template #footer>
            <button
                @click="$emit('close')"
                class="text-primary-content/60 hover:text-primary-content rounded px-4 py-2">
                {{ translate('common.cancel') }}
            </button>
            <button
                @click="startTest"
                :disabled="!canStart"
                class="bg-accent text-primary-content rounded px-4 py-2 font-medium disabled:opacity-50">
                {{ translate('translationTest.startTest') }}
            </button>
        </template>
    </ModalComponent>

    <VisualLinePickerModal
        v-if="showVisualPicker"
        :subtitle-path="currentSubtitlePath"
        :selected-start="startLine"
        :selected-end="endLine"
        :media-id="mediaId"
        :media-type="mediaType"
        :stream-index="embeddedStreamIndex"
        :language="selectedSubtitleLanguage"
        @select="onVisualSelect"
        @close="showVisualPicker = false" />
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useI18n } from '@/plugins/i18n'
import ModalComponent from '@/components/common/ModalComponent.vue'
import VisualLinePickerModal from './VisualLinePickerModal.vue'

interface Language {
    code: string
    name: string
}

interface Subtitle {
    path: string
    language?: string
    fileName?: string
}

interface EmbeddedSubtitle {
    streamIndex: number
    language?: string
    title?: string
    codecName: string
    isTextBased: boolean
    isDefault: boolean
    isForced: boolean
}

type SubtitleOption = Subtitle | EmbeddedSubtitle

const props = defineProps<{
    isOpen: boolean
    subtitlePath: string
    title?: string
    posterPath?: string
    year?: number | null
    totalLines: number
    defaultSourceLanguage?: string
    defaultTargetLanguage?: string
    availableSourceLanguages: Language[]
    availableTargetLanguages: Language[]
    availableSubtitles: SubtitleOption[]
    mediaId?: number
    mediaType?: 'Movie' | 'Episode'
}>()

const emit = defineEmits<{
    close: []
    start: [config: TestConfig]
}>()

interface TestConfig {
    subtitlePath: string
    sourceLanguage: string
    targetLanguage: string
    startLine?: number
    endLine?: number
    maxLines?: number
    embeddedStreamIndex?: number
}

const { translate } = useI18n()

const lineMode = ref<'first' | 'range' | 'all'>('first')
const firstN = ref(20)
const startLine = ref(1)
const endLine = ref(20)
const sourceLanguage = ref(props.defaultSourceLanguage || '')
const targetLanguage = ref(props.defaultTargetLanguage || '')
const showVisualPicker = ref(false)
const selectedSubtitle = ref<SubtitleOption | null>(null)

function isEmbedded(sub: SubtitleOption): sub is EmbeddedSubtitle {
    return 'streamIndex' in sub
}

function getSubtitleLabel(sub: SubtitleOption): string {
    if (isEmbedded(sub)) {
        const lang = sub.language?.toUpperCase() || 'UND'
        return `🎬 ${lang} [EMBEDDED, ${sub.codecName.toUpperCase()}]${sub.title ? ` - ${sub.title}` : ''}`
    }
    const lang = sub.language?.toUpperCase() || '??'
    return `📄 ${lang} (${sub.fileName || sub.path.split('/').pop() || 'unknown'})`
}

function getSubtitlePath(sub: SubtitleOption): string {
    if (isEmbedded(sub)) return ''
    return sub.path
}

function getEmbeddedIndex(sub: SubtitleOption): number | undefined {
    if (isEmbedded(sub)) return sub.streamIndex
    return undefined
}

watch(() => props.availableSubtitles, (subs) => {
    if (!subs || subs.length === 0) return
    
    if (!selectedSubtitle.value) {
        autoSelectSubtitle(subs, sourceLanguage.value)
    }
}, { immediate: true })

watch(sourceLanguage, (newLang) => {
    if (props.availableSubtitles?.length > 0) {
        autoSelectSubtitle(props.availableSubtitles, newLang)
    }
})

function autoSelectSubtitle(subs: SubtitleOption[], lang: string) {
    const langLower = lang.toLowerCase()
    
    const externalMatch = subs.find(s => !isEmbedded(s) && s.language?.toLowerCase() === langLower) as Subtitle | undefined
    if (externalMatch) {
        selectedSubtitle.value = externalMatch
        return
    }
    
    const embeddedMatch = subs.find(s => isEmbedded(s) && s.language?.toLowerCase() === langLower) as EmbeddedSubtitle | undefined
    if (embeddedMatch) {
        selectedSubtitle.value = embeddedMatch
        return
    }
    
    const firstExternal = subs.find(s => !isEmbedded(s)) as Subtitle | undefined
    if (firstExternal) {
        selectedSubtitle.value = firstExternal
        return
    }
    
    selectedSubtitle.value = subs[0]
}

watch(
    () => props.totalLines,
    (newTotal) => {
        firstN.value = Math.min(20, newTotal)
        endLine.value = Math.min(20, newTotal)
    },
    { immediate: true }
)

watch(
    () => props.defaultSourceLanguage,
    (newVal) => {
        if (newVal) sourceLanguage.value = newVal
    }
)

watch(
    () => props.defaultTargetLanguage,
    (newVal) => {
        if (newVal) targetLanguage.value = newVal
    }
)

watch(selectedSubtitle, async (sub) => {
    if (!sub) return
    
    if (isEmbedded(sub)) {
        return
    }
    
    const path = getSubtitlePath(sub)
    try {
        const response = await fetch(`/api/test-translation/subtitle-preview?path=${encodeURIComponent(path)}`)
        if (response.ok) {
            await response.json()
        }
    } catch {
        // Ignore errors - totalLines is passed from parent
    }
})

const canStart = computed(() => {
    return sourceLanguage.value && targetLanguage.value && 
           sourceLanguage.value !== targetLanguage.value && 
           selectedSubtitle.value !== null
})

const currentSubtitlePath = computed(() => {
    if (!selectedSubtitle.value) return ''
    return getSubtitlePath(selectedSubtitle.value)
})

const embeddedStreamIndex = computed(() => {
    if (!selectedSubtitle.value || !isEmbedded(selectedSubtitle.value)) return undefined
    return (selectedSubtitle.value as EmbeddedSubtitle).streamIndex
})

const selectedSubtitleLanguage = computed(() => {
    if (!selectedSubtitle.value) return undefined
    if (isEmbedded(selectedSubtitle.value)) {
        return (selectedSubtitle.value as EmbeddedSubtitle).language
    }
    return (selectedSubtitle.value as Subtitle).language
})

function startTest() {
    if (!selectedSubtitle.value) return
    
    const config: TestConfig = {
        subtitlePath: currentSubtitlePath.value,
        sourceLanguage: sourceLanguage.value,
        targetLanguage: targetLanguage.value,
        embeddedStreamIndex: getEmbeddedIndex(selectedSubtitle.value)
    }

    if (lineMode.value === 'first') {
        config.maxLines = firstN.value
    } else if (lineMode.value === 'range') {
        config.startLine = startLine.value
        config.endLine = endLine.value
    }

    emit('start', config)
}

function onVisualSelect(start: number, end: number) {
    startLine.value = start
    endLine.value = end
    showVisualPicker.value = false
}
</script>
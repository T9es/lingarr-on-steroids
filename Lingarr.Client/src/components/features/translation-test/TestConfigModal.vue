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
                    <h2 class="text-primary-content text-lg font-semibold">
                        {{ title || translate('translationTest.configureTest') }}
                    </h2>
                    <p v-if="year" class="text-secondary-content text-sm">{{ year }}</p>
                </div>
            </div>
        </template>

        <div class="space-y-4">
            <div>
                <label class="text-primary-content mb-2 block text-sm font-medium">
                    {{ t('translationTest.subtitleToTranslate', 'Subtitle to translate') }}
                </label>
                <select
                    v-model="selectedSubtitle"
                    class="bg-primary border-accent text-primary-content w-full rounded border px-3 py-2 text-sm">
                    <option
                        v-for="sub in effectiveSubtitles"
                        :key="isEmbedded(sub) ? `emb-${sub.streamIndex}` : sub.path"
                        :value="sub"
                        :disabled="isEmbedded(sub) && !sub.isTextBased">
                        {{ getSubtitleLabel(sub) }}
                        {{
                            isEmbedded(sub) && !sub.isTextBased
                                ? ` ${t('translationTest.imageBasedUnavailableSuffix', '(image-based, not selectable)')}`
                                : ''
                        }}
                    </option>
                </select>
                <p
                    v-if="selectedSubtitle && isEmbedded(selectedSubtitle)"
                    class="text-accent mt-1 text-xs">
                    {{
                        t(
                            'translationTest.embeddedSubtitleHint',
                            'Embedded subtitle will be extracted when needed'
                        )
                    }}
                </p>
                <p v-else-if="isLoadingSubtitleMeta" class="text-secondary-content mt-1 text-xs">
                    {{
                        t('translationTest.loadingSubtitleMetadata', 'Loading subtitle metadata...')
                    }}
                </p>
                <p v-else-if="subtitleMetaError" class="text-error mt-1 text-xs">
                    {{ subtitleMetaError }}
                </p>
                <p
                    v-else-if="currentTotalLines !== null"
                    class="text-secondary-content mt-1 text-xs">
                    {{
                        t(
                            'translationTest.availableLinesCount',
                            `${currentTotalLines} lines available`,
                            { count: currentTotalLines }
                        )
                    }}
                </p>
            </div>

            <div>
                <label class="text-primary-content mb-2 block text-sm font-medium">
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
                        @click="lineMode = 'specific'"
                        :class="
                            lineMode === 'specific'
                                ? 'bg-accent text-primary-content'
                                : 'bg-tertiary text-primary-content'
                        "
                        class="rounded px-3 py-2 text-sm transition">
                        {{ t('translationTest.specificLines', 'Specific lines') }}
                    </button>
                    <button
                        @click="lineMode = 'all'"
                        :class="
                            lineMode === 'all'
                                ? 'bg-accent text-primary-content'
                                : 'bg-tertiary text-primary-content'
                        "
                        class="rounded px-3 py-2 text-sm transition">
                        <template v-if="currentTotalLines !== null">
                            {{ translate('translationTest.allLines') }} ({{ currentTotalLines }})
                        </template>
                        <template v-else>
                            {{ translate('translationTest.allLines') }}
                        </template>
                    </button>
                </div>

                <div v-if="lineMode === 'first'" class="mt-3">
                    <input
                        v-model.number="firstN"
                        type="number"
                        min="1"
                        :max="currentTotalLines ?? undefined"
                        class="bg-primary border-accent text-primary-content w-24 rounded border px-3 py-2 text-sm" />
                    <span class="text-secondary-content ml-2 text-sm">
                        <template v-if="currentTotalLines !== null">
                            {{ translate('translationTest.ofLines', { count: currentTotalLines }) }}
                        </template>
                        <template v-else>
                            {{ t('translationTest.firstLinesFallback', 'lines') }}
                        </template>
                    </span>
                </div>

                <div v-if="lineMode === 'specific'" class="mt-3 flex flex-wrap items-center gap-2">
                    <button
                        @click="showVisualPicker = true"
                        :disabled="!canOpenVisualPicker"
                        class="bg-accent text-primary-content rounded px-3 py-2 text-sm disabled:opacity-50">
                        {{ translate('translationTest.visualSelect') }}
                    </button>
                    <span class="text-secondary-content text-sm">
                        {{
                            t(
                                'translationTest.selectedLinesSummary',
                                `${selectedLinePositions.length} lines selected`,
                                { count: selectedLinePositions.length }
                            )
                        }}
                    </span>
                </div>
            </div>

            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label class="text-primary-content mb-2 block text-sm font-medium">
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
                    <label class="text-primary-content mb-2 block text-sm font-medium">
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
        v-if="showVisualPicker && selectedSubtitle"
        :subtitle-path="currentSubtitlePath"
        :selected-positions="selectedLinePositions"
        :media-id="mediaId"
        :media-type="mediaType"
        :stream-index="embeddedStreamIndex"
        :language="selectedSubtitleLanguage"
        @select="onVisualSelect"
        @close="showVisualPicker = false" />
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
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
    maxLines?: number
    embeddedStreamIndex?: number
    selectedLinePositions?: number[]
}

const { translate } = useI18n()

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}

const lineMode = ref<'first' | 'specific' | 'all'>('first')
const firstN = ref(20)
const sourceLanguage = ref(props.defaultSourceLanguage || '')
const targetLanguage = ref(props.defaultTargetLanguage || '')
const showVisualPicker = ref(false)
const selectedSubtitle = ref<SubtitleOption | null>(null)
const selectedLinePositions = ref<number[]>([])
const currentTotalLines = ref<number | null>(null)
const isLoadingSubtitleMeta = ref(false)
const subtitleMetaError = ref<string | null>(null)

const effectiveSubtitles = computed<SubtitleOption[]>(() => {
    if (props.availableSubtitles.length > 0) {
        return props.availableSubtitles
    }

    if (props.subtitlePath) {
        return [
            {
                path: props.subtitlePath
            }
        ]
    }

    return []
})

function isEmbedded(sub: SubtitleOption): sub is EmbeddedSubtitle {
    return 'streamIndex' in sub
}

function getSubtitleLabel(sub: SubtitleOption): string {
    if (isEmbedded(sub)) {
        const lang = sub.language?.toUpperCase() || 'UND'
        return `[EMB] ${lang} [${sub.codecName.toUpperCase()}]${sub.title ? ` - ${sub.title}` : ''}`
    }

    const lang = sub.language?.toUpperCase() || '??'
    return `[FILE] ${lang} (${sub.fileName || sub.path.split('/').pop() || 'unknown'})`
}

function getSubtitlePath(sub: SubtitleOption): string {
    if (isEmbedded(sub)) {
        return ''
    }

    return sub.path
}

function getEmbeddedIndex(sub: SubtitleOption): number | undefined {
    if (isEmbedded(sub)) {
        return sub.streamIndex
    }

    return undefined
}

function autoSelectSubtitle(subs: SubtitleOption[], lang: string) {
    if (subs.length === 0) {
        selectedSubtitle.value = null
        return
    }

    const normalizedLanguage = lang.toLowerCase()

    const externalMatch = subs.find(
        (sub) => !isEmbedded(sub) && sub.language?.toLowerCase() === normalizedLanguage
    )

    if (externalMatch) {
        selectedSubtitle.value = externalMatch
        return
    }

    const embeddedMatch = subs.find(
        (sub) =>
            isEmbedded(sub) && sub.isTextBased && sub.language?.toLowerCase() === normalizedLanguage
    )

    if (embeddedMatch) {
        selectedSubtitle.value = embeddedMatch
        return
    }

    const firstExternal = subs.find((sub) => !isEmbedded(sub))

    if (firstExternal) {
        selectedSubtitle.value = firstExternal
        return
    }

    selectedSubtitle.value = subs.find((sub) => !isEmbedded(sub) || sub.isTextBased) || subs[0]
}

async function loadSubtitleMetadata(sub: SubtitleOption | null) {
    currentTotalLines.value = null
    subtitleMetaError.value = null

    if (!sub || isEmbedded(sub)) {
        return
    }

    try {
        isLoadingSubtitleMeta.value = true

        const response = await fetch(
            `/api/test-translation/subtitle-preview?path=${encodeURIComponent(sub.path)}`
        )

        if (!response.ok) {
            throw new Error('Failed to load subtitle')
        }

        const data = await response.json()
        currentTotalLines.value = data.totalLines ?? null

        if (currentTotalLines.value !== null) {
            firstN.value = Math.min(Math.max(firstN.value, 1), currentTotalLines.value)
        }
    } catch (error) {
        console.error('Failed to load subtitle metadata:', error)
        subtitleMetaError.value =
            error instanceof Error ? error.message : 'Failed to load subtitle metadata'
    } finally {
        isLoadingSubtitleMeta.value = false
    }
}

watch(
    () => effectiveSubtitles.value,
    (subs) => {
        if (!selectedSubtitle.value) {
            autoSelectSubtitle(subs, sourceLanguage.value)
        }
    },
    { immediate: true }
)

watch(sourceLanguage, (newLanguage) => {
    autoSelectSubtitle(effectiveSubtitles.value, newLanguage)
})

watch(
    () => props.defaultSourceLanguage,
    (value) => {
        if (value) {
            sourceLanguage.value = value
        }
    }
)

watch(
    () => props.defaultTargetLanguage,
    (value) => {
        if (value) {
            targetLanguage.value = value
        }
    }
)

watch(
    selectedSubtitle,
    async (sub) => {
        selectedLinePositions.value = []
        await loadSubtitleMetadata(sub)
    },
    { immediate: true }
)

const canOpenVisualPicker = computed(() => {
    if (!selectedSubtitle.value) {
        return false
    }

    if (isEmbedded(selectedSubtitle.value)) {
        return props.mediaId !== undefined && props.mediaType !== undefined
    }

    return currentSubtitlePath.value.length > 0
})

const canStart = computed(() => {
    if (
        !sourceLanguage.value ||
        !targetLanguage.value ||
        sourceLanguage.value === targetLanguage.value
    ) {
        return false
    }

    if (!selectedSubtitle.value) {
        return false
    }

    if (lineMode.value === 'specific' && selectedLinePositions.value.length === 0) {
        return false
    }

    if (lineMode.value === 'first' && firstN.value < 1) {
        return false
    }

    return true
})

const currentSubtitlePath = computed(() => {
    if (!selectedSubtitle.value) {
        return ''
    }

    return getSubtitlePath(selectedSubtitle.value)
})

const embeddedStreamIndex = computed(() => {
    if (!selectedSubtitle.value) {
        return undefined
    }

    return getEmbeddedIndex(selectedSubtitle.value)
})

const selectedSubtitleLanguage = computed(() => {
    if (!selectedSubtitle.value) {
        return undefined
    }

    return selectedSubtitle.value.language
})

function startTest() {
    if (!selectedSubtitle.value) {
        return
    }

    const config: TestConfig = {
        subtitlePath: currentSubtitlePath.value,
        sourceLanguage: sourceLanguage.value,
        targetLanguage: targetLanguage.value,
        embeddedStreamIndex: embeddedStreamIndex.value
    }

    if (lineMode.value === 'first') {
        config.maxLines = Math.max(1, firstN.value)
    } else if (lineMode.value === 'specific') {
        config.selectedLinePositions = [...selectedLinePositions.value]
    }

    emit('start', config)
}

function onVisualSelect(positions: number[]) {
    selectedLinePositions.value = positions
    showVisualPicker.value = false
}
</script>

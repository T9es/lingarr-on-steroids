<template>
    <Teleport to="body">
        <Transition
            enter-active-class="transition-all duration-300 ease-in-out"
            leave-active-class="transition-all duration-300 ease-in-out"
            enter-from-class="opacity-0"
            leave-to-class="opacity-0">
            <div
                v-if="isOpen"
                class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
                @click="$emit('close')">
                <Transition
                    enter-active-class="transition-transform duration-300 ease-in-out"
                    leave-active-class="transition-transform duration-300 ease-in-out"
                    enter-from-class="scale-90"
                    leave-to-class="scale-90">
                    <div
                        v-if="isOpen"
                        class="bg-primary border-secondary flex w-full max-w-lg flex-col rounded-lg border p-4 shadow-xl"
                        @click.stop>
                        <!-- Header -->
                        <div class="mb-4 flex items-center justify-between">
                            <h3 class="text-lg font-semibold">
                                {{ translate('subtitleSelector.title') }}
                            </h3>
                            <button
                                class="text-primary-content/60 hover:text-primary-content"
                                @click="$emit('close')">
                                <svg
                                    class="h-6 w-6"
                                    fill="none"
                                    stroke="currentColor"
                                    viewBox="0 0 24 24">
                                    <path
                                        stroke-linecap="round"
                                        stroke-linejoin="round"
                                        stroke-width="2"
                                        d="M6 18L18 6M6 6l12 12" />
                                </svg>
                            </button>
                        </div>

                        <!-- Media Info -->
                        <div class="bg-secondary/50 mb-4 rounded p-3">
                            <div class="font-medium">{{ mediaTitle }}</div>
                            <div class="text-primary-content/70 text-sm">
                                {{ mediaType }} #{{ mediaId }}
                            </div>
                        </div>

                        <!-- Loading State -->
                        <div v-if="isLoading" class="flex items-center justify-center py-8">
                            <svg
                                class="text-accent h-8 w-8 animate-spin"
                                xmlns="http://www.w3.org/2000/svg"
                                fill="none"
                                viewBox="0 0 24 24">
                                <circle
                                    class="opacity-25"
                                    cx="12"
                                    cy="12"
                                    r="10"
                                    stroke="currentColor"
                                    stroke-width="4"></circle>
                                <path
                                    class="opacity-75"
                                    fill="currentColor"
                                    d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                            </svg>
                            <span class="ml-2">
                                {{ translate('subtitleSelector.loadingSubtitles') }}
                            </span>
                        </div>

                        <!-- Error State -->
                        <div
                            v-else-if="error"
                            class="rounded border border-red-500/30 bg-red-500/10 p-4 text-red-400">
                            {{ error }}
                        </div>

                        <!-- Empty State -->
                        <div
                            v-else-if="subtitles.length === 0"
                            class="text-primary-content/70 py-8 text-center">
                            No embedded subtitles found for this media.
                        </div>

                        <!-- Subtitle List -->
                        <div v-else class="max-h-96 overflow-y-auto">
                            <div class="text-primary-content/70 mb-2 text-sm">
                                {{ translate('subtitleSelector.selectSubtitle') }}
                            </div>
                            <div class="space-y-2">
                                <div
                                    v-for="subtitle in subtitles"
                                    :key="subtitle.streamIndex"
                                    class="cursor-pointer rounded border p-3 transition-colors"
                                    :class="{
                                        'border-accent bg-accent/10':
                                            selectedStreamIndex === subtitle.streamIndex,
                                        'border-secondary/30 hover:border-accent':
                                            selectedStreamIndex !== subtitle.streamIndex,
                                        'opacity-50': !subtitle.isTextBased
                                    }"
                                    @click="selectSubtitle(subtitle)">
                                    <div class="flex items-start justify-between">
                                        <div class="flex-1">
                                            <div class="flex items-center gap-2">
                                                <!-- Language Flag/Icon -->
                                                <span class="text-lg">
                                                    {{ getLanguageEmoji(subtitle.language) }}
                                                </span>
                                                <span class="font-medium">
                                                    {{ getLanguageName(subtitle.language) }}
                                                </span>
                                                <!-- Title Badge -->
                                                <span
                                                    v-if="subtitle.title"
                                                    class="bg-secondary rounded px-2 py-0.5 text-xs">
                                                    {{ subtitle.title }}
                                                </span>
                                                <!-- Default Badge -->
                                                <span
                                                    v-if="subtitle.isDefault"
                                                    class="rounded bg-blue-500/20 px-2 py-0.5 text-xs text-blue-400">
                                                    Default
                                                </span>
                                            </div>
                                            <div
                                                class="text-primary-content/70 mt-1 flex items-center gap-2 text-sm">
                                                <span>
                                                    {{ translate('subtitleSelector.stream') }}
                                                    {{ subtitle.streamIndex }}
                                                </span>
                                                <span>•</span>
                                                <span
                                                    :class="
                                                        subtitle.isTextBased
                                                            ? 'text-green-400'
                                                            : 'text-red-400'
                                                    ">
                                                    {{ subtitle.codecName }}
                                                </span>
                                                <!-- Entry Count -->
                                                <template
                                                    v-if="
                                                        subtitle.entryCount !== null &&
                                                        subtitle.entryCount !== undefined
                                                    ">
                                                    <span>•</span>
                                                    <span
                                                        :class="
                                                            subtitle.isSparse
                                                                ? 'text-yellow-400'
                                                                : 'text-green-400'
                                                        ">
                                                        {{ subtitle.entryCount }} entries
                                                    </span>
                                                </template>
                                            </div>
                                        </div>
                                        <!-- Warning Icon for Forced/Signs -->
                                        <div
                                            v-if="subtitle.isForced || subtitle.isSparse"
                                            class="ml-2">
                                            <svg
                                                class="h-5 w-5 text-yellow-500"
                                                fill="none"
                                                stroke="currentColor"
                                                viewBox="0 0 24 24"
                                                title="This subtitle may be incomplete (Forced/Signs-only)">
                                                <path
                                                    stroke-linecap="round"
                                                    stroke-linejoin="round"
                                                    stroke-width="2"
                                                    d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                            </svg>
                                        </div>
                                    </div>
                                    <!-- Warning Text -->
                                    <div
                                        v-if="subtitle.isForced"
                                        class="mt-1 text-xs text-yellow-500">
                                        ⚠️ Forced subtitle - may only contain foreign language
                                        segments
                                    </div>
                                    <div
                                        v-else-if="subtitle.isSparse"
                                        class="mt-1 text-xs text-yellow-500">
                                        ⚠️ Low entry count - may be Signs/Songs only
                                    </div>
                                    <div
                                        v-else-if="!subtitle.isTextBased"
                                        class="mt-1 text-xs text-red-400">
                                        ❌ Image-based subtitle - cannot be translated
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Footer -->
                        <div
                            class="border-secondary/30 mt-4 flex items-center justify-between border-t pt-4">
                            <div class="text-primary-content/70 text-sm">
                                <span v-if="selectedStreamIndex !== null">
                                    Selected: Stream {{ selectedStreamIndex }}
                                </span>
                                <span v-else>Select a subtitle</span>
                            </div>
                            <div class="flex gap-2">
                                <button
                                    class="bg-secondary hover:bg-secondary/80 rounded px-4 py-2 text-sm font-medium"
                                    @click="$emit('close')">
                                    Cancel
                                </button>
                                <button
                                    :disabled="
                                        selectedStreamIndex === null ||
                                        isQueuing ||
                                        !selectedSubtitle?.isTextBased
                                    "
                                    class="bg-accent hover:bg-accent/80 text-primary-content disabled:bg-secondary/50 rounded px-4 py-2 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-50"
                                    @click="extractAndTranslate">
                                    <span v-if="isQueuing" class="flex items-center">
                                        <svg
                                            class="mr-2 h-4 w-4 animate-spin"
                                            xmlns="http://www.w3.org/2000/svg"
                                            fill="none"
                                            viewBox="0 0 24 24">
                                            <circle
                                                class="opacity-25"
                                                cx="12"
                                                cy="12"
                                                r="10"
                                                stroke="currentColor"
                                                stroke-width="4"></circle>
                                            <path
                                                class="opacity-75"
                                                fill="currentColor"
                                                d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                        </svg>
                                        Queuing...
                                    </span>
                                    <span v-else>Extract & Translate</span>
                                </button>
                            </div>
                        </div>
                    </div>
                </Transition>
            </div>
        </Transition>
    </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import axios from 'axios'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

interface AvailableSubtitle {
    id: number
    streamIndex: number
    language: string | null
    title: string | null
    codecName: string
    isTextBased: boolean
    isDefault: boolean
    isForced: boolean
    isExtracted: boolean
    extractedPath: string | null
    entryCount: number | null
    isSparse: boolean | null
}

interface ErrorResponse {
    error?: string
    message?: string
}

const props = defineProps<{
    isOpen: boolean
    mediaId: number
    mediaType: string
    mediaTitle: string
    sourceLanguage: string
}>()

const emit = defineEmits<{
    (e: 'close'): void
    (e: 'success', message: string): void
    (e: 'error', message: string): void
}>()

const subtitles = ref<AvailableSubtitle[]>([])
const isLoading = ref(false)
const isQueuing = ref(false)
const error = ref('')
const selectedStreamIndex = ref<number | null>(null)

const selectedSubtitle = computed(() => {
    return subtitles.value.find((s) => s.streamIndex === selectedStreamIndex.value) || null
})

// Language code to emoji mapping
const languageEmojis: Record<string, string> = {
    eng: '🇬🇧',
    en: '🇬🇧',
    jpn: '🇯🇵',
    ja: '🇯🇵',
    fra: '🇫🇷',
    fr: '🇫🇷',
    deu: '🇩🇪',
    de: '🇩🇪',
    spa: '🇪🇸',
    es: '🇪🇸',
    ita: '🇮🇹',
    it: '🇮🇹',
    por: '🇵🇹',
    pt: '🇵🇹',
    rus: '🇷🇺',
    ru: '🇷🇺',
    kor: '🇰🇷',
    ko: '🇰🇷',
    cmn: '🇨🇳',
    zh: '🇨🇳',
    chi: '🇨🇳',
    nld: '🇳🇱',
    nl: '🇳🇱',
    pol: '🇵🇱',
    pl: '🇵🇱',
    tur: '🇹🇷',
    tr: '🇹🇷',
    swe: '🇸🇪',
    sv: '🇸🇪',
    nor: '🇳🇴',
    no: '🇳🇴',
    dan: '🇩🇰',
    da: '🇩🇰',
    fin: '🇫🇮',
    fi: '🇫🇮',
    gre: '🇬🇷',
    el: '🇬🇷',
    hun: '🇭🇺',
    hu: '🇭🇺',
    cze: '🇨🇿',
    cs: '🇨🇿',
    slk: '🇸🇰',
    sk: '🇸🇰',
    ron: '🇷🇴',
    ro: '🇷🇴',
    hrv: '🇭🇷',
    hr: '🇭🇷',
    srp: '🇷🇸',
    sr: '🇷🇸',
    ukr: '🇺🇦',
    uk: '🇺🇦',
    ara: '🇸🇦',
    ar: '🇸🇦',
    heb: '🇮🇱',
    he: '🇮🇱',
    hin: '🇮🇳',
    hi: '🇮🇳',
    tha: '🇹🇭',
    th: '🇹🇭',
    vie: '🇻🇳',
    vi: '🇻🇳',
    ind: '🇮🇩',
    id: '🇮🇩',
    msa: '🇲🇾',
    ms: '🇲🇾',
    und: '🌐'
}

const languageNames: Record<string, string> = {
    eng: 'English',
    en: 'English',
    jpn: 'Japanese',
    ja: 'Japanese',
    fra: 'French',
    fr: 'French',
    deu: 'German',
    de: 'German',
    spa: 'Spanish',
    es: 'Spanish',
    ita: 'Italian',
    it: 'Italian',
    por: 'Portuguese',
    pt: 'Portuguese',
    rus: 'Russian',
    ru: 'Russian',
    kor: 'Korean',
    ko: 'Korean',
    cmn: 'Chinese',
    zh: 'Chinese',
    chi: 'Chinese',
    nld: 'Dutch',
    nl: 'Dutch',
    pol: 'Polish',
    pl: 'Polish',
    tur: 'Turkish',
    tr: 'Turkish',
    swe: 'Swedish',
    sv: 'Swedish',
    nor: 'Norwegian',
    no: 'Norwegian',
    dan: 'Danish',
    da: 'Danish',
    fin: 'Finnish',
    fi: 'Finnish',
    gre: 'Greek',
    el: 'Greek',
    hun: 'Hungarian',
    hu: 'Hungarian',
    cze: 'Czech',
    cs: 'Czech',
    slk: 'Slovak',
    sk: 'Slovak',
    ron: 'Romanian',
    ro: 'Romanian',
    hrv: 'Croatian',
    hr: 'Croatian',
    srp: 'Serbian',
    sr: 'Serbian',
    ukr: 'Ukrainian',
    uk: 'Ukrainian',
    ara: 'Arabic',
    ar: 'Arabic',
    heb: 'Hebrew',
    he: 'Hebrew',
    hin: 'Hindi',
    hi: 'Hindi',
    tha: 'Thai',
    th: 'Thai',
    vie: 'Vietnamese',
    vi: 'Vietnamese',
    ind: 'Indonesian',
    id: 'Indonesian',
    msa: 'Malay',
    ms: 'Malay',
    und: 'Undefined'
}

const getLanguageEmoji = (lang: string | null): string => {
    if (!lang) return '🌐'
    const normalized = lang.toLowerCase()
    return languageEmojis[normalized] || '🌐'
}

const getLanguageName = (lang: string | null): string => {
    if (!lang) return 'Unknown'
    const normalized = lang.toLowerCase()
    return languageNames[normalized] || lang.toUpperCase()
}

const selectSubtitle = (subtitle: AvailableSubtitle) => {
    if (!subtitle.isTextBased) return
    selectedStreamIndex.value = subtitle.streamIndex
}

const getErrorResponse = (error: unknown): ErrorResponse | undefined => {
    if (axios.isAxiosError<ErrorResponse>(error)) {
        return error.response?.data
    }

    return undefined
}

const fetchSubtitles = async () => {
    if (!props.isOpen || !props.mediaId) return

    isLoading.value = true
    error.value = ''
    subtitles.value = []
    selectedStreamIndex.value = null

    try {
        const type = props.mediaType.toLowerCase() === 'movie' ? 'movie' : 'episode'
        const response = await axios.get(`/api/subtitle/available/${type}/${props.mediaId}`)
        subtitles.value = response.data

        // Auto-select the first text-based subtitle
        const firstTextBased = subtitles.value.find((s) => s.isTextBased)
        if (firstTextBased) {
            selectedStreamIndex.value = firstTextBased.streamIndex
        }
    } catch (err: unknown) {
        error.value = getErrorResponse(err)?.error || 'Failed to load subtitles'
        console.error('Failed to fetch subtitles:', err)
    } finally {
        isLoading.value = false
    }
}

const extractAndTranslate = async () => {
    if (selectedStreamIndex.value === null || !selectedSubtitle.value?.isTextBased) return

    isQueuing.value = true
    try {
        const response = await axios.post('/api/translate/queue-with-subtitle', {
            mediaId: props.mediaId,
            mediaType: props.mediaType,
            streamIndex: selectedStreamIndex.value,
            sourceLanguage: props.sourceLanguage
        })

        if (response.data.success) {
            emit('success', response.data.message)
            emit('close')
        } else {
            emit('error', response.data.message)
        }
    } catch (err: unknown) {
        const message = getErrorResponse(err)?.message || 'Failed to queue translation'
        emit('error', message)
        console.error('Failed to queue translation:', err)
    } finally {
        isQueuing.value = false
    }
}

watch(
    () => props.isOpen,
    (newValue) => {
        if (newValue) {
            fetchSubtitles()
        }
    }
)
</script>

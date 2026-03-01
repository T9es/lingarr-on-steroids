<template>
    <div class="bg-tertiary text-tertiary-content w-full">
        <div class="border-primary grid grid-cols-14 border-b-2 font-bold">
            <div class="col-span-1 px-4 py-2">
                <span class="hidden lg:block">
                    {{ translate('tvShows.episode') }}
                </span>
                <span class="block lg:hidden">#</span>
            </div>
            <div class="col-span-4 px-4 py-2 md:col-span-3">
                {{ translate('tvShows.episodeTitle') }}
            </div>
            <div
                class="col-span-1 flex items-center justify-center py-2"
                title="Translation Status">
                📊
            </div>
            <div class="col-span-4 py-2 pr-4 md:col-span-4">
                <span>{{ translate('tvShows.episodeSubtitles') }}</span>
            </div>
            <div class="col-span-1 py-2 text-center md:col-span-1">
                <span class="hidden md:block">
                    {{ translate('tvShows.translateNow') }}
                </span>
                <span class="block md:hidden">⚡</span>
            </div>
            <div class="col-span-1 py-2 text-center md:col-span-1">
                <span class="hidden md:block">
                    {{ translate('tvShows.integrityCheck') }}
                </span>
                <span class="block md:hidden">✔</span>
            </div>
            <div class="col-span-1 py-2 pr-4 text-right">
                <span class="hidden md:block">
                    {{ translate('tvShows.exclude') }}
                </span>
                <span class="block md:hidden">⊘</span>
            </div>
        </div>
        <div v-for="episode in episodes" :key="episode.id" class="grid grid-cols-14">
            <div class="col-span-1 px-4 py-2">
                {{ episode.episodeNumber }}
            </div>
            <div class="col-span-4 px-4 py-2 md:col-span-3">
                {{ episode.title }}
            </div>
            <div class="col-span-1 flex items-center justify-center py-2">
                <TranslationStateBadge
                    :state="episode.translationState ?? TRANSLATION_STATE.UNKNOWN" />
            </div>
            <div class="col-span-4 pr-4 md:col-span-4">
                <div v-if="episode?.fileName" class="flex flex-wrap items-center gap-2 py-2">
                    <template
                        v-for="item in getAllSubtitles(episode).slice(
                            0,
                            isSubtitlesExpanded(episode.id) ? undefined : MAX_VISIBLE_SUBTITLES
                        )"
                        :key="item.key">
                        <ContextMenu
                            v-if="item.type === 'external'"
                            :media-type="MEDIA_TYPE.EPISODE"
                            :media="episode"
                            :subtitle="item.data as ISubtitle">
                            <BadgeComponent>
                                {{ (item.data as ISubtitle).language.toUpperCase() }}
                                <span
                                    v-if="(item.data as ISubtitle).caption"
                                    class="text-primary-content/50">
                                    - {{ (item.data as ISubtitle).caption.toUpperCase() }}
                                </span>
                            </BadgeComponent>
                        </ContextMenu>
                        <ContextMenu
                            v-else
                            :embeddedSubtitle="item.data as IEmbeddedSubtitle"
                            :media="episode"
                            :media-type="MEDIA_TYPE.EPISODE"
                            v-slot="{ isExtracting }">
                            <BadgeComponent
                                :classes="getEmbeddedBadgeClasses(item.data as IEmbeddedSubtitle)">
                                <span class="mr-1">📦</span>
                                {{ formatEmbeddedLanguage(item.data as IEmbeddedSubtitle) }}
                                <span
                                    v-if="(item.data as IEmbeddedSubtitle).title"
                                    class="ml-1 text-amber-200/70">
                                    ({{ truncate((item.data as IEmbeddedSubtitle).title, 10) }})
                                </span>
                                <span
                                    v-if="(item.data as IEmbeddedSubtitle).isForced"
                                    class="ml-1 text-xs opacity-70">
                                    F
                                </span>
                                <span
                                    v-if="(item.data as IEmbeddedSubtitle).isDefault"
                                    class="ml-1 text-xs opacity-70">
                                    D
                                </span>
                                <LoaderCircleIcon
                                    v-if="isExtracting"
                                    class="ml-1 h-3 w-3 animate-spin" />
                            </BadgeComponent>
                        </ContextMenu>
                    </template>
                    <button
                        v-if="getAllSubtitles(episode).length > MAX_VISIBLE_SUBTITLES"
                        class="border-accent text-secondary-content hover:bg-accent/20 cursor-pointer rounded-full border px-3 py-1 text-xs font-semibold"
                        @click="toggleSubtitles(episode.id)">
                        {{
                            isSubtitlesExpanded(episode.id)
                                ? 'Show less'
                                : `+${getAllSubtitles(episode).length - MAX_VISIBLE_SUBTITLES} more`
                        }}
                    </button>
                </div>
            </div>
            <div class="col-span-1 flex items-center justify-center py-2 md:col-span-1">
                <button
                    class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                    :disabled="translatingEpisode[episode.id]"
                    :title="translate('tvShows.translateNow')"
                    @click="translateEpisode(episode)">
                    <LoaderCircleIcon
                        v-if="translatingEpisode[episode.id]"
                        class="h-4 w-4 animate-spin" />
                    <LanguageIcon v-else class="h-4 w-4" />
                </button>
            </div>
            <div class="col-span-1 flex items-center justify-center py-2 md:col-span-1">
                <button
                    class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                    :disabled="integrityCheckingEpisode[episode.id]"
                    :title="translate('tvShows.integrityCheck')"
                    @click="checkIntegrityEpisode(episode)">
                    <LoaderCircleIcon
                        v-if="integrityCheckingEpisode[episode.id]"
                        class="h-4 w-4 animate-spin" />
                    <CheckMarkCicleIcon v-else class="h-4 w-4" />
                </button>
            </div>
            <div class="col-span-1 flex items-center justify-end px-1 py-2 pr-4">
                <ToggleButton
                    v-model="episode.excludeFromTranslation"
                    size="small"
                    @toggle:update="() => showStore.exclude(MEDIA_TYPE.EPISODE, episode.id)" />
            </div>
        </div>
    </div>
</template>
<script setup lang="ts">
import { reactive, onMounted, ref } from 'vue'
import { IEpisode, ISubtitle, IEmbeddedSubtitle, MEDIA_TYPE, TRANSLATION_STATE } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import ContextMenu from '@/components/layout/ContextMenu.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import LanguageIcon from '@/components/icons/LanguageIcon.vue'
import CheckMarkCicleIcon from '@/components/icons/CheckMarkCicleIcon.vue'
import TranslationStateBadge from '@/components/common/TranslationStateBadge.vue'
import { useShowStore } from '@/store/show'
import services from '@/services'

const { translate } = useI18n()

const props = defineProps<{
    episodes: IEpisode[]
    subtitles: ISubtitle[]
}>()
const showStore = useShowStore()

// Track which episodes are currently being translated
const translatingEpisode = reactive<Record<number, boolean>>({})
const integrityCheckingEpisode = reactive<Record<number, boolean>>({})

// Subtitle collapse state
const expandedSubtitles = ref<Set<number>>(new Set())
const MAX_VISIBLE_SUBTITLES = 4

interface TranslateMediaResponse {
    translationsQueued: number
    message: string
}

const translateEpisode = async (episode: IEpisode) => {
    translatingEpisode[episode.id] = true
    try {
        const response = await services.translate.translateMedia<TranslateMediaResponse>(
            episode.id,
            MEDIA_TYPE.EPISODE
        )
        console.log(response.message)
    } catch (error) {
        console.error('Failed to translate episode:', error)
    } finally {
        translatingEpisode[episode.id] = false
    }
}

const checkIntegrityEpisode = async (episode: IEpisode) => {
    integrityCheckingEpisode[episode.id] = true
    try {
        const count = await services.media.integrityCheck<number>(MEDIA_TYPE.EPISODE, episode.id)
        if (count > 0) {
            console.log(`Integrity check failed. Queued ${count} repair translations.`)
        } else {
            console.log('Integrity check passed or no repairs needed.')
        }
    } catch (error) {
        console.error('Failed to check integrity for episode:', error)
    } finally {
        integrityCheckingEpisode[episode.id] = false
    }
}

// Fetch embedded subtitles for episodes on mount
onMounted(async () => {
    for (const episode of props.episodes) {
        if (!episode.embeddedSubtitles || episode.embeddedSubtitles.length === 0) {
            try {
                episode.embeddedSubtitles = await services.subtitle.getEmbeddedSubtitles<
                    IEmbeddedSubtitle[]
                >('episode', episode.id)
            } catch (error) {
                console.error(
                    `Failed to fetch embedded subtitles for episode ${episode.id}:`,
                    error
                )
            }
        }
    }
})

const getSubtitle = (fileName: string | null) => {
    if (!fileName) return null
    return props.subtitles
        .filter(
            (subtitle: ISubtitle) =>
                subtitle.fileName.toLocaleLowerCase().includes(fileName.toLocaleLowerCase()) &&
                subtitle.language &&
                subtitle.language.trim() !== ''
        )
        .slice()
        .sort((a, b) => a.language.localeCompare(b.language))
}

const getEmbeddedSubtitles = (episode: IEpisode): IEmbeddedSubtitle[] => {
    if (!episode.embeddedSubtitles) return []

    // Get external subtitle languages for deduplication
    const externalLanguages = new Set(
        (getSubtitle(episode.fileName ?? null) || []).map((s) => s.language?.toLowerCase())
    )

    // Filter out embedded subs that have already been extracted AND have a matching external subtitle
    return episode.embeddedSubtitles.filter((embSub) => {
        // Always show if not extracted
        if (!embSub.isExtracted) return true
        // If extracted, hide if an external subtitle with matching language exists
        const lang = embSub.language?.toLowerCase()
        return !lang || !externalLanguages.has(lang)
    })
}

const formatEmbeddedLanguage = (sub: IEmbeddedSubtitle): string => {
    if (sub.language) {
        return sub.language.toUpperCase()
    }
    return `#${sub.streamIndex}`
}

const truncate = (str: string | null | undefined, len: number): string => {
    if (!str) return ''
    return str.length > len ? str.substring(0, len) + '...' : str
}

const getEmbeddedBadgeClasses = (sub: IEmbeddedSubtitle): string => {
    if (!sub.isTextBased) {
        // Image-based (PGS/VobSub) - gray, non-clickable
        return 'cursor-not-allowed text-gray-400 border-gray-500 bg-gray-700/50 opacity-60'
    }
    if (sub.isExtracted) {
        // Extracted - green tint
        return 'cursor-pointer text-green-300 border-green-500 bg-green-900/30'
    }
    // Text-based, not extracted - amber
    return 'cursor-pointer text-amber-300 border-amber-500 bg-amber-900/30'
}

const toggleSubtitles = (episodeId: number) => {
    if (expandedSubtitles.value.has(episodeId)) {
        expandedSubtitles.value.delete(episodeId)
    } else {
        expandedSubtitles.value.add(episodeId)
    }
}

const isSubtitlesExpanded = (episodeId: number): boolean => {
    return expandedSubtitles.value.has(episodeId)
}

interface SubtitleItem {
    type: 'external' | 'embedded'
    data: ISubtitle | IEmbeddedSubtitle
    key: string
}

const getAllSubtitles = (episode: IEpisode): SubtitleItem[] => {
    const items: SubtitleItem[] = []
    const fileName = episode.fileName ?? null
    const externalSubs: ISubtitle[] = getSubtitle(fileName) ?? []
    const embeddedSubs = getEmbeddedSubtitles(episode)

    externalSubs.forEach((sub, index) => {
        items.push({
            type: 'external',
            data: sub,
            key: `ext-${episode.id}-${index}`
        })
    })

    embeddedSubs.forEach((sub) => {
        items.push({
            type: 'embedded',
            data: sub,
            key: `emb-${episode.id}-${sub.id}`
        })
    })

    return items
}
</script>

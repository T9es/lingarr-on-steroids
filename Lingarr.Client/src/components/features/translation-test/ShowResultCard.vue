<template>
    <div class="bg-tertiary overflow-hidden rounded-lg">
        <!-- Show header -->
        <div class="hover:bg-tertiary/80 cursor-pointer p-4 transition" @click="toggleShow">
            <div class="flex gap-4">
                <img
                    v-if="show.posterPath"
                    :src="`/api/image/${show.posterPath}`"
                    class="h-24 w-16 rounded object-cover"
                    @error="($event.target as HTMLImageElement).style.display = 'none'" />
                <div class="flex-1">
                    <div class="flex items-start justify-between gap-2">
                        <h3 class="text-primary-content text-lg font-semibold">{{ show.title }}</h3>
                        <span class="bg-accent/20 text-accent rounded px-1.5 py-0.5 text-xs">
                            TV
                        </span>
                    </div>
                    <p v-if="show.year" class="text-secondary-content mt-1 text-sm">
                        {{ show.year }}
                    </p>
                    <p class="text-secondary-content mt-2 text-sm">
                        {{ totalEpisodes }} {{ translate('translationTest.episodesWithSubtitles') }}
                    </p>
                </div>
            </div>
        </div>

        <!-- Seasons -->
        <div v-if="isExpanded" class="border-accent border-t">
            <div
                v-for="season in show.seasons"
                :key="season.seasonNumber"
                class="border-secondary/30 border-b">
                <!-- Season header -->
                <div
                    class="hover:bg-secondary/50 cursor-pointer px-4 py-3 transition"
                    @click="toggleSeason(season.seasonNumber)">
                    <div class="flex items-center justify-between">
                        <div class="flex items-center gap-2">
                            <CaretButton
                                :is-expanded="!expandedSeasons.has(season.seasonNumber)"
                                class="h-4 w-4" />
                            <span class="text-primary-content font-medium">
                                {{
                                    season.seasonNumber === 0
                                        ? translate('tvShows.specials')
                                        : translate('translationTest.season') +
                                          ' ' +
                                          season.seasonNumber
                                }}
                            </span>
                            <span class="text-secondary-content text-sm">
                                ({{ season.episodes.length }}
                                {{ translate('translationTest.episodes') }})
                            </span>
                        </div>
                    </div>
                </div>

                <!-- Episodes -->
                <div v-if="expandedSeasons.has(season.seasonNumber)" class="bg-secondary/30">
                    <div
                        v-for="episode in season.episodes"
                        :key="episode.episodeId"
                        class="hover:bg-secondary border-secondary/20 cursor-pointer border-b px-4 py-3 transition"
                        @click="selectEpisode(episode)">
                        <div class="flex items-center justify-between gap-2">
                            <div class="flex-1">
                                <div class="flex items-center gap-2">
                                    <span class="text-accent font-mono text-sm">
                                        {{ episode.displayTitle }}
                                    </span>
                                    <span class="text-primary-content font-medium">
                                        {{ episode.title }}
                                    </span>
                                </div>
                                <div class="mt-1 flex flex-wrap gap-1">
                                    <span
                                        v-for="subtitle in episode.subtitles.slice(0, 5)"
                                        :key="subtitle.path"
                                        class="bg-primary text-primary-content rounded px-1.5 py-0.5 text-xs">
                                        {{ subtitle.language?.toUpperCase() || '??' }}
                                    </span>
                                    <span
                                        v-if="episode.subtitles.length > 5"
                                        class="text-secondary-content text-xs">
                                        +{{ episode.subtitles.length - 5 }}
                                    </span>
                                    <span
                                        v-for="embSub in (episode.embeddedSubtitles || []).slice(
                                            0,
                                            3
                                        )"
                                        :key="`emb-${embSub.streamIndex}`"
                                        class="rounded border px-1.5 py-0.5 text-xs"
                                        :class="getEmbeddedBadgeClasses(embSub)">
                                        <span class="mr-0.5">📦</span>
                                        {{ formatEmbeddedLanguage(embSub) }}
                                    </span>
                                    <span
                                        v-if="(episode.embeddedSubtitles?.length || 0) > 3"
                                        class="text-secondary-content text-xs">
                                        +{{ (episode.embeddedSubtitles?.length || 0) - 3 }} emb
                                    </span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from '@/plugins/i18n'
import CaretButton from '@/components/common/CaretButton.vue'

interface SubtitleInfo {
    path: string
    language?: string
    fileName?: string
}

interface EmbeddedSubtitleInfo {
    streamIndex: number
    language?: string
    title?: string
    codecName: string
    isTextBased: boolean
    isDefault: boolean
    isForced: boolean
}

interface EpisodePreview {
    episodeId: number
    episodeNumber: number
    title: string
    displayTitle: string
    seasonNumber: number
    subtitles: SubtitleInfo[]
    embeddedSubtitles?: EmbeddedSubtitleInfo[]
}

interface SeasonPreview {
    seasonNumber: number
    episodes: EpisodePreview[]
}

interface ShowSearchResult {
    title: string
    showId: number
    posterPath?: string
    year?: number
    seasons: SeasonPreview[]
}

const props = defineProps<{
    show: ShowSearchResult
}>()

const emit = defineEmits<{
    select: [episode: EpisodePreview, show: ShowSearchResult]
}>()

const { translate } = useI18n()

const isExpanded = ref(false)
const expandedSeasons = ref<Set<number>>(new Set())

const totalEpisodes = computed(() => {
    return props.show.seasons.reduce((sum, season) => sum + season.episodes.length, 0)
})

const getEmbeddedBadgeClasses = (sub: EmbeddedSubtitleInfo): string => {
    if (!sub.isTextBased) {
        return 'text-secondary-content/50 border-secondary-content/30 bg-secondary/30 opacity-60'
    }
    return 'text-amber-300 border-amber-500 bg-amber-900/30'
}

const formatEmbeddedLanguage = (sub: EmbeddedSubtitleInfo): string => {
    if (sub.language) {
        return sub.language.toUpperCase()
    }
    return `#${sub.streamIndex}`
}

function toggleShow() {
    isExpanded.value = !isExpanded.value
    if (isExpanded.value && props.show.seasons.length > 0) {
        expandedSeasons.value.add(props.show.seasons[0].seasonNumber)
    }
}

function toggleSeason(seasonNumber: number) {
    if (expandedSeasons.value.has(seasonNumber)) {
        expandedSeasons.value.delete(seasonNumber)
    } else {
        expandedSeasons.value.add(seasonNumber)
    }
}

function selectEpisode(episode: EpisodePreview) {
    emit('select', episode, props.show)
}
</script>

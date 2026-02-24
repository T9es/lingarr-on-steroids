<template>
    <PageLayout>
        <div v-if="settingsCompleted === 'true'" class="w-full">
            <div class="bg-tertiary flex flex-wrap items-center justify-between gap-2 p-4">
                <SearchComponent v-model="filter" />
                <div
                    class="flex w-full flex-col gap-2 md:w-fit md:flex-row md:justify-between md:space-x-2">
                    <SortControls
                        v-model="filter"
                        :options="[
                            {
                                label: translate('common.sortByTitle'),
                                value: 'Title'
                            },
                            {
                                label: translate('common.sortByAdded'),
                                value: 'DateAdded'
                            }
                        ]" />
                </div>
            </div>

            <div class="w-full px-4">
                <div class="border-accent grid grid-cols-14 border-b font-bold">
                    <div class="col-span-3 px-4 py-2">{{ translate('movies.title') }}</div>
                    <div
                        class="col-span-1 flex items-center justify-center px-2 py-2"
                        title="Translation Status">
                        📊
                    </div>
                    <div class="col-span-3 px-4 py-2">{{ translate('movies.subtitles') }}</div>
                    <div class="col-span-1 px-4 py-2">
                        {{ translate('movies.exclude') }}
                    </div>
                    <div class="col-span-1 px-4 py-2 text-center">
                        <span class="hidden md:block">
                            {{ translate('movies.priority') }}
                        </span>
                        <span class="block md:hidden">★</span>
                    </div>
                    <div class="col-span-1 px-4 py-2 text-center">
                        <span class="hidden md:block">
                            {{ translate('movies.translateNow') }}
                        </span>
                        <span class="block md:hidden">⚡</span>
                    </div>
                    <div class="col-span-1 px-4 py-2 text-center">
                        <span class="hidden md:block">
                            {{ translate('movies.integrityCheck') }}
                        </span>
                        <span class="block md:hidden">✔</span>
                    </div>
                    <div class="col-span-2 px-4 py-2">
                        {{ translate('movies.ageThreshold') }}
                        <span class="float-right">
                            <ReloadComponent @toggle:update="movieStore.fetch()" />
                        </span>
                    </div>
                </div>
                <template v-for="group in groupedMovies" :key="group.key">
                    <!-- Single movie - normal display -->
                    <template v-if="group.movies.length === 1">
                        <div class="border-accent grid grid-cols-14 border-b">
                            <div class="col-span-3 px-4 py-2">
                                {{ group.movies[0].title }}
                            </div>
                            <div class="col-span-1 flex items-center justify-center px-2 py-2">
                                <TranslationStateBadge
                                    :state="
                                        group.movies[0].translationState ??
                                        TRANSLATION_STATE.UNKNOWN
                                    " />
                            </div>
                            <div class="col-span-3 flex flex-wrap items-center gap-2 px-4 py-2">
                                <ContextMenu
                                    v-for="(subtitle, index) in group.movies[0].subtitles"
                                    :key="`ext-${index}-${subtitle.fileName}`"
                                    :subtitle="subtitle"
                                    :media="group.movies[0]"
                                    :media-type="MEDIA_TYPE.MOVIE"
                                    @update:toggle="toggleMovie(group.movies[0])">
                                    <BadgeComponent>
                                        {{ subtitle.language.toUpperCase() }}
                                        <span
                                            v-if="subtitle.caption"
                                            class="text-primary-content/50">
                                            - {{ subtitle.caption.toUpperCase() }}
                                        </span>
                                    </BadgeComponent>
                                </ContextMenu>
                                <ContextMenu
                                    v-for="embeddedSub in getEmbeddedSubtitles(group.movies[0])"
                                    :key="`emb-${group.movies[0].id}-${embeddedSub.id}`"
                                    :embeddedSubtitle="embeddedSub"
                                    :media="group.movies[0]"
                                    :media-type="MEDIA_TYPE.MOVIE"
                                    @update:toggle="toggleMovie(group.movies[0])"
                                    v-slot="{ isExtracting }">
                                    <BadgeComponent :classes="getEmbeddedBadgeClasses(embeddedSub)">
                                        <span class="mr-1">📦</span>
                                        {{ formatEmbeddedLanguage(embeddedSub) }}
                                        <span
                                            v-if="embeddedSub.title"
                                            class="ml-1 text-amber-200/70">
                                            ({{ truncate(embeddedSub.title, 10) }})
                                        </span>
                                        <span
                                            v-if="embeddedSub.isForced"
                                            class="ml-1 text-xs opacity-70">
                                            F
                                        </span>
                                        <span
                                            v-if="embeddedSub.isDefault"
                                            class="ml-1 text-xs opacity-70">
                                            D
                                        </span>
                                        <LoaderCircleIcon
                                            v-if="isExtracting"
                                            class="ml-1 h-3 w-3 animate-spin" />
                                    </BadgeComponent>
                                </ContextMenu>
                            </div>
                            <div class="col-span-1 flex flex-wrap items-center gap-2 px-4 py-2">
                                <ToggleButton
                                    v-model="group.movies[0].excludeFromTranslation"
                                    size="small"
                                    @toggle:update="
                                        () =>
                                            movieStore.exclude(MEDIA_TYPE.MOVIE, group.movies[0].id)
                                    " />
                            </div>
                            <div
                                class="col-span-1 flex items-center justify-center px-4 py-2"
                                @click.stop>
                                <ToggleButton
                                    v-model="group.movies[0].isPriority"
                                    size="small"
                                    @toggle:update="
                                        () =>
                                            movieStore.priority(
                                                MEDIA_TYPE.MOVIE,
                                                group.movies[0].id
                                            )
                                    " />
                            </div>
                            <div
                                class="col-span-1 flex items-center justify-center px-4 py-2"
                                @click.stop>
                                <button
                                    class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                    :disabled="translatingMovies[group.movies[0].id]"
                                    :title="translate('movies.translateNow')"
                                    @click="translateMovie(group.movies[0])">
                                    <LoaderCircleIcon
                                        v-if="translatingMovies[group.movies[0].id]"
                                        class="h-4 w-4 animate-spin" />
                                    <LanguageIcon v-else class="h-4 w-4" />
                                </button>
                            </div>
                            <div
                                class="col-span-1 flex items-center justify-center px-4 py-2"
                                @click.stop>
                                <button
                                    class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                    :disabled="integrityCheckingMovies[group.movies[0].id]"
                                    :title="translate('movies.integrityCheck')"
                                    @click="checkIntegrityMovie(group.movies[0])">
                                    <LoaderCircleIcon
                                        v-if="integrityCheckingMovies[group.movies[0].id]"
                                        class="h-4 w-4 animate-spin" />
                                    <CheckMarkCicleIcon v-else class="h-4 w-4" />
                                </button>
                            </div>
                            <div class="col-span-2 flex items-center px-4 py-2" @click.stop>
                                <InputComponent
                                    :model-value="group.movies[0]?.translationAgeThreshold"
                                    :placeholder="translate('movies.hours')"
                                    class="w-14"
                                    size="sm"
                                    type="number"
                                    validation-type="number"
                                    @update:value="
                                        (value) => {
                                            group.movies[0].translationAgeThreshold = value
                                            movieStore.updateThreshold(
                                                MEDIA_TYPE.MOVIE,
                                                group.movies[0].id,
                                                value
                                            )
                                        }
                                    " />
                            </div>
                        </div>
                    </template>

                    <!-- Multiple movies (duplicates) - collapsible display -->
                    <template v-else>
                        <!-- Collapsed header row -->
                        <div
                            class="border-accent hover:bg-secondary/30 grid cursor-pointer grid-cols-14 border-b"
                            @click="toggleGroup(group.key)">
                            <div class="col-span-3 px-4 py-2">
                                <span class="mr-2">{{ group.title }}</span>
                                <span class="text-secondary-content text-sm">
                                    ({{ group.movies.length }} instances)
                                </span>
                            </div>
                            <div class="col-span-1 flex items-center justify-center px-2 py-2">
                                <span class="text-secondary-content text-sm">
                                    {{ getGroupStateSummary(group.movies) }}
                                </span>
                            </div>
                            <div class="col-span-11 flex items-center justify-end px-4 py-2">
                                <span class="text-secondary-content text-sm">
                                    {{
                                        isGroupExpanded(group.key)
                                            ? 'Click to collapse'
                                            : 'Click to expand'
                                    }}
                                </span>
                            </div>
                        </div>

                        <!-- Expanded instance rows -->
                        <template v-if="isGroupExpanded(group.key)">
                            <div
                                v-for="item in group.movies"
                                :key="item.id"
                                class="border-accent/50 bg-secondary/20 grid grid-cols-14 border-b">
                                <div class="col-span-3 px-4 py-2">
                                    <span class="text-secondary-content mr-2 text-sm">
                                        {{ getInstanceName(item.sourceInstanceId) }}:
                                    </span>
                                    <span class="text-primary-content/80">{{ item.title }}</span>
                                </div>
                                <div class="col-span-1 flex items-center justify-center px-2 py-2">
                                    <TranslationStateBadge
                                        :state="
                                            item.translationState ?? TRANSLATION_STATE.UNKNOWN
                                        " />
                                </div>
                                <div class="col-span-3 flex flex-wrap items-center gap-2 px-4 py-2">
                                    <ContextMenu
                                        v-for="(subtitle, index) in item.subtitles"
                                        :key="`ext-${index}-${subtitle.fileName}`"
                                        :subtitle="subtitle"
                                        :media="item"
                                        :media-type="MEDIA_TYPE.MOVIE"
                                        @update:toggle="toggleMovie(item)">
                                        <BadgeComponent>
                                            {{ subtitle.language.toUpperCase() }}
                                            <span
                                                v-if="subtitle.caption"
                                                class="text-primary-content/50">
                                                - {{ subtitle.caption.toUpperCase() }}
                                            </span>
                                        </BadgeComponent>
                                    </ContextMenu>
                                    <ContextMenu
                                        v-for="embeddedSub in getEmbeddedSubtitles(item)"
                                        :key="`emb-${item.id}-${embeddedSub.id}`"
                                        :embeddedSubtitle="embeddedSub"
                                        :media="item"
                                        :media-type="MEDIA_TYPE.MOVIE"
                                        @update:toggle="toggleMovie(item)"
                                        v-slot="{ isExtracting }">
                                        <BadgeComponent
                                            :classes="getEmbeddedBadgeClasses(embeddedSub)">
                                            <span class="mr-1">📦</span>
                                            {{ formatEmbeddedLanguage(embeddedSub) }}
                                            <span
                                                v-if="embeddedSub.title"
                                                class="ml-1 text-amber-200/70">
                                                ({{ truncate(embeddedSub.title, 10) }})
                                            </span>
                                            <span
                                                v-if="embeddedSub.isForced"
                                                class="ml-1 text-xs opacity-70">
                                                F
                                            </span>
                                            <span
                                                v-if="embeddedSub.isDefault"
                                                class="ml-1 text-xs opacity-70">
                                                D
                                            </span>
                                            <LoaderCircleIcon
                                                v-if="isExtracting"
                                                class="ml-1 h-3 w-3 animate-spin" />
                                        </BadgeComponent>
                                    </ContextMenu>
                                </div>
                                <div class="col-span-1 flex flex-wrap items-center gap-2 px-4 py-2">
                                    <ToggleButton
                                        v-model="item.excludeFromTranslation"
                                        size="small"
                                        @toggle:update="
                                            () => movieStore.exclude(MEDIA_TYPE.MOVIE, item.id)
                                        " />
                                </div>
                                <div
                                    class="col-span-1 flex items-center justify-center px-4 py-2"
                                    @click.stop>
                                    <ToggleButton
                                        v-model="item.isPriority"
                                        size="small"
                                        @toggle:update="
                                            () => movieStore.priority(MEDIA_TYPE.MOVIE, item.id)
                                        " />
                                </div>
                                <div
                                    class="col-span-1 flex items-center justify-center px-4 py-2"
                                    @click.stop>
                                    <button
                                        class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                        :disabled="translatingMovies[item.id]"
                                        :title="translate('movies.translateNow')"
                                        @click="translateMovie(item)">
                                        <LoaderCircleIcon
                                            v-if="translatingMovies[item.id]"
                                            class="h-4 w-4 animate-spin" />
                                        <LanguageIcon v-else class="h-4 w-4" />
                                    </button>
                                </div>
                                <div
                                    class="col-span-1 flex items-center justify-center px-4 py-2"
                                    @click.stop>
                                    <button
                                        class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                        :disabled="integrityCheckingMovies[item.id]"
                                        :title="translate('movies.integrityCheck')"
                                        @click="checkIntegrityMovie(item)">
                                        <LoaderCircleIcon
                                            v-if="integrityCheckingMovies[item.id]"
                                            class="h-4 w-4 animate-spin" />
                                        <CheckMarkCicleIcon v-else class="h-4 w-4" />
                                    </button>
                                </div>
                                <div class="col-span-2 flex items-center px-4 py-2" @click.stop>
                                    <InputComponent
                                        :model-value="item?.translationAgeThreshold"
                                        :placeholder="translate('movies.hours')"
                                        class="w-14"
                                        size="sm"
                                        type="number"
                                        validation-type="number"
                                        @update:value="
                                            (value) => {
                                                item.translationAgeThreshold = value
                                                movieStore.updateThreshold(
                                                    MEDIA_TYPE.MOVIE,
                                                    item.id,
                                                    value
                                                )
                                            }
                                        " />
                                </div>
                            </div>
                        </template>
                    </template>
                </template>
            </div>

            <PaginationComponent
                v-if="movies.totalCount"
                v-model="filter"
                :total-count="movies.totalCount"
                :page-size="movies.pageSize" />
        </div>
        <NoMediaNotification v-else />
    </PageLayout>
</template>

<script setup lang="ts">
import { computed, onMounted, ComputedRef, reactive, watch, ref } from 'vue'
import {
    IFilter,
    IMovie,
    IPagedResult,
    IEmbeddedSubtitle,
    MEDIA_TYPE,
    SETTINGS,
    TRANSLATION_STATE,
    IInstance
} from '@/ts'
import useDebounce from '@/composables/useDebounce'
import { useMovieStore } from '@/store/movie'
import { useSettingStore } from '@/store/setting'
import { useInstanceStore } from '@/store/instance'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import PaginationComponent from '@/components/common/PaginationComponent.vue'
import PageLayout from '@/components/layout/PageLayout.vue'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import SortControls from '@/components/common/SortControls.vue'
import SearchComponent from '@/components/common/SearchComponent.vue'
import ContextMenu from '@/components/layout/ContextMenu.vue'
import ReloadComponent from '@/components/common/ReloadComponent.vue'
import NoMediaNotification from '@/components/common/NoMediaNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import LanguageIcon from '@/components/icons/LanguageIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import CheckMarkCicleIcon from '@/components/icons/CheckMarkCicleIcon.vue'
import TranslationStateBadge from '@/components/common/TranslationStateBadge.vue'

const { translate } = useI18n()
const movieStore = useMovieStore()
const settingStore = useSettingStore()
const instanceStore = useInstanceStore()

const translatingMovies = reactive<Record<number, boolean>>({})
const integrityCheckingMovies = reactive<Record<number, boolean>>({})

// Group management for multi-instance duplicates
const expandedGroups = ref<Set<string>>(new Set())

interface IMovieGroup {
    key: string
    title: string
    year?: number
    movies: IMovie[]
}

const groupedMovies = computed<IMovieGroup[]>(() => {
    const groups = new Map<string, IMovieGroup>()

    for (const movie of movies.value.items) {
        // Create a normalized key for grouping (title + year from path)
        const normalizedTitle = movie.title.toLowerCase().trim()
        // Extract year from path if available (e.g., "/movies/Movie Name (2024)/")
        const year = movie.path?.match(/\((\d{4})\)/)?.[1]
        const groupKey = `${normalizedTitle}-${year || 'unknown'}`

        if (!groups.has(groupKey)) {
            groups.set(groupKey, {
                key: groupKey,
                title: movie.title,
                year: year ? parseInt(year) : undefined,
                movies: []
            })
        }
        groups.get(groupKey)!.movies.push(movie)
    }

    return Array.from(groups.values())
})

const getInstanceName = (sourceInstanceId: string | null | undefined): string => {
    if (!sourceInstanceId) return 'Default'

    // Get instance name from setting store
    const instancesJson = settingStore.getSetting(SETTINGS.RADARR_INSTANCES) as string
    if (instancesJson) {
        try {
            const instances = JSON.parse(instancesJson) as IInstance[]
            const instance = instances.find((i) => i.id === sourceInstanceId)
            return instance?.name || sourceInstanceId
        } catch {
            return sourceInstanceId
        }
    }
    return 'Default'
}

const toggleGroup = (groupKey: string) => {
    if (expandedGroups.value.has(groupKey)) {
        expandedGroups.value.delete(groupKey)
    } else {
        expandedGroups.value.add(groupKey)
    }
}

const isGroupExpanded = (groupKey: string): boolean => {
    return expandedGroups.value.has(groupKey)
}

const getGroupStateSummary = (movies: IMovie[]): string => {
    const states = movies.map((m) => m.translationState)
    const translated = states.filter((s) => s === TRANSLATION_STATE.COMPLETE).length
    const pending = states.filter((s) => s === TRANSLATION_STATE.PENDING).length
    const failed = states.filter((s) => s === TRANSLATION_STATE.FAILED).length

    if (translated === movies.length) return 'All translated'
    if (failed > 0) return `${failed} failed`
    if (pending > 0) return `${pending} pending`
    return 'Mixed'
}

interface TranslateMediaResponse {
    translationsQueued: number
    message: string
}

const settingsCompleted: ComputedRef<string> = computed(
    () => settingStore.getSetting(SETTINGS.RADARR_SETTINGS_COMPLETED) as string
)
const movies: ComputedRef<IPagedResult<IMovie>> = computed(() => movieStore.get)
const filter: ComputedRef<IFilter> = computed({
    get: () => movieStore.getFilter,
    set: useDebounce((value: IFilter) => {
        movieStore.setFilter(value)
    }, 300)
})

// Fetch embedded subtitles when movies change
watch(
    () => movies.value.items,
    async (newItems) => {
        if (!newItems) return
        for (const movie of newItems) {
            if (!movie.embeddedSubtitles || movie.embeddedSubtitles.length === 0) {
                try {
                    movie.embeddedSubtitles = await services.subtitle.getEmbeddedSubtitles<
                        IEmbeddedSubtitle[]
                    >('movie', movie.id)
                } catch (error) {
                    console.error(
                        `Failed to fetch embedded subtitles for movie ${movie.id}:`,
                        error
                    )
                }
            }
        }
    },
    { immediate: true }
)

const toggleMovie = useDebounce(async (movie: IMovie) => {
    instanceStore.setPoster({ content: movie, type: 'movie' })
}, 1000)

const translateMovie = async (movie: IMovie) => {
    translatingMovies[movie.id] = true
    try {
        const response = await services.translate.translateMedia<TranslateMediaResponse>(
            movie.id,
            MEDIA_TYPE.MOVIE
        )
        console.log(response.message)
    } catch (error) {
        console.error('Failed to translate movie:', error)
    } finally {
        translatingMovies[movie.id] = false
    }
}

const checkIntegrityMovie = async (movie: IMovie) => {
    integrityCheckingMovies[movie.id] = true
    try {
        const count = await services.media.integrityCheck<number>(MEDIA_TYPE.MOVIE, movie.id)
        if (count > 0) {
            console.log(`Integrity check failed. Queued ${count} repair translations.`)
        } else {
            console.log('Integrity check passed or no repairs needed.')
        }
    } catch (error) {
        console.error('Failed to check integrity for movie:', error)
    } finally {
        integrityCheckingMovies[movie.id] = false
    }
}

const getEmbeddedSubtitles = (movie: IMovie): IEmbeddedSubtitle[] => {
    if (!movie.embeddedSubtitles) return []

    // Get external subtitle languages for deduplication
    const externalLanguages = new Set((movie.subtitles || []).map((s) => s.language?.toLowerCase()))

    // Filter out embedded subs that have already been extracted AND have a matching external subtitle
    return movie.embeddedSubtitles.filter((embSub) => {
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

const truncate = (str: string, len: number): string => {
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

onMounted(async () => {
    await movieStore.fetch()
})
</script>

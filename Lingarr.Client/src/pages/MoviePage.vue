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

            <div class="w-full overflow-x-auto px-4">
                <div class="border-accent grid min-w-[900px] grid-cols-14 border-b font-bold">
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
                        <div
                            class="border-accent hover:bg-secondary/50 grid grid-cols-14 border-b transition-colors">
                            <div class="col-span-3 px-4 py-2">
                                {{ group.movies[0].title }}
                                <span
                                    class="text-secondary-content ml-2 cursor-help text-xs"
                                    :title="formatOriginDetails(group.movies[0])">
                                    details
                                </span>
                            </div>
                            <div class="col-span-1 flex items-center justify-center px-2 py-2">
                                <TranslationStateBadge
                                    :state="
                                        group.movies[0].translationState ??
                                        TRANSLATION_STATE.UNKNOWN
                                    " />
                            </div>
                            <div class="col-span-3 flex flex-wrap items-center gap-2 px-4 py-2">
                                <template
                                    v-for="item in getAllSubtitles(group.movies[0]).slice(
                                        0,
                                        isSubtitlesExpanded(group.movies[0].id)
                                            ? undefined
                                            : MAX_VISIBLE_SUBTITLES
                                    )"
                                    :key="item.key">
                                    <ContextMenu
                                        v-if="item.type === 'external'"
                                        :subtitle="item.data as ISubtitle"
                                        :media="group.movies[0]"
                                        :media-type="MEDIA_TYPE.MOVIE"
                                        @update:toggle="toggleMovie(group.movies[0])">
                                        <BadgeComponent>
                                            {{ (item.data as ISubtitle).language.toUpperCase() }}
                                            <span
                                                v-if="(item.data as ISubtitle).caption"
                                                class="text-primary-content/50">
                                                -
                                                {{ (item.data as ISubtitle).caption.toUpperCase() }}
                                            </span>
                                        </BadgeComponent>
                                    </ContextMenu>
                                    <ContextMenu
                                        v-else
                                        v-slot="{ isExtracting }"
                                        :embedded-subtitle="item.data as IEmbeddedSubtitle"
                                        :media="group.movies[0]"
                                        :media-type="MEDIA_TYPE.MOVIE"
                                        @update:toggle="toggleMovie(group.movies[0])">
                                        <BadgeComponent
                                            :classes="
                                                getEmbeddedBadgeClasses(
                                                    item.data as IEmbeddedSubtitle
                                                )
                                            ">
                                            <span class="mr-1">📦</span>
                                            {{
                                                formatEmbeddedLanguage(
                                                    item.data as IEmbeddedSubtitle
                                                )
                                            }}
                                            <span
                                                v-if="(item.data as IEmbeddedSubtitle).title"
                                                class="ml-1 text-amber-200/70">
                                                ({{
                                                    truncate(
                                                        (item.data as IEmbeddedSubtitle).title,
                                                        10
                                                    )
                                                }})
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
                                    v-if="
                                        getAllSubtitles(group.movies[0]).length >
                                        MAX_VISIBLE_SUBTITLES
                                    "
                                    class="border-accent text-secondary-content hover:bg-accent/20 cursor-pointer rounded-full border px-3 py-1 text-xs font-semibold"
                                    @click="toggleSubtitles(group.movies[0].id)">
                                    {{
                                        isSubtitlesExpanded(group.movies[0].id)
                                            ? 'Show less'
                                            : `+${getAllSubtitles(group.movies[0]).length - MAX_VISIBLE_SUBTITLES} more`
                                    }}
                                </button>
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
                                class="col-span-1 flex items-center justify-center gap-2 px-4 py-2"
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
                                <button
                                    class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                    :disabled="recreatingMovies[group.movies[0].id]"
                                    :title="translate('common.recreate')"
                                    @click="recreateMovie(group.movies[0])">
                                    <LoaderCircleIcon
                                        v-if="recreatingMovies[group.movies[0].id]"
                                        class="h-4 w-4 animate-spin" />
                                    <ReloadIcon v-else class="h-4 w-4" />
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
                                class="border-accent/50 hover:bg-secondary/50 grid grid-cols-14 border-b transition-colors">
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
                                    <template
                                        v-for="itemSub in getAllSubtitles(item).slice(
                                            0,
                                            isSubtitlesExpanded(item.id)
                                                ? undefined
                                                : MAX_VISIBLE_SUBTITLES
                                        )"
                                        :key="itemSub.key">
                                        <ContextMenu
                                            v-if="itemSub.type === 'external'"
                                            :subtitle="itemSub.data as ISubtitle"
                                            :media="item"
                                            :media-type="MEDIA_TYPE.MOVIE"
                                            @update:toggle="toggleMovie(item)">
                                            <BadgeComponent>
                                                {{
                                                    (
                                                        itemSub.data as ISubtitle
                                                    ).language.toUpperCase()
                                                }}
                                                <span
                                                    v-if="(itemSub.data as ISubtitle).caption"
                                                    class="text-primary-content/50">
                                                    -
                                                    {{
                                                        (
                                                            itemSub.data as ISubtitle
                                                        ).caption.toUpperCase()
                                                    }}
                                                </span>
                                            </BadgeComponent>
                                        </ContextMenu>
                                        <ContextMenu
                                            v-else
                                            v-slot="{ isExtracting }"
                                            :embedded-subtitle="itemSub.data as IEmbeddedSubtitle"
                                            :media="item"
                                            :media-type="MEDIA_TYPE.MOVIE"
                                            @update:toggle="toggleMovie(item)">
                                            <BadgeComponent
                                                :classes="
                                                    getEmbeddedBadgeClasses(
                                                        itemSub.data as IEmbeddedSubtitle
                                                    )
                                                ">
                                                <span class="mr-1">📦</span>
                                                {{
                                                    formatEmbeddedLanguage(
                                                        itemSub.data as IEmbeddedSubtitle
                                                    )
                                                }}
                                                <span
                                                    v-if="(itemSub.data as IEmbeddedSubtitle).title"
                                                    class="ml-1 text-amber-200/70">
                                                    ({{
                                                        truncate(
                                                            (itemSub.data as IEmbeddedSubtitle)
                                                                .title,
                                                            10
                                                        )
                                                    }})
                                                </span>
                                                <span
                                                    v-if="
                                                        (itemSub.data as IEmbeddedSubtitle).isForced
                                                    "
                                                    class="ml-1 text-xs opacity-70">
                                                    F
                                                </span>
                                                <span
                                                    v-if="
                                                        (itemSub.data as IEmbeddedSubtitle)
                                                            .isDefault
                                                    "
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
                                        v-if="getAllSubtitles(item).length > MAX_VISIBLE_SUBTITLES"
                                        class="border-accent text-secondary-content hover:bg-accent/20 cursor-pointer rounded-full border px-3 py-1 text-xs font-semibold"
                                        @click="toggleSubtitles(item.id)">
                                        {{
                                            isSubtitlesExpanded(item.id)
                                                ? 'Show less'
                                                : `+${getAllSubtitles(item).length - MAX_VISIBLE_SUBTITLES} more`
                                        }}
                                    </button>
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
                                    class="col-span-1 flex items-center justify-center gap-2 px-4 py-2"
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
                                    <button
                                        class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                        :disabled="recreatingMovies[item.id]"
                                        :title="translate('common.recreate')"
                                        @click="recreateMovie(item)">
                                        <LoaderCircleIcon
                                            v-if="recreatingMovies[item.id]"
                                            class="h-4 w-4 animate-spin" />
                                        <ReloadIcon v-else class="h-4 w-4" />
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
    ISubtitle,
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
import ReloadIcon from '@/components/icons/ReloadIcon.vue'
import TranslationStateBadge from '@/components/common/TranslationStateBadge.vue'

const { translate } = useI18n()
const movieStore = useMovieStore()
const settingStore = useSettingStore()
const instanceStore = useInstanceStore()

const translatingMovies = reactive<Record<number, boolean>>({})
const recreatingMovies = reactive<Record<number, boolean>>({})
const integrityCheckingMovies = reactive<Record<number, boolean>>({})

// Group management for multi-instance duplicates
const expandedGroups = ref<Set<string>>(new Set())

// Subtitle collapse state - track which movies have expanded subtitles
const expandedSubtitles = ref<Set<number>>(new Set())
const MAX_VISIBLE_SUBTITLES = 4

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
    const instancesValue = settingStore.getSetting(SETTINGS.RADARR_INSTANCES) as
        | string
        | IInstance[]
    if (instancesValue) {
        try {
            const instances = Array.isArray(instancesValue)
                ? instancesValue
                : (JSON.parse(instancesValue) as IInstance[])
            const instance = instances.find((i) => i.id === sourceInstanceId)
            return instance?.name || sourceInstanceId
        } catch {
            return sourceInstanceId
        }
    }
    return 'Default'
}

const formatOriginDetails = (movie: IMovie): string => {
    return `Instance: ${getInstanceName(movie.sourceInstanceId)} (${movie.sourceInstanceId ?? 'default'})\nPath: ${movie.path}`
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

const recreateMovie = async (movie: IMovie) => {
    recreatingMovies[movie.id] = true
    try {
        const response = await services.translate.translateMedia<TranslateMediaResponse>(
            movie.id,
            MEDIA_TYPE.MOVIE,
            true
        )
        console.log(response.message)
    } catch (error) {
        console.error('Failed to recreate movie:', error)
    } finally {
        recreatingMovies[movie.id] = false
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

const truncate = (str: string | null | undefined, len: number): string => {
    if (!str) return ''
    return str.length > len ? str.substring(0, len) + '...' : str
}

const getEmbeddedBadgeClasses = (sub: IEmbeddedSubtitle): string => {
    if (!sub.isTextBased) {
        if (sub.ocrStatus === 'Queued' || sub.ocrStatus === 'Processing') {
            return 'cursor-pointer text-accent border-accent bg-accent/10'
        }
        if (sub.isOcrUsable) {
            return 'cursor-pointer text-blue-300 border-blue-500 bg-blue-900/30'
        }
        if (sub.ocrStatus === 'BlockedLowQuality' || sub.ocrStatus === 'Failed') {
            return 'cursor-pointer text-red-300 border-red-500 bg-red-900/30'
        }
        if (sub.isOcrSupported) {
            return 'cursor-pointer text-blue-300 border-blue-500 bg-blue-900/20'
        }
        return 'cursor-not-allowed text-secondary-content/50 border-secondary-content/30 bg-secondary/30 opacity-60'
    }
    if (sub.isExtracted) {
        // Extracted - green tint
        return 'cursor-pointer text-green-300 border-green-500 bg-green-900/30'
    }
    // Text-based, not extracted - amber
    return 'cursor-pointer text-amber-300 border-amber-500 bg-amber-900/30'
}

const toggleSubtitles = (movieId: number) => {
    if (expandedSubtitles.value.has(movieId)) {
        expandedSubtitles.value.delete(movieId)
    } else {
        expandedSubtitles.value.add(movieId)
    }
}

const isSubtitlesExpanded = (movieId: number): boolean => {
    return expandedSubtitles.value.has(movieId)
}

interface SubtitleItem {
    type: 'external' | 'embedded'
    data: ISubtitle | IEmbeddedSubtitle
    key: string
}

const getAllSubtitles = (movie: IMovie): SubtitleItem[] => {
    const items: SubtitleItem[] = []
    const externalSubs = movie.subtitles || []
    const embeddedSubs = getEmbeddedSubtitles(movie)

    externalSubs.forEach((sub, index) => {
        items.push({
            type: 'external',
            data: sub,
            key: `ext-${index}-${(sub as ISubtitle).fileName}`
        })
    })

    embeddedSubs.forEach((sub) => {
        items.push({
            type: 'embedded',
            data: sub,
            key: `emb-${movie.id}-${sub.id}`
        })
    })

    return items
}

onMounted(async () => {
    await movieStore.fetch()
})
</script>

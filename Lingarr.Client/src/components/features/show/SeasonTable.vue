<template>
    <div class="bg-secondary p-4">
        <div
            class="border-secondary bg-primary text-secondary-content grid grid-cols-12 border-b-2 font-bold">
            <div class="col-span-6 px-4 py-2 md:col-span-3">
                {{ translate('tvShows.season') }}
            </div>
            <div class="col-span-4 flex justify-between px-4 py-2 md:col-span-6">
                <span>{{ translate('tvShows.episodes') }}</span>
                <span class="hidden md:block">
                    {{ translate('tvShows.exclude') }}
                </span>
                <span class="block md:hidden">⊘</span>
            </div>
            <div class="col-span-2 px-4 py-2 text-center md:col-span-2">
                <span class="hidden md:block">
                    {{ translate('tvShows.translateNow') }}
                </span>
                <span class="block md:hidden">⚡</span>
            </div>
            <div class="col-span-0 md:col-span-1"></div>
        </div>
        <!-- Seasons -->
        <div
            v-for="season in seasons"
            :key="season.id"
            class="bg-primary text-accent-content text-sm md:text-base">
            <div
                class="grid grid-cols-12"
                :class="{ 'cursor-pointer': season.episodes.length }"
                @click="toggleSeason(season)">
                <div class="col-span-6 flex items-center px-4 py-2 select-none md:col-span-3">
                    <CaretButton
                        v-if="season.episodes.length"
                        :is-expanded="expandedSeason?.id !== season.id"
                        class="pr-2" />
                    <div v-else class="w-7" />
                    <span v-if="season.seasonNumber == 0">
                        {{ translate('tvShows.specials') }}
                    </span>
                    <span v-else>{{ translate('tvShows.season') }} {{ season.seasonNumber }}</span>
                </div>
                <div class="col-span-4 flex justify-between px-4 py-2 select-none md:col-span-6">
                    <span>
                        {{ season.episodes.length }}
                        {{ translate('tvShows.episodesLine') }}
                    </span>
                    <span @click.stop>
                        <ToggleButton
                            v-model="season.excludeFromTranslation"
                            size="small"
                            @toggle:update="
                                () => showStore.exclude(MEDIA_TYPE.SEASON, season.id)
                            " />
                    </span>
                </div>
                <div class="col-span-2 flex items-center justify-center px-4 py-2 md:col-span-2" @click.stop>
                    <button
                        class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                        :disabled="translatingSeason[season.id]"
                        :title="translate('tvShows.translateNow')"
                        @click="translateSeason(season)">
                        <LoaderCircleIcon v-if="translatingSeason[season.id]" class="h-4 w-4 animate-spin" />
                        <LanguageIcon v-else class="h-4 w-4" />
                    </button>
                </div>
                <div class="col-span-0 md:col-span-1"></div>
            </div>
            <EpisodeTable
                v-if="expandedSeason?.id === season.id"
                :subtitles="subtitles"
                :episodes="season.episodes" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, Ref, reactive } from 'vue'
import { ISeason, ISubtitle, MEDIA_TYPE } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import EpisodeTable from '@/components/features/show/EpisodeTable.vue'
import CaretButton from '@/components/common/CaretButton.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import LanguageIcon from '@/components/icons/LanguageIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import { useShowStore } from '@/store/show'

const { translate } = useI18n()

defineProps<{
    seasons: ISeason[]
}>()

const showStore = useShowStore()
const subtitles: Ref<ISubtitle[]> = ref([])
const expandedSeason: Ref<ISeason | null> = ref(null)
const translatingSeason = reactive<Record<number, boolean>>({})

interface TranslateMediaResponse {
    translationsQueued: number
    message: string
}

async function toggleSeason(season: ISeason) {
    if (!season.episodes.length) return
    if (expandedSeason.value?.id === season.id) {
        expandedSeason.value = null
        return
    }
    expandedSeason.value = season
    await collectSubtitles()
}

async function collectSubtitles() {
    if (expandedSeason.value?.path) {
        subtitles.value = await services.subtitle.collect(expandedSeason.value.path)
    }
}

const translateSeason = async (season: ISeason) => {
    translatingSeason[season.id] = true
    try {
        const response = await services.translate.translateMedia<TranslateMediaResponse>(
            season.id,
            MEDIA_TYPE.SEASON
        )
        console.log(response.message)
    } catch (error) {
        console.error('Failed to translate season:', error)
    } finally {
        translatingSeason[season.id] = false
    }
}
</script>

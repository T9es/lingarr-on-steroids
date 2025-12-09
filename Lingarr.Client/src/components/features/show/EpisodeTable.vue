<template>
    <div class="bg-tertiary text-tertiary-content w-full">
        <div class="border-primary grid grid-cols-12 border-b-2 font-bold">
            <div class="col-span-1 px-4 py-2">
                <span class="hidden lg:block">
                    {{ translate('tvShows.episode') }}
                </span>
                <span class="block lg:hidden">#</span>
            </div>
            <div class="col-span-7 px-4 py-2 md:col-span-5">
                {{ translate('tvShows.episodeTitle') }}
            </div>
            <div class="col-span-4 flex justify-between py-2 pr-4 md:col-span-5">
                <span>{{ translate('tvShows.episodeSubtitles') }}</span>
                <span class="hidden md:block">
                    {{ translate('tvShows.exclude') }}
                </span>
                <span class="block md:hidden">⊘</span>
            </div>
        </div>
        <div v-for="episode in episodes" :key="episode.id" class="grid grid-cols-12">
            <div class="col-span-1 px-4 py-2">
                {{ episode.episodeNumber }}
            </div>
            <div class="col-span-7 px-4 py-2 md:col-span-5">
                {{ episode.title }}
            </div>
            <div class="col-span-4 flex justify-between pr-4 md:col-span-5">
                <div v-if="episode?.fileName" class="flex flex-wrap items-center gap-2">
                    <!-- External subtitles (blue badges) -->
                    <ContextMenu
                        v-for="(subtitle, jndex) in getSubtitle(episode.fileName)"
                        :key="`ext-${episode.id}-${jndex}`"
                        :media-type="MEDIA_TYPE.EPISODE"
                        :media="episode"
                        :subtitle="subtitle">
                        <BadgeComponent>
                            {{ subtitle.language.toUpperCase() }}
                            <span v-if="subtitle.caption" class="text-primary-content/50">
                                - {{ subtitle.caption.toUpperCase() }}
                            </span>
                        </BadgeComponent>
                    </ContextMenu>
                    <!-- Embedded subtitles (amber badges with 📦 icon) -->
                    <div
                        v-for="embeddedSub in getEmbeddedSubtitles(episode)"
                        :key="`emb-${episode.id}-${embeddedSub.id}`"
                        class="relative">
                        <BadgeComponent
                            :classes="getEmbeddedBadgeClasses(embeddedSub)"
                            @click="handleEmbeddedClick(episode, embeddedSub)">
                            <span class="mr-1">📦</span>
                            {{ formatEmbeddedLanguage(embeddedSub) }}
                            <span v-if="embeddedSub.title" class="text-amber-200/70 ml-1">
                                ({{ truncate(embeddedSub.title, 10) }})
                            </span>
                            <span v-if="embeddedSub.isForced" class="ml-1 text-xs opacity-70">F</span>
                            <span v-if="embeddedSub.isDefault" class="ml-1 text-xs opacity-70">D</span>
                            <LoaderCircleIcon
                                v-if="extractingStreams[`${episode.id}-${embeddedSub.streamIndex}`]"
                                class="ml-1 h-3 w-3 animate-spin" />
                        </BadgeComponent>
                    </div>
                </div>
                <div class="col-span-1 px-1 py-2 md:col-span-1">
                    <ToggleButton
                        v-model="episode.excludeFromTranslation"
                        size="small"
                        @toggle:update="() => showStore.exclude(MEDIA_TYPE.EPISODE, episode.id)" />
                </div>
            </div>
        </div>
    </div>
</template>
<script setup lang="ts">
import { reactive, onMounted } from 'vue'
import { IEpisode, ISubtitle, IEmbeddedSubtitle, MEDIA_TYPE } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import ContextMenu from '@/components/layout/ContextMenu.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import { useShowStore } from '@/store/show'
import services from '@/services'

const { translate } = useI18n()

const props = defineProps<{
    episodes: IEpisode[]
    subtitles: ISubtitle[]
}>()
const showStore = useShowStore()

// Track which streams are currently being extracted
const extractingStreams = reactive<Record<string, boolean>>({})

// Fetch embedded subtitles for episodes on mount
onMounted(async () => {
    for (const episode of props.episodes) {
        if (!episode.embeddedSubtitles || episode.embeddedSubtitles.length === 0) {
            try {
                episode.embeddedSubtitles = await services.subtitle.getEmbeddedSubtitles<IEmbeddedSubtitle[]>(
                    'episode',
                    episode.id
                )
            } catch (error) {
                console.error(`Failed to fetch embedded subtitles for episode ${episode.id}:`, error)
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
        (getSubtitle(episode.fileName) || []).map(s => s.language?.toLowerCase())
    )
    
    // Filter out embedded subs that have already been extracted AND have a matching external subtitle
    return episode.embeddedSubtitles.filter(embSub => {
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

const handleEmbeddedClick = async (episode: IEpisode, sub: IEmbeddedSubtitle) => {
    // Don't allow extraction of image-based subtitles
    if (!sub.isTextBased) {
        alert(translate('embedded.imageBased'))
        return
    }
    
    // If already extracted, just show info
    if (sub.isExtracted) {
        alert(`${translate('embedded.extracted')}: ${sub.extractedPath}`)
        return
    }
    
    const key = `${episode.id}-${sub.streamIndex}`
    if (extractingStreams[key]) return
    
    try {
        extractingStreams[key] = true
        const result = await services.subtitle.extractSubtitle('episode', episode.id, sub.streamIndex)
        
        if (result.success) {
            // Update the local state
            sub.isExtracted = true
            sub.extractedPath = result.extractedPath
            alert(translate('embedded.extractSuccess'))
        } else {
            alert(`${translate('embedded.extractFailed')}: ${result.error}`)
        }
    } catch (error) {
        console.error('Extraction failed:', error)
        alert(translate('embedded.extractFailed'))
    } finally {
        extractingStreams[key] = false
    }
}
</script>

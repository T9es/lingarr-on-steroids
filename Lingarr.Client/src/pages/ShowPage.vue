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
                <!-- Shows -->
                <div class="border-accent grid min-w-[700px] grid-cols-12 border-b font-bold">
                    <div class="col-span-6 px-4 py-2">{{ translate('tvShows.title') }}</div>
                    <div class="col-span-1 px-4 py-2">
                        <span class="hidden md:block">
                            {{ translate('tvShows.exclude') }}
                        </span>
                        <span class="block md:hidden">⊘</span>
                    </div>
                    <div class="col-span-2 px-4 py-2">
                        {{ translate('tvShows.ageThreshold') }}
                    </div>
                    <div class="col-span-1 px-4 py-2 text-center">
                        <span class="hidden md:block">
                            {{ translate('tvShows.priority') }}
                        </span>
                        <span class="block md:hidden">★</span>
                    </div>
                    <div class="col-span-1 px-4 py-2 text-center">
                        <span class="hidden md:block">
                            {{ translate('tvShows.translateNow') }}
                        </span>
                        <span class="block md:hidden">⚡</span>
                    </div>
                    <div class="col-span-1 flex justify-end px-4 py-2">
                        <ReloadComponent @toggle:update="showStore.fetch()" />
                    </div>
                </div>
                <template v-for="group in groupedShows" :key="group.key">
                    <!-- Single show - normal display -->
                    <template v-if="group.shows.length === 1">
                        <div
                            class="border-accent hover:bg-secondary/50 grid cursor-pointer grid-cols-12 border-b transition-colors"
                            @click="toggleShow(group.shows[0])">
                            <div class="col-span-6 flex items-center px-4 py-2">
                                <CaretButton
                                    :is-expanded="expandedShow !== group.shows[0].id"
                                    class="pr-2" />
                                {{ group.shows[0].title }}
                            </div>
                            <div class="col-span-1 flex items-center px-4 py-2" @click.stop>
                                <ToggleButton
                                    v-model="group.shows[0].excludeFromTranslation"
                                    size="small"
                                    @toggle:update="
                                        () => showStore.exclude(MEDIA_TYPE.SHOW, group.shows[0].id)
                                    " />
                            </div>
                            <div class="col-span-2 flex items-center px-4 py-2" @click.stop>
                                <InputComponent
                                    :model-value="group.shows[0].translationAgeThreshold ?? null"
                                    :placeholder="translate('tvShows.hours')"
                                    class="w-14"
                                    size="sm"
                                    type="number"
                                    validation-type="number"
                                    @update:value="
                                        (value) => {
                                            group.shows[0].translationAgeThreshold = value
                                            showStore.updateThreshold(
                                                MEDIA_TYPE.SHOW,
                                                group.shows[0].id,
                                                value
                                            )
                                        }
                                    " />
                            </div>
                            <div
                                class="col-span-1 flex items-center justify-center px-4 py-2"
                                @click.stop>
                                <ToggleButton
                                    v-model="group.shows[0].isPriority"
                                    size="small"
                                    @toggle:update="
                                        () => showStore.priority(MEDIA_TYPE.SHOW, group.shows[0].id)
                                    " />
                            </div>
                            <div
                                class="col-span-1 flex items-center justify-center px-4 py-2"
                                @click.stop>
                                <button
                                    class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                    :disabled="translatingShows[group.shows[0].id]"
                                    :title="translate('tvShows.translateNow')"
                                    @click="translateShow(group.shows[0])">
                                    <LoaderCircleIcon
                                        v-if="translatingShows[group.shows[0].id]"
                                        class="h-4 w-4 animate-spin" />
                                    <LanguageIcon v-else class="h-4 w-4" />
                                </button>
                            </div>
                            <div class="col-span-1"></div>
                        </div>
                        <SeasonTable
                            v-if="expandedShow === group.shows[0].id"
                            :seasons="group.shows[0].seasons" />
                    </template>

                    <!-- Multiple shows (duplicates) - collapsible display -->
                    <template v-else>
                        <!-- Collapsed header row -->
                        <div
                            class="border-accent hover:bg-secondary/30 grid cursor-pointer grid-cols-12 border-b"
                            @click="toggleGroup(group.key)">
                            <div class="col-span-6 flex items-center px-4 py-2">
                                <CaretButton
                                    :is-expanded="!isGroupExpanded(group.key)"
                                    class="pr-2" />
                                <span class="mr-2">{{ group.title }}</span>
                                <span class="text-secondary-content text-sm">
                                    ({{ group.shows.length }} instances)
                                </span>
                            </div>
                            <div class="col-span-1 flex items-center px-4 py-2">
                                <span class="text-secondary-content text-sm">
                                    {{ getGroupStateSummary(group.shows) }}
                                </span>
                            </div>
                            <div class="col-span-5 flex items-center justify-end px-4 py-2">
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
                            <div v-for="item in group.shows" :key="item.id">
                                <div
                                    class="border-accent/50 hover:bg-secondary/50 grid cursor-pointer grid-cols-12 border-b transition-colors"
                                    @click="toggleShow(item)">
                                    <div class="col-span-6 flex items-center px-4 py-2">
                                        <CaretButton
                                            :is-expanded="expandedShow !== item.id"
                                            class="pr-2" />
                                        <span class="text-secondary-content mr-2 text-sm">
                                            {{ getInstanceName(item.sourceInstanceId) }}:
                                        </span>
                                        <span class="text-primary-content/80">
                                            {{ item.title }}
                                        </span>
                                    </div>
                                    <div class="col-span-1 flex items-center px-4 py-2" @click.stop>
                                        <ToggleButton
                                            v-model="item.excludeFromTranslation"
                                            size="small"
                                            @toggle:update="
                                                () => showStore.exclude(MEDIA_TYPE.SHOW, item.id)
                                            " />
                                    </div>
                                    <div class="col-span-2 flex items-center px-4 py-2" @click.stop>
                                        <InputComponent
                                            :model-value="item.translationAgeThreshold ?? null"
                                            :placeholder="translate('tvShows.hours')"
                                            class="w-14"
                                            size="sm"
                                            type="number"
                                            validation-type="number"
                                            @update:value="
                                                (value) => {
                                                    item.translationAgeThreshold = value
                                                    showStore.updateThreshold(
                                                        MEDIA_TYPE.SHOW,
                                                        item.id,
                                                        value
                                                    )
                                                }
                                            " />
                                    </div>
                                    <div
                                        class="col-span-1 flex items-center justify-center px-4 py-2"
                                        @click.stop>
                                        <ToggleButton
                                            v-model="item.isPriority"
                                            size="small"
                                            @toggle:update="
                                                () => showStore.priority(MEDIA_TYPE.SHOW, item.id)
                                            " />
                                    </div>
                                    <div
                                        class="col-span-1 flex items-center justify-center px-4 py-2"
                                        @click.stop>
                                        <button
                                            class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                            :disabled="translatingShows[item.id]"
                                            :title="translate('tvShows.translateNow')"
                                            @click="translateShow(item)">
                                            <LoaderCircleIcon
                                                v-if="translatingShows[item.id]"
                                                class="h-4 w-4 animate-spin" />
                                            <LanguageIcon v-else class="h-4 w-4" />
                                        </button>
                                    </div>
                                    <div class="col-span-1"></div>
                                </div>
                                <SeasonTable
                                    v-if="expandedShow === item.id"
                                    :seasons="item.seasons" />
                            </div>
                        </template>
                    </template>
                </template>
            </div>

            <PaginationComponent
                v-if="shows.totalCount"
                v-model="filter"
                :total-count="shows.totalCount"
                :page-size="shows.pageSize" />
        </div>
        <NoMediaNotification v-else />
    </PageLayout>
</template>

<script setup lang="ts">
import { ref, Ref, computed, onMounted, ComputedRef, reactive } from 'vue'
import { IFilter, IPagedResult, IShow, MEDIA_TYPE, SETTINGS, IInstance } from '@/ts'
import useDebounce from '@/composables/useDebounce'
import { useInstanceStore } from '@/store/instance'
import { useSettingStore } from '@/store/setting'
import { useShowStore } from '@/store/show'
import { useI18n } from '@/plugins/i18n'
import services from '@/services'
import PaginationComponent from '@/components/common/PaginationComponent.vue'
import PageLayout from '@/components/layout/PageLayout.vue'
import SearchComponent from '@/components/common/SearchComponent.vue'
import CaretButton from '@/components/common/CaretButton.vue'
import SortControls from '@/components/common/SortControls.vue'
import ReloadComponent from '@/components/common/ReloadComponent.vue'
import NoMediaNotification from '@/components/common/NoMediaNotification.vue'
import ToggleButton from '@/components/common/ToggleButton.vue'
import SeasonTable from '@/components/features/show/SeasonTable.vue'
import InputComponent from '@/components/common/InputComponent.vue'
import LanguageIcon from '@/components/icons/LanguageIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
const { translate } = useI18n()
const instanceStore = useInstanceStore()
const showStore = useShowStore()
const settingStore = useSettingStore()
const expandedShow: Ref<boolean | number | null> = ref(null)

const translatingShows = reactive<Record<number, boolean>>({})

// Group management for multi-instance duplicates
const expandedGroups = ref<Set<string>>(new Set())

interface IShowGroup {
    key: string
    title: string
    shows: IShow[]
}

const groupedShows = computed<IShowGroup[]>(() => {
    const groups = new Map<string, IShowGroup>()

    for (const show of shows.value.items) {
        // Create a normalized key for grouping (title)
        const normalizedTitle = show.title.toLowerCase().trim()
        const groupKey = normalizedTitle

        if (!groups.has(groupKey)) {
            groups.set(groupKey, {
                key: groupKey,
                title: show.title,
                shows: []
            })
        }
        groups.get(groupKey)!.shows.push(show)
    }

    return Array.from(groups.values())
})

const getInstanceName = (sourceInstanceId: string | null | undefined): string => {
    if (!sourceInstanceId) return 'Default'

    // Get instance name from setting store
    const instancesValue = settingStore.getSetting(SETTINGS.SONARR_INSTANCES) as
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

const getGroupStateSummary = (showList: IShow[]): string => {
    // Shows don't have translationState directly - count excluded vs active
    const excluded = showList.filter((s) => s.excludeFromTranslation).length
    if (excluded === showList.length) return 'All excluded'
    if (excluded > 0) return `${showList.length - excluded} active`
    return `${showList.length} instances`
}

interface TranslateMediaResponse {
    translationsQueued: number
    message: string
}

const settingsCompleted: ComputedRef<string> = computed(
    () => settingStore.getSetting(SETTINGS.SONARR_SETTINGS_COMPLETED) as string
)
const shows: ComputedRef<IPagedResult<IShow>> = computed(() => showStore.get)
const filter: ComputedRef<IFilter> = computed({
    get: () => showStore.getFilter,
    set: useDebounce((value: IFilter) => {
        showStore.setFilter(value)
    }, 300)
})

async function toggleShow(show: IShow) {
    if (expandedShow.value === show.id) {
        expandedShow.value = null
        return
    }

    if (!show.seasons || show.seasons.length === 0) {
        await showStore.fetchShow(show.id)
        const updatedShow = showStore.get.items.find((s) => s.id === show.id)
        if (updatedShow) show = updatedShow
    }

    instanceStore.setPoster({ content: show, type: 'show' })
    expandedShow.value = show.id
}

const translateShow = async (show: IShow) => {
    translatingShows[show.id] = true
    try {
        const response = await services.translate.translateMedia<TranslateMediaResponse>(
            show.id,
            MEDIA_TYPE.SHOW
        )
        console.log(response.message)
    } catch (error) {
        console.error('Failed to translate show:', error)
    } finally {
        translatingShows[show.id] = false
    }
}

onMounted(async () => {
    await showStore.fetch()
})
</script>

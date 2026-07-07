<template>
    <div class="grid gap-4 p-4 xl:grid-cols-[380px_minmax(0,1fr)]">
        <CardComponent title="Custom Sources">
            <template #description>
                Register mounted container paths as movie or show roots and let Lingarr index them
                separately from Radarr and Sonarr.
            </template>

            <div class="space-y-3">
                <div>
                    <label class="text-primary-content/80 mb-1 block text-sm">Name</label>
                    <input
                        v-model="form.name"
                        class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2"
                        type="text" />
                </div>

                <div>
                    <label class="text-primary-content/80 mb-1 block text-sm">Source Type</label>
                    <select
                        v-model="form.sourceType"
                        class="bg-secondary border-accent/20 text-primary-content w-full rounded-md border px-3 py-2">
                        <option value="MovieRoot">Movie root</option>
                        <option value="ShowRoot">Show root</option>
                    </select>
                </div>

                <div>
                    <label class="text-primary-content/80 mb-1 block text-sm">Mounted Root Path</label>
                    <div class="flex gap-2">
                        <input
                            v-model="form.rootPath"
                            class="bg-secondary border-accent/20 text-primary-content min-w-0 flex-1 rounded-md border px-3 py-2"
                            type="text" />
                        <button
                            class="bg-secondary border-accent/20 text-primary-content rounded-md border px-3 py-2"
                            @click="isDirectoryModalOpen = true">
                            Browse
                        </button>
                    </div>
                </div>

                <label class="flex items-center gap-2 text-sm">
                    <input v-model="form.recursive" type="checkbox" />
                    <span>Scan recursively</span>
                </label>

                <label class="flex items-center gap-2 text-sm">
                    <input v-model="form.enabled" type="checkbox" />
                    <span>Enable source</span>
                </label>

                <label class="flex items-center gap-2 text-sm">
                    <input v-model="form.includeInAutomation" type="checkbox" />
                    <span>Include in automation</span>
                </label>

                <div class="flex gap-2 pt-2">
                    <button
                        class="bg-accent text-secondary-content rounded-md px-3 py-2 text-sm font-medium"
                        :disabled="saving"
                        @click="saveSource">
                        {{ selectedSourceId ? 'Update Source' : 'Create Source' }}
                    </button>
                    <button
                        class="bg-secondary border-accent/20 rounded-md border px-3 py-2 text-sm"
                        @click="resetForm">
                        Clear
                    </button>
                </div>

                <p v-if="errorMessage" class="text-sm text-red-400">{{ errorMessage }}</p>
            </div>
        </CardComponent>

        <div class="space-y-4">
            <CardComponent title="Registered Sources">
                <template #actions>
                    <button
                        class="bg-secondary border-accent/20 rounded-md border px-3 py-2 text-sm"
                        :disabled="loading"
                        @click="rescanAllSources">
                        Rescan Enabled
                    </button>
                </template>

                <div v-if="sources.length === 0" class="text-primary-content/60 text-sm">
                    No custom sources registered yet.
                </div>

                <div v-else class="space-y-3">
                    <button
                        v-for="source in sources"
                        :key="source.id"
                        class="bg-secondary border-accent/20 hover:border-accent/50 w-full rounded-md border p-3 text-left transition-colors"
                        :class="{ 'border-accent': selectedSourceId === source.id }"
                        @click="selectSource(source)">
                        <div class="flex items-start justify-between gap-3">
                            <div class="min-w-0">
                                <div class="flex items-center gap-2">
                                    <FoldersIcon class="text-accent h-4 w-4 shrink-0" />
                                    <span class="truncate font-medium">{{ source.name }}</span>
                                </div>
                                <p class="text-primary-content/60 mt-1 truncate text-sm">
                                    {{ source.rootPath }}
                                </p>
                                <p class="text-primary-content/60 mt-1 text-xs">
                                    {{ source.sourceType }} • {{ source.items?.length || 0 }} items
                                </p>
                            </div>

                            <div class="flex shrink-0 gap-2">
                                <button class="text-sm text-blue-300" @click.stop="editSource(source)">
                                    Edit
                                </button>
                                <button
                                    class="text-sm text-orange-300"
                                    @click.stop="rescanSource(source.id)">
                                    Scan
                                </button>
                                <button
                                    class="text-sm text-red-300"
                                    @click.stop="deleteSource(source.id)">
                                    Delete
                                </button>
                            </div>
                        </div>
                        <p v-if="source.lastScanError" class="mt-2 text-xs text-red-400">
                            {{ source.lastScanError }}
                        </p>
                        <p v-else-if="source.lastScanResult" class="text-primary-content/60 mt-2 text-xs">
                            {{ source.lastScanResult }}
                        </p>
                    </button>
                </div>
            </CardComponent>

            <CardComponent :title="selectedSourceTitle">
                <template #description>
                    Review indexed items, then mark them as excluded or priority without touching
                    your Arr-backed libraries.
                </template>

                <div v-if="selectedItems.length === 0" class="text-primary-content/60 text-sm">
                    Select a source to inspect its indexed items.
                </div>

                <div v-else class="space-y-2">
                    <div
                        v-for="item in selectedItems"
                        :key="item.id"
                        class="bg-secondary border-accent/10 rounded-md border p-3">
                        <div class="flex items-start justify-between gap-3">
                            <div class="min-w-0">
                                <p class="truncate font-medium">{{ item.title }}</p>
                                <p class="text-primary-content/60 truncate text-xs">
                                    {{ item.relativePath }}
                                </p>
                                <p
                                    v-if="item.seriesTitle"
                                    class="text-primary-content/60 truncate text-xs">
                                    {{ item.seriesTitle }}
                                    <span v-if="item.seasonNumber && item.episodeNumber">
                                        • S{{ String(item.seasonNumber).padStart(2, '0') }}E{{
                                            String(item.episodeNumber).padStart(2, '0')
                                        }}
                                    </span>
                                </p>
                            </div>

                            <div class="flex shrink-0 gap-2">
                                <button class="text-xs text-emerald-300" @click="translateItem(item)">
                                    Translate
                                </button>
                                <button
                                    class="text-xs"
                                    :class="
                                        item.excludeFromTranslation ? 'text-orange-300' : 'text-blue-300'
                                    "
                                    @click="toggleExcluded(item)">
                                    {{ item.excludeFromTranslation ? 'Include' : 'Exclude' }}
                                </button>
                                <button
                                    class="text-xs"
                                    :class="item.isPriority ? 'text-yellow-300' : 'text-blue-300'"
                                    @click="togglePriority(item)">
                                    {{ item.isPriority ? 'Unpriority' : 'Priority' }}
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </CardComponent>
        </div>
    </div>

    <DirectoryModal
        :is-open="isDirectoryModalOpen"
        @close="isDirectoryModalOpen = false"
        @select="handleDirectorySelection" />
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import services from '@/services'
import { ICustomMediaItem, ICustomSource } from '@/ts'
import CardComponent from '@/components/common/CardComponent.vue'
import DirectoryModal from '@/components/features/settings/DirectoryModal.vue'
import FoldersIcon from '@/components/icons/FoldersIcon.vue'

interface CustomSourceForm {
    name: string
    sourceType: 'MovieRoot' | 'ShowRoot'
    rootPath: string
    recursive: boolean
    enabled: boolean
    includeInAutomation: boolean
}

const loading = ref(false)
const saving = ref(false)
const errorMessage = ref('')
const isDirectoryModalOpen = ref(false)
const selectedSourceId = ref<number | null>(null)
const sources = ref<ICustomSource[]>([])
const selectedItems = ref<ICustomMediaItem[]>([])

const defaultForm = (): CustomSourceForm => ({
    name: '',
    sourceType: 'MovieRoot',
    rootPath: '',
    recursive: true,
    enabled: true,
    includeInAutomation: true
})

const form = ref<CustomSourceForm>(defaultForm())

const selectedSourceTitle = computed(() => {
    const source = sources.value.find((item) => item.id === selectedSourceId.value)
    return source ? `${source.name} Items` : 'Indexed Items'
})

const loadSources = async () => {
    loading.value = true
    try {
        sources.value = await services.customSources.getSources()
        if (selectedSourceId.value) {
            await loadItems(selectedSourceId.value)
        }
    } finally {
        loading.value = false
    }
}

const loadItems = async (sourceId: number) => {
    selectedItems.value = await services.customSources.getItems(sourceId)
}

const selectSource = async (source: ICustomSource) => {
    selectedSourceId.value = source.id
    await loadItems(source.id)
}

const editSource = async (source: ICustomSource) => {
    selectedSourceId.value = source.id
    form.value = {
        name: source.name,
        sourceType: source.sourceType,
        rootPath: source.rootPath,
        recursive: source.recursive,
        enabled: source.enabled,
        includeInAutomation: source.includeInAutomation
    }
    await loadItems(source.id)
}

const resetForm = () => {
    selectedSourceId.value = null
    form.value = defaultForm()
    errorMessage.value = ''
}

const saveSource = async () => {
    saving.value = true
    errorMessage.value = ''

    try {
        if (selectedSourceId.value) {
            await services.customSources.updateSource(selectedSourceId.value, form.value)
        } else {
            await services.customSources.createSource(form.value)
        }

        await loadSources()
        if (!selectedSourceId.value && sources.value.length > 0) {
            await selectSource(sources.value[0])
        }
        resetForm()
    } catch (error) {
        errorMessage.value = 'Unable to save the custom source. Check the mounted path and try again.'
        console.error(error)
    } finally {
        saving.value = false
    }
}

const deleteSource = async (sourceId: number) => {
    await services.customSources.deleteSource(sourceId)
    if (selectedSourceId.value === sourceId) {
        selectedSourceId.value = null
        selectedItems.value = []
    }
    await loadSources()
}

const rescanSource = async (sourceId: number) => {
    await services.customSources.rescan(sourceId)
    await loadSources()
    if (selectedSourceId.value === sourceId) {
        await loadItems(sourceId)
    }
}

const rescanAllSources = async () => {
    await services.customSources.rescanAll()
    await loadSources()
}

const toggleExcluded = async (item: ICustomMediaItem) => {
    await services.customSources.setExcluded(item.id, !item.excludeFromTranslation)
    if (selectedSourceId.value) {
        await loadItems(selectedSourceId.value)
    }
}

const togglePriority = async (item: ICustomMediaItem) => {
    await services.customSources.setPriority(item.id, !item.isPriority)
    if (selectedSourceId.value) {
        await loadItems(selectedSourceId.value)
    }
}

const translateItem = async (item: ICustomMediaItem) => {
    await services.customSources.translate(item.id)
    if (selectedSourceId.value) {
        await loadItems(selectedSourceId.value)
    }
}

const handleDirectorySelection = (path: string) => {
    form.value.rootPath = path
    isDirectoryModalOpen.value = false
}

onMounted(async () => {
    await loadSources()
    if (sources.value.length > 0) {
        await selectSource(sources.value[0])
    }
})
</script>

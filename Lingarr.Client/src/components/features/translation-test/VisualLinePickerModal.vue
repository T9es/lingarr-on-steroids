<template>
    <ModalComponent
        :is-open="true"
        size="xxl"
        :body-scrollable="false"
        body-class="p-0"
        @close="$emit('close')">
        <template #header>
            {{ translate('translationTest.selectLines') }}
        </template>

        <div class="flex h-[75vh] flex-col">
            <div class="border-secondary/30 flex items-center justify-between border-b px-4 py-3">
                <div class="flex items-center gap-4">
                    <span class="text-secondary-content text-sm">
                        {{
                            t(
                                'translationTest.selectedLinesSummary',
                                `${selectedCount} lines selected`,
                                { count: selectedCount }
                            )
                        }}
                    </span>
                    <span class="text-secondary-content text-xs">
                        {{
                            t(
                                'translationTest.multiSelectHint',
                                'Click to select, Ctrl+click to toggle, Shift+click to add a range'
                            )
                        }}
                    </span>
                </div>
                <div class="flex gap-2">
                    <button
                        @click="clearSelection"
                        class="text-secondary-content text-sm hover:underline">
                        {{ translate('translationTest.clearSelection') }}
                    </button>
                    <button @click="selectAll" class="text-accent text-sm hover:underline">
                        {{ translate('translationTest.selectAll') }}
                    </button>
                </div>
            </div>

            <div v-if="loading" class="flex h-full items-center justify-center px-6 text-center">
                <div class="space-y-3">
                    <div class="border-accent mx-auto h-10 w-10 animate-spin rounded-full border-2 border-t-transparent"></div>
                    <p class="text-secondary-content text-sm">
                        {{ loadingLabel }}
                    </p>
                </div>
            </div>

            <div
                v-else-if="error"
                class="text-error flex h-full items-center justify-center px-6 text-center text-sm">
                {{ error }}
            </div>

            <template v-else>
                <div class="border-secondary/20 bg-primary/40 grid grid-cols-[4rem_7rem_minmax(0,1fr)] gap-3 border-b px-4 py-2 text-[11px] uppercase tracking-[0.2em]">
                    <span class="text-secondary-content/70 text-right">#</span>
                    <span class="text-secondary-content/70">
                        {{ t('translationTest.time', 'Time') }}
                    </span>
                    <span class="text-secondary-content/70">
                        {{ translate('translationTest.original') }}
                    </span>
                </div>

                <div class="flex-1 overflow-y-auto font-mono text-xs">
                    <div
                        v-for="line in lines"
                        :key="line.position"
                        @click="toggleLine(line.position, $event)"
                        :class="{
                            'bg-accent/20': isSelected(line.position),
                            'hover:bg-tertiary/70': !isSelected(line.position)
                        }"
                        class="border-secondary/10 grid cursor-pointer grid-cols-[4rem_7rem_minmax(0,1fr)] gap-3 border-b px-4 py-2 transition">
                        <span class="text-secondary-content/70 text-right">
                            {{ line.position }}
                        </span>
                        <span class="text-secondary-content/70">
                            {{ line.startTime }}
                        </span>
                        <span class="text-primary-content whitespace-pre-wrap break-words">
                            {{ line.text }}
                        </span>
                    </div>
                </div>
            </template>
        </div>

        <template #footer>
            <button
                @click="$emit('close')"
                class="text-primary-content/60 hover:text-primary-content rounded px-4 py-2">
                {{ translate('common.cancel') }}
            </button>
            <button
                @click="confirmSelection"
                :disabled="selectedCount === 0"
                class="bg-accent text-primary-content rounded px-4 py-2 font-medium disabled:opacity-50">
                {{ t('translationTest.confirmSelection', `Confirm (${selectedCount})`, { count: selectedCount }) }}
            </button>
        </template>
    </ModalComponent>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from '@/plugins/i18n'
import ModalComponent from '@/components/common/ModalComponent.vue'

interface SubtitleLine {
    position: number
    startTime: string
    endTime: string
    text: string
}

const props = defineProps<{
    subtitlePath: string
    selectedPositions?: number[]
    mediaId?: number
    mediaType?: 'Movie' | 'Episode'
    streamIndex?: number
    language?: string
}>()

const emit = defineEmits<{
    select: [positions: number[]]
    close: []
}>()

const { translate } = useI18n()

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}

const lines = ref<SubtitleLine[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const selectedPositions = ref<number[]>([])
const lastClicked = ref<number | null>(null)

const orderedPositions = computed(() => lines.value.map((line) => line.position))
const selectedCount = computed(() => selectedPositions.value.length)
const loadingLabel = computed(() =>
    props.streamIndex !== undefined
        ? t('translationTest.extractingEmbeddedSubtitle', 'Extracting embedded subtitle...')
        : translate('translationTest.loadingSubtitle')
)

function isSelected(position: number): boolean {
    return selectedPositions.value.includes(position)
}

function setSelectedPositions(values: number[]) {
    const selected = new Set(values)
    selectedPositions.value = orderedPositions.value.filter((position) => selected.has(position))
}

function toggleLine(position: number, event: MouseEvent) {
    if (event.shiftKey && lastClicked.value !== null) {
        const startIndex = orderedPositions.value.indexOf(lastClicked.value)
        const endIndex = orderedPositions.value.indexOf(position)

        if (startIndex >= 0 && endIndex >= 0) {
            const [from, to] =
                startIndex < endIndex ? [startIndex, endIndex] : [endIndex, startIndex]
            const range = orderedPositions.value.slice(from, to + 1)
            setSelectedPositions([...selectedPositions.value, ...range])
        }
    } else if (event.ctrlKey || event.metaKey) {
        if (isSelected(position)) {
            setSelectedPositions(selectedPositions.value.filter((item) => item !== position))
        } else {
            setSelectedPositions([...selectedPositions.value, position])
        }
    } else {
        setSelectedPositions([position])
    }

    lastClicked.value = position
}

function clearSelection() {
    selectedPositions.value = []
    lastClicked.value = null
}

function selectAll() {
    selectedPositions.value = [...orderedPositions.value]
    lastClicked.value = orderedPositions.value[0] ?? null
}

function confirmSelection() {
    emit('select', selectedPositions.value)
}

async function loadSubtitle() {
    try {
        loading.value = true
        error.value = null

        let response: Response

        if (props.mediaId && props.mediaType && props.streamIndex !== undefined) {
            const params = new URLSearchParams({
                mediaId: props.mediaId.toString(),
                mediaType: props.mediaType,
                streamIndex: props.streamIndex.toString()
            })

            if (props.language) {
                params.append('language', props.language)
            }

            response = await fetch(`/api/test-translation/embedded-preview?${params}`)
        } else {
            response = await fetch(
                `/api/test-translation/subtitle-preview?path=${encodeURIComponent(props.subtitlePath)}`
            )
        }

        if (!response.ok) {
            const payload = await response.json().catch(() => null)
            throw new Error(payload?.message || payload?.Message || 'Failed to load subtitle')
        }

        const data = await response.json()
        lines.value = data.lines || []

        const initialPositions =
            props.selectedPositions && props.selectedPositions.length > 0
                ? props.selectedPositions
                : lines.value.slice(0, Math.min(20, lines.value.length)).map((line) => line.position)

        setSelectedPositions(initialPositions)
        lastClicked.value = selectedPositions.value[0] ?? null
    } catch (err) {
        console.error('Failed to load subtitle:', err)
        error.value = err instanceof Error ? err.message : 'Unknown error loading subtitle'
    } finally {
        loading.value = false
    }
}

onMounted(loadSubtitle)
</script>

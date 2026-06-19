<template>
    <div class="flex h-[75vh] flex-col">
        <div class="border-secondary/30 flex items-center justify-between border-b px-4 py-3">
            <div class="flex items-center gap-4">
                <span class="text-secondary-content text-sm">
                    {{
                        t(
                            'translationTest.selectedLinesSummary',
                            `${selectedCount} lines selected`,
                            {
                                count: selectedCount
                            }
                        )
                    }}
                </span>
                <span v-if="missingCount > 0" class="text-secondary-content text-sm">
                    {{ missingCount }} missing
                </span>
                <span class="text-secondary-content text-xs">
                    {{
                        t(
                            'translationTest.compareSelectionHint',
                            'Click to focus a line, Ctrl+click to compare multiple, Shift+click to extend'
                        )
                    }}
                </span>
            </div>
            <div class="flex items-center gap-2">
                <button
                    v-if="missingCount > 0"
                    @click="jumpToNextMissing"
                    class="rounded bg-yellow-500/15 px-2 py-1 text-xs text-yellow-400 hover:bg-yellow-500/25">
                    {{ t('translationTest.nextMissing', 'Next Missing') }}
                </button>
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

        <div
            v-if="pageCount > 1"
            class="border-secondary/20 bg-primary/30 flex items-center justify-between border-b px-4 py-2 text-xs">
            <span class="text-secondary-content">
                {{ visibleStart }}-{{ visibleEnd }} / {{ lines.length }}
            </span>
            <div class="flex items-center gap-2">
                <button
                    @click="goToPage(currentPage - 1)"
                    :disabled="currentPage === 1"
                    class="text-secondary-content hover:text-primary-content disabled:text-secondary-content/40 rounded px-2 py-1">
                    {{ t('common.previous', 'Previous') }}
                </button>
                <span class="text-secondary-content">{{ currentPage }} / {{ pageCount }}</span>
                <button
                    @click="goToPage(currentPage + 1)"
                    :disabled="currentPage === pageCount"
                    class="text-secondary-content hover:text-primary-content disabled:text-secondary-content/40 rounded px-2 py-1">
                    {{ t('common.next', 'Next') }}
                </button>
            </div>
        </div>

        <div
            class="border-secondary/20 bg-primary/40 grid grid-cols-[4rem_7rem_minmax(0,1fr)_minmax(0,1fr)_5rem] gap-3 border-b px-4 py-2 text-[11px] tracking-[0.2em] uppercase">
            <span class="text-secondary-content/70 text-right">#</span>
            <span class="text-secondary-content/70">
                {{ t('translationTest.time', 'Time') }}
            </span>
            <span class="text-secondary-content/70">
                {{ translate('translationTest.original') }}
            </span>
            <span class="text-secondary-content/70">
                {{ translate('translationTest.translated') }}
            </span>
            <span class="text-secondary-content/70 text-right">ms</span>
        </div>

        <div class="flex-1 overflow-y-auto font-mono text-xs">
            <div
                v-for="(line, index) in visibleLines"
                :key="`${line.position}-${pageStartIndex + index}`"
                :ref="(el) => setRowRef(line.position, el)"
                @click="toggleLine(line.position, $event)"
                :class="{
                    'bg-accent/20': isSelected(line.position),
                    'bg-yellow-500/10': isMissing(line.position),
                    'hover:bg-tertiary/70': !isSelected(line.position) && !isMissing(line.position)
                }"
                class="border-secondary/10 grid cursor-pointer grid-cols-[4rem_7rem_minmax(0,1fr)_minmax(0,1fr)_5rem] gap-3 border-b px-4 py-2 transition">
                <span class="text-secondary-content/70 text-right">
                    {{ line.position }}
                </span>
                <span class="text-secondary-content/70">
                    {{ formatTimestamp(line.startTimeMs) }}
                </span>
                <span class="text-primary-content break-words whitespace-pre-wrap">
                    {{ line.original }}
                </span>
                <span
                    class="break-words whitespace-pre-wrap"
                    :class="line.success ? 'text-primary-content' : 'text-error'">
                    <template v-if="editingPosition === line.position">
                        <textarea
                            v-model="editingText"
                            @blur="finishEditing"
                            @keydown.ctrl.enter="finishEditing"
                            @keydown.escape="cancelEditing"
                            @click.stop
                            class="w-full resize-none bg-transparent font-mono text-xs focus:outline-none"
                            rows="2"
                        />
                    </template>
                    <template v-else>
                        <span
                            v-if="props.editable && line.canEdit"
                            @click.stop="startEditing(line)"
                            class="cursor-pointer hover:underline">
                            {{ line.translated || line.error || '-' }}
                        </span>
                        <span v-else>
                            {{ line.translated || line.error || '-' }}
                        </span>
                    </template>
                    <span
                        v-if="isMissing(line.position)"
                        class="ml-2 inline-block rounded bg-yellow-500/15 px-2 py-0.5 text-xs text-yellow-400">
                        Missing
                    </span>
                </span>
                <span class="text-secondary-content/70 text-right">
                    {{ line.durationMs?.toFixed(0) ?? '-' }}
                </span>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, nextTick } from 'vue'
import { useI18n } from '@/plugins/i18n'
import type { TranslationCompareLine } from '@/ts/translationCompare'

interface Props {
    lines: TranslationCompareLine[]
    editable?: boolean        // default false — enable inline editing
    missingPositions?: number[]  // positions flagged as missing/untranslated
}

const props = withDefaults(defineProps<Props>(), {
    editable: false,
    missingPositions: () => []
})

const emit = defineEmits<{
    'edit-line': [payload: { position: number; translatedText: string }]
}>()

const { translate } = useI18n()
const pageSize = 150

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}

const selectedPositions = ref<Set<number>>(new Set())
const lastClicked = ref<number | null>(null)
const currentPage = ref(1)
const editingPosition = ref<number | null>(null)
const editingText = ref('')
const missingPositions = ref<Set<number>>(new Set())
const rowRefs = ref<Map<number, HTMLElement>>(new Map())

watch(
    () => props.missingPositions,
    (positions) => {
        missingPositions.value = new Set(positions ?? [])
    },
    { immediate: true }
)

const orderedPositions = computed(() => props.lines.map((line) => line.position))
const pageCount = computed(() => Math.max(1, Math.ceil(props.lines.length / pageSize)))
const pageStartIndex = computed(() => (currentPage.value - 1) * pageSize)
const pageEndIndex = computed(() => Math.min(pageStartIndex.value + pageSize, props.lines.length))
const visibleLines = computed(() => props.lines.slice(pageStartIndex.value, pageEndIndex.value))
const visibleStart = computed(() => (props.lines.length === 0 ? 0 : pageStartIndex.value + 1))
const visibleEnd = computed(() => pageEndIndex.value)

watch(
    () => props.lines,
    (lines) => {
        const firstPosition = lines[0]?.position ?? null

        currentPage.value = 1
        selectedPositions.value = firstPosition === null ? new Set() : new Set([firstPosition])
        lastClicked.value = firstPosition
    },
    { immediate: true }
)

watch(pageCount, (count) => {
    if (currentPage.value > count) {
        currentPage.value = count
    }
})

const selectedCount = computed(() => selectedPositions.value.size)
const missingCount = computed(() => missingPositions.value.size)

function isSelected(position: number): boolean {
    return selectedPositions.value.has(position)
}

function isMissing(position: number): boolean {
    return missingPositions.value.has(position)
}

function setRowRef(position: number, el: unknown) {
    if (el) {
        rowRefs.value.set(position, el as HTMLElement)
    } else {
        rowRefs.value.delete(position)
    }
}

function toggleLine(position: number, event: MouseEvent) {
    if (event.shiftKey && lastClicked.value !== null) {
        const startIndex = orderedPositions.value.indexOf(lastClicked.value)
        const endIndex = orderedPositions.value.indexOf(position)

        if (startIndex >= 0 && endIndex >= 0) {
            const [from, to] =
                startIndex < endIndex ? [startIndex, endIndex] : [endIndex, startIndex]
            const range = orderedPositions.value.slice(from, to + 1)
            const merged = new Set(selectedPositions.value)
            range.forEach((item) => merged.add(item))
            selectedPositions.value = merged
        }
    } else if (event.ctrlKey || event.metaKey) {
        const updated = new Set(selectedPositions.value)

        if (isSelected(position)) {
            updated.delete(position)
        } else {
            updated.add(position)
        }

        selectedPositions.value = updated
    } else {
        selectedPositions.value = new Set([position])
    }

    lastClicked.value = position
}

function clearSelection() {
    selectedPositions.value = new Set()
    lastClicked.value = null
}

function startEditing(line: TranslationCompareLine) {
    if (!props.editable || !line.canEdit) return
    editingPosition.value = line.position
    editingText.value = line.translated || ''
}

function finishEditing() {
    if (editingPosition.value === null) return
    emit('edit-line', {
        position: editingPosition.value,
        translatedText: editingText.value
    })
    editingPosition.value = null
    editingText.value = ''
}

function cancelEditing() {
    editingPosition.value = null
    editingText.value = ''
}

function jumpToNextMissing() {
    if (missingPositions.value.size === 0) return

    const sortedMissing = Array.from(missingPositions.value).sort((a, b) => a - b)
    const lastPos = lastClicked.value ?? -1

    let nextPos = sortedMissing.find((pos) => pos > lastPos)
    if (nextPos === undefined) {
        nextPos = sortedMissing[0] // wrap around
    }

    // Find the page for this position
    const lineIndex = props.lines.findIndex((line) => line.position === nextPos)
    if (lineIndex === -1) return

    const targetPage = Math.floor(lineIndex / pageSize) + 1
    currentPage.value = targetPage

    // Scroll into view after nextTick
    nextTick(() => {
        const rowEl = rowRefs.value.get(nextPos!)
        if (rowEl) {
            rowEl.scrollIntoView({ behavior: 'smooth', block: 'center' })
        }
    })

    // Update selection
    selectedPositions.value = new Set([nextPos])
    lastClicked.value = nextPos
}

function selectAll() {
    selectedPositions.value = new Set(orderedPositions.value)
    lastClicked.value = orderedPositions.value[0] ?? null
}

function goToPage(page: number) {
    currentPage.value = Math.min(Math.max(page, 1), pageCount.value)
}

function formatTimestamp(milliseconds?: number): string {
    if (milliseconds === undefined) {
        return '--:--:--'
    }

    const totalSeconds = Math.floor(milliseconds / 1000)
    const hours = Math.floor(totalSeconds / 3600)
    const minutes = Math.floor((totalSeconds % 3600) / 60)
    const seconds = totalSeconds % 60

    return [hours, minutes, seconds].map((value) => value.toString().padStart(2, '0')).join(':')
}
</script>

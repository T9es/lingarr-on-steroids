<template>
    <ModalComponent
        :is-open="isOpen"
        size="xxl"
        :body-scrollable="false"
        body-class="p-0"
        @close="$emit('close')">
        <template #header>
            {{ t('translationTest.debugPanel.compareView', 'Side-by-Side Compare') }}
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
                                'translationTest.compareSelectionHint',
                                'Click to focus a line, Ctrl+click to compare multiple, Shift+click to extend'
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
                    v-for="line in lines"
                    :key="line.position"
                    @click="toggleLine(line.position, $event)"
                    :class="{
                        'bg-accent/20': isSelected(line.position),
                        'hover:bg-tertiary/70': !isSelected(line.position)
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
                        {{ line.translated || line.error || '-' }}
                    </span>
                    <span class="text-secondary-content/70 text-right">
                        {{ line.durationMs?.toFixed(0) ?? '-' }}
                    </span>
                </div>
            </div>
        </div>

        <template #footer>
            <button
                @click="$emit('close')"
                class="text-primary-content/60 hover:text-primary-content rounded px-4 py-2">
                {{ translate('common.close') }}
            </button>
        </template>
    </ModalComponent>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from '@/plugins/i18n'
import ModalComponent from '@/components/common/ModalComponent.vue'

export interface CompareLineResult {
    position: number
    original: string
    translated?: string
    success: boolean
    error?: string
    durationMs?: number
    startTimeMs?: number
    endTimeMs?: number
}

const props = defineProps<{
    isOpen: boolean
    lines: CompareLineResult[]
}>()

defineEmits<{
    close: []
}>()

const { translate } = useI18n()

function t(key: string, fallback: string, params?: Record<string, string | number>): string {
    const value = translate(key, params)
    return value === key ? fallback : value
}

const selectedPositions = ref<number[]>([])
const lastClicked = ref<number | null>(null)

watch(
    () => props.isOpen,
    (isOpen) => {
        if (isOpen) {
            selectedPositions.value = props.lines.map((line) => line.position)
            lastClicked.value = selectedPositions.value[0] ?? null
        }
    },
    { immediate: true }
)

const selectedCount = computed(() => selectedPositions.value.length)

function isSelected(position: number): boolean {
    return selectedPositions.value.includes(position)
}

function toggleLine(position: number, event: MouseEvent) {
    const orderedPositions = props.lines.map((line) => line.position)

    if (event.shiftKey && lastClicked.value !== null) {
        const startIndex = orderedPositions.indexOf(lastClicked.value)
        const endIndex = orderedPositions.indexOf(position)

        if (startIndex >= 0 && endIndex >= 0) {
            const [from, to] =
                startIndex < endIndex ? [startIndex, endIndex] : [endIndex, startIndex]
            const range = orderedPositions.slice(from, to + 1)
            const merged = new Set([...selectedPositions.value, ...range])
            selectedPositions.value = orderedPositions.filter((item) => merged.has(item))
        }
    } else if (event.ctrlKey || event.metaKey) {
        if (isSelected(position)) {
            selectedPositions.value = selectedPositions.value.filter((item) => item !== position)
        } else {
            const merged = new Set([...selectedPositions.value, position])
            selectedPositions.value = orderedPositions.filter((item) => merged.has(item))
        }
    } else {
        selectedPositions.value = [position]
    }

    lastClicked.value = position
}

function clearSelection() {
    selectedPositions.value = []
    lastClicked.value = null
}

function selectAll() {
    selectedPositions.value = props.lines.map((line) => line.position)
    lastClicked.value = selectedPositions.value[0] ?? null
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

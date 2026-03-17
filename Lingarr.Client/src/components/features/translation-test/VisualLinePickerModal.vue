<template>
    <ModalComponent :is-open="true" size="lg" @close="$emit('close')">
        <template #header>
            {{ translate('translationTest.selectLines') }}
        </template>

        <div class="space-y-2">
            <div class="border-secondary/30 flex items-center justify-between border-b pb-2">
                <div class="flex items-center gap-4">
                    <span class="text-secondary-content text-sm">
                        {{ selectedCount }} {{ translate('translationTest.linesSelected') }} ({{
                            startLine
                        }}-{{ endLine }})
                    </span>
                    <span class="text-secondary-content text-xs">
                        {{ translate('translationTest.shiftClickHint') }}
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

            <div v-if="loading" class="flex h-64 items-center justify-center">
                <span class="text-secondary-content">
                    {{ translate('translationTest.loadingSubtitle') }}
                </span>
            </div>

            <div v-else-if="error" class="flex h-64 items-center justify-center">
                <span class="text-error text-sm">
                    {{ error }}
                </span>
            </div>

            <div
                v-else
                ref="scrollContainer"
                class="max-h-[70vh] overflow-y-auto font-mono text-xs"
                @scroll="onScroll">
                <div
                    v-for="line in visibleLines"
                    :key="line.position"
                    @click="toggleLine(line.position, $event)"
                    :class="{
                        'bg-accent/20': isInRange(line.position),
                        'bg-primary hover:bg-accent/10': !isInRange(line.position)
                    }"
                    class="flex cursor-pointer gap-3 px-2 py-1 transition">
                    <span class="text-secondary-content/60 w-8 text-right">
                        {{ line.position }}
                    </span>
                    <span class="text-secondary-content/60 w-20">
                        {{ line.startTime }}
                    </span>
                    <span class="text-primary-content flex-1 whitespace-pre-wrap">
                        {{ line.text }}
                    </span>
                </div>
            </div>
        </div>

        <template #footer>
            <button
                @click="$emit('close')"
                class="text-primary-content/60 hover:text-primary-content rounded px-4 py-2">
                {{ translate('common.cancel') }}
            </button>
            <button
                @click="confirmSelection"
                class="bg-accent text-primary-content rounded px-4 py-2 font-medium">
                {{ translate('translationTest.confirm') }} ({{ selectedCount }})
            </button>
        </template>
    </ModalComponent>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
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
    selectedStart?: number
    selectedEnd?: number
    mediaId?: number
    mediaType?: 'Movie' | 'Episode'
    streamIndex?: number
    language?: string
}>()

const emit = defineEmits<{
    select: [start: number, end: number]
    close: []
}>()

const { translate } = useI18n()

const lines = ref<SubtitleLine[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const startLine = ref(props.selectedStart ?? 1)
const endLine = ref(props.selectedEnd ?? 1)
const lastClicked = ref<number | null>(null)
const scrollContainer = ref<HTMLElement | null>(null)
const scrollTop = ref(0)
const lineHeight = 24
const visibleCount = 50

const visibleLines = computed(() => {
    const start = Math.floor(scrollTop.value / lineHeight)
    const end = Math.min(start + visibleCount, lines.value.length)
    return lines.value.slice(start, end)
})

const selectedCount = computed(() => endLine.value - startLine.value + 1)

function isInRange(position: number): boolean {
    return position >= startLine.value && position <= endLine.value
}

function toggleLine(position: number, event: MouseEvent) {
    if (event.shiftKey && lastClicked.value !== null) {
        startLine.value = Math.min(lastClicked.value, position)
        endLine.value = Math.max(lastClicked.value, position)
    } else if (isInRange(position) && startLine.value !== endLine.value) {
        startLine.value = position
        endLine.value = position
    } else if (position < startLine.value || position > endLine.value) {
        if (position < startLine.value) startLine.value = position
        else endLine.value = position
    }
    lastClicked.value = position
}

function clearSelection() {
    startLine.value = 1
    endLine.value = 1
    lastClicked.value = null
}

function selectAll() {
    startLine.value = 1
    endLine.value = lines.value.length
}

function onScroll() {
    if (scrollContainer.value) {
        scrollTop.value = scrollContainer.value.scrollTop
    }
}

function confirmSelection() {
    emit('select', startLine.value, endLine.value)
}

async function loadSubtitle() {
    try {
        loading.value = true
        error.value = null
        let data

        if (props.mediaId && props.mediaType && props.streamIndex !== undefined) {
            const params = new URLSearchParams({
                mediaId: props.mediaId.toString(),
                mediaType: props.mediaType,
                streamIndex: props.streamIndex.toString()
            })
            if (props.language) params.append('language', props.language)

            const response = await fetch(`/api/test-translation/embedded-preview?${params}`)
            if (!response.ok) {
                if (response.status === 400) {
                    const err = await response.json()
                    throw new Error(
                        err.message || 'Cannot extract this subtitle (may be image-based)'
                    )
                }
                throw new Error('Failed to extract embedded subtitle')
            }
            data = await response.json()
        } else {
            const response = await fetch(
                `/api/test-translation/subtitle-preview?path=${encodeURIComponent(props.subtitlePath)}`
            )
            if (!response.ok) throw new Error('Failed to load subtitle')
            data = await response.json()
        }

        lines.value = data.lines
        if (lines.value.length > 0) {
            endLine.value = Math.min(props.selectedEnd ?? 20, lines.value.length)
        }
    } catch (err) {
        console.error('Failed to load subtitle:', err)
        error.value = err instanceof Error ? err.message : 'Unknown error loading subtitle'
    } finally {
        loading.value = false
    }
}

onMounted(loadSubtitle)
</script>

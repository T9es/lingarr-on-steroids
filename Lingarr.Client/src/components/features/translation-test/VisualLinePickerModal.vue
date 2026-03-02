<template>
    <ModalComponent :is-open="true" @close="$emit('close')">
        <template #header>
            {{ translate('translationTest.selectLines') }}
        </template>

        <div class="space-y-2">
            <div class="flex items-center justify-between border-b border-secondary/30 pb-2">
                <span class="text-secondary-content text-sm">
                    {{ selectedCount }} {{ translate('translationTest.linesSelected') }}
                    ({{ startLine }}-{{ endLine }})
                </span>
                <button @click="selectAll" class="text-accent text-sm hover:underline">
                    {{ translate('translationTest.selectAll') }}
                </button>
            </div>

            <div v-if="loading" class="flex h-64 items-center justify-center">
                <span class="text-secondary-content">
                    {{ translate('translationTest.loadingSubtitle') }}
                </span>
            </div>

            <div
                v-else
                ref="scrollContainer"
                class="h-80 overflow-y-auto font-mono text-xs"
                @scroll="onScroll">
                <div
                    v-for="line in visibleLines"
                    :key="line.position"
                    @click="toggleLine(line.position)"
                    :class="{
                        'bg-accent/20': isInRange(line.position),
                        'bg-primary hover:bg-accent/10': !isInRange(line.position)
                    }"
                    class="flex cursor-pointer gap-3 px-2 py-1 transition">
                    <span class="w-8 text-right text-secondary-content/60">
                        {{ line.position }}
                    </span>
                    <span class="w-20 text-secondary-content/60">
                        {{ line.startTime }}
                    </span>
                    <span class="flex-1 whitespace-pre-wrap">{{ line.text }}</span>
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
}>()

const emit = defineEmits<{
    select: [start: number, end: number]
    close: []
}>()

const { translate } = useI18n()

const lines = ref<SubtitleLine[]>([])
const loading = ref(true)
const startLine = ref(props.selectedStart ?? 1)
const endLine = ref(props.selectedEnd ?? 1)
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

function toggleLine(position: number) {
    if (position < startLine.value) {
        startLine.value = position
    } else if (position > endLine.value) {
        endLine.value = position
    } else if (startLine.value === endLine.value) {
        endLine.value = position
    }
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
        const response = await fetch(
            `/api/test-translation/subtitle-preview?path=${encodeURIComponent(props.subtitlePath)}`
        )
        if (!response.ok) throw new Error('Failed to load subtitle')
        const data = await response.json()
        lines.value = data.lines
        if (lines.value.length > 0) {
            endLine.value = Math.min(props.selectedEnd ?? 20, lines.value.length)
        }
    } catch (error) {
        console.error('Failed to load subtitle:', error)
    } finally {
        loading.value = false
    }
}

onMounted(loadSubtitle)
</script>
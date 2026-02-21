<template>
    <div
        :class="[
            'relative rounded-md bg-linear-to-br from-secondary to-tertiary p-6 shadow-md transition-all duration-200',
            sizeClass,
            isConfigMode && 'ring-2 ring-dashed ring-accent/50',
            isDragging && 'opacity-50',
            isDragTarget && 'ring-2 ring-accent'
        ]"
        :draggable="isConfigMode"
        @dragstart="onDragStart"
        @dragend="onDragEnd"
        @dragover.prevent="onDragOver"
        @dragleave="onDragLeave"
        @drop="onDrop">
        <!-- Configuration Mode Controls -->
        <div
            v-if="isConfigMode"
            class="absolute -top-2 -right-2 z-10 flex gap-1">
            <button
                class="bg-primary hover:bg-accent rounded-full p-1.5 transition-colors"
                :title="'Toggle visibility'"
                @click.stop="$emit('toggle-visibility')">
                <EyeOnIcon
                    v-if="isVisible"
                    class="h-4 w-4" />
                <EyeOffIcon
                    v-else
                    class="h-4 w-4 text-gray-400" />
            </button>
        </div>

        <!-- Drag Handle (Config Mode) -->
        <div
            v-if="isConfigMode"
            class="bg-accent/20 absolute top-2 left-2 cursor-grab rounded px-2 py-1 text-xs active:cursor-grabbing">
            ⋮⋮
        </div>

        <!-- Widget Content -->
        <div :class="isConfigMode && 'mt-4'">
            <slot />
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import EyeOnIcon from '@/components/icons/EyeOnIcon.vue'
import EyeOffIcon from '@/components/icons/EyeOffIcon.vue'

const props = defineProps<{
    widgetId: string
    size?: 'full' | 'half' | 'third'
    isConfigMode?: boolean
    isDragging?: boolean
    isVisible?: boolean
}>()

const emit = defineEmits<{
    'drag-start': [widgetId: string]
    'drag-end': []
    'drop': [widgetId: string]
    'toggle-visibility': []
}>()

const isDragTarget = ref(false)

const sizeClass = computed(() => {
    switch (props.size) {
        case 'full':
            return 'col-span-full'
        case 'half':
            return 'lg:col-span-1'
        case 'third':
            return 'lg:col-span-1 xl:col-span-1/2'
        default:
            return ''
    }
})

function onDragStart(event: DragEvent) {
    if (!props.isConfigMode) return
    event.dataTransfer?.setData('text/plain', props.widgetId)
    emit('drag-start', props.widgetId)
}

function onDragEnd() {
    isDragTarget.value = false
    emit('drag-end')
}

function onDragOver(event: DragEvent) {
    if (!props.isConfigMode) return
    event.preventDefault()
    isDragTarget.value = true
}

function onDragLeave() {
    isDragTarget.value = false
}

function onDrop(event: DragEvent) {
    if (!props.isConfigMode) return
    event.preventDefault()
    isDragTarget.value = false
    emit('drop', props.widgetId)
}
</script>

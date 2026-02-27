<template>
    <div
        :class="[
            'from-secondary to-tertiary relative h-full rounded-md bg-linear-to-br p-4 shadow-md transition-all duration-200',
            isConfigMode && 'ring-dashed ring-accent/50 ring-2'
        ]">
        <!-- Configuration Mode Controls -->
        <div v-if="isConfigMode" class="absolute -top-2 -right-2 z-10 flex gap-1">
            <button
                class="bg-primary hover:bg-accent rounded-full p-1.5 transition-colors"
                :title="isVisible ? 'Hide widget' : 'Show widget'"
                @click.stop="$emit('toggle-visibility')">
                <EyeOnIcon v-if="isVisible" class="h-4 w-4" />
                <EyeOffIcon v-else class="text-primary-content/50 h-4 w-4" />
            </button>
        </div>

        <!-- Drag Handle (Config Mode) -->
        <div
            v-if="isConfigMode"
            class="bg-accent/20 absolute top-2 left-2 cursor-grab rounded px-2 py-1 text-xs select-none active:cursor-grabbing">
            ⋮⋮
        </div>

        <!-- Widget Content -->
        <div :class="['h-full overflow-x-hidden', isConfigMode && 'mt-4']">
            <slot />
        </div>
    </div>
</template>

<script setup lang="ts">
import EyeOnIcon from '@/components/icons/EyeOnIcon.vue'
import EyeOffIcon from '@/components/icons/EyeOffIcon.vue'

defineProps<{
    widgetId: string
    isConfigMode?: boolean
    isVisible?: boolean
}>()

defineEmits<{
    'toggle-visibility': []
}>()
</script>

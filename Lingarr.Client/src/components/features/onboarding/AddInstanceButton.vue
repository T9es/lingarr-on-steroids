<template>
    <div
        class="border-accent/50 hover:border-accent flex min-h-[200px] cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed transition-colors"
        @click="$emit('add')">
        <PlusIcon class="text-secondary-content mb-2 h-8 w-8" />
        <span class="text-secondary-content font-medium">Add new</span>
    </div>
</template>

<script setup lang="ts">
import PlusIcon from '@/components/icons/PlusIcon.vue'

defineProps<{
    type: 'radarr' | 'sonarr'
}>()

defineEmits<{
    (e: 'add'): void
}>()
</script>

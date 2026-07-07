<template>
    <div
        :class="[
            'flex items-start justify-between gap-4 border-b border-secondary/20 py-3 last:border-b-0',
            nested ? 'ml-6' : ''
        ]">
        <div class="flex-1 pr-2">
            <span class="text-primary-content block text-sm font-semibold leading-snug">
                {{ label }}
            </span>
            <p v-if="description" class="text-secondary-content mt-0.5 text-xs leading-snug">
                {{ description }}
            </p>
        </div>
        <div class="flex shrink-0 items-center">
            <slot></slot>
        </div>
    </div>
</template>

<script setup lang="ts">
defineProps<{
    label: string
    description?: string
    nested?: boolean
}>()
</script>

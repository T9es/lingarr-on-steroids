<template>
    <div
        class="inline-flex items-center rounded-full border px-3 py-1 text-xs font-semibold whitespace-nowrap"
        :class="variantClasses">
        <span><slot></slot></span>
    </div>
</template>

<script setup lang="ts">
const props = withDefaults(defineProps<{
    variant?: 'default' | 'success' | 'warning' | 'error' | 'info'
    classes?: string
}>(), {
    variant: 'default'
})

const variantClasses = computed(() => {
    if (props.classes) return props.classes

    const variants = {
        default: 'text-primary-content border-accent bg-secondary',
        success: 'text-green-400 border-green-500/50 bg-green-500/10',
        warning: 'text-yellow-400 border-yellow-500/50 bg-yellow-500/10',
        error: 'text-red-400 border-red-500/50 bg-red-500/10',
        info: 'text-blue-400 border-blue-500/50 bg-blue-500/10'
    }
    return variants[props.variant]
})
</script>

<script lang="ts">
import { computed } from 'vue'
</script>

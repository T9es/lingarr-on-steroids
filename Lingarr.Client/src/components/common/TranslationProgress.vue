<template>
    <div
        class="relative grow select-none"
        role="progressbar"
        :aria-valuenow="progress"
        aria-valuemin="0"
        aria-valuemax="100"
        :aria-label="ariaLabel ?? 'Translation progress'"
        :title="`${progress}%`">
        <div
            class="bg-secondary h-2 w-full overflow-hidden rounded-full brightness-125 select-none">
            <div
                class="h-full rounded-full bg-linear-to-r from-blue-500 to-purple-500 transition-all duration-1000 ease-out select-none"
                :style="{ width: `${progress}%` }"></div>
        </div>
    </div>
</template>

<script setup lang="ts">
defineProps<{
    progress: number
    ariaLabel?: string
}>()
</script>

<template>
    <div class="from-secondary to-tertiary relative rounded-md bg-linear-to-br p-6 shadow-md">
        <div class="flex items-center justify-center space-x-3 mb-2" v-if="title">
            <div v-if="$slots.icon" class="text-primary-content h-6 w-6">
                <slot name="icon"></slot>
            </div>
            <h2 class="text-primary-content text-2xl font-bold">{{ title }}</h2>
        </div>
        <p class="text-secondary-content/80 mb-6"><slot name="description"></slot></p>

        <div class="space-y-4">
            <slot name="content"></slot>
        </div>
    </div>
</template>

<script setup lang="ts">
const { title } = defineProps<{
    title: string
}>()
</script>

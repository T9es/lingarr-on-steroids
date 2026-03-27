<template>
    <div class="bg-primary border-secondary relative rounded-md border p-4 shadow-sm">
        <div v-if="title || $slots.header" class="mb-4 flex items-center justify-between">
            <div class="flex items-center gap-3">
                <div v-if="$slots.icon" class="text-accent">
                    <slot name="icon"></slot>
                </div>
                <h2 v-if="title" class="text-primary-content text-lg font-semibold">{{ title }}</h2>
            </div>
            <div v-if="$slots.actions">
                <slot name="actions"></slot>
            </div>
        </div>
        <slot name="header"></slot>
        <p v-if="$slots.description" class="text-secondary-content mb-4 text-sm">
            <slot name="description"></slot>
        </p>

        <div class="space-y-4">
            <slot></slot>
            <slot name="body"></slot>
            <slot name="content"></slot>
        </div>
    </div>
</template>

<script setup lang="ts">
const { title } = defineProps<{
    title?: string
}>()
</script>

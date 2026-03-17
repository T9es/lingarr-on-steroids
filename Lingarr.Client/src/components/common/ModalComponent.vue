<template>
    <Teleport to="body">
        <transition name="modal">
            <div
                v-if="isOpen"
                class="fixed inset-0 z-50 flex items-center justify-center bg-black/60"
                @click.self="$emit('close')">
                <div
                    :class="sizeClasses"
                    class="bg-secondary max-h-[90vh] w-full overflow-hidden rounded-lg shadow-xl">
                    <div class="border-accent flex items-center justify-between border-b px-4 py-3">
                        <h2 class="text-primary-content text-lg font-semibold">
                            <slot name="header" />
                        </h2>
                        <button
                            class="text-secondary-content hover:text-primary-content"
                            @click="$emit('close')">
                            <svg
                                class="h-5 w-5"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24">
                                <path
                                    stroke-linecap="round"
                                    stroke-linejoin="round"
                                    stroke-width="2"
                                    d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>
                    <div class="max-h-[calc(90vh-8rem)] overflow-y-auto p-4">
                        <slot />
                    </div>
                    <div class="border-accent flex justify-end gap-2 border-t px-4 py-3">
                        <slot name="footer" />
                    </div>
                </div>
            </div>
        </transition>
    </Teleport>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
    defineProps<{
        isOpen: boolean
        size?: 'sm' | 'md' | 'lg' | 'xl'
    }>(),
    {
        size: 'md'
    }
)

defineEmits<{
    close: []
}>()

const sizeClasses = computed(() => {
    switch (props.size) {
        case 'sm':
            return 'max-w-md'
        case 'md':
            return 'max-w-2xl'
        case 'lg':
            return 'max-w-4xl'
        case 'xl':
            return 'max-w-6xl'
        default:
            return 'max-w-2xl'
    }
})
</script>

<style scoped>
.modal-enter-active,
.modal-leave-active {
    transition: opacity 0.2s ease;
}

.modal-enter-from,
.modal-leave-to {
    opacity: 0;
}
</style>

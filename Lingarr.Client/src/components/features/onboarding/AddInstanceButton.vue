<template>
    <div class="relative" ref="containerRef">
        <div
            class="border-accent/50 hover:border-accent flex min-h-[200px] cursor-pointer flex-col items-center justify-center rounded-lg border-2 border-dashed transition-colors"
            @click="toggleMenu">
            <PlusIcon class="text-secondary-content mb-2 h-8 w-8" />
            <span class="text-secondary-content font-medium">Add new</span>
        </div>
        
        <!-- Dropdown Menu -->
        <Transition
            enter-active-class="transition ease-out duration-100"
            enter-from-class="transform opacity-0 scale-95"
            enter-to-class="transform opacity-100 scale-100"
            leave-active-class="transition ease-in duration-75"
            leave-from-class="transform opacity-100 scale-100"
            leave-to-class="transform opacity-0 scale-95">
            <div
                v-if="isOpen"
                class="border-accent bg-primary absolute top-full left-0 right-0 z-10 mt-2 overflow-hidden rounded-md border shadow-lg">
                <button
                    type="button"
                    class="hover:bg-accent/10 flex w-full cursor-pointer items-center gap-3 px-4 py-3 text-left transition-colors"
                    @click.stop="selectType('radarr')">
                    <RadarrIcon class="h-5 w-5" />
                    <span class="text-primary-content">Add Radarr</span>
                </button>
                <button
                    type="button"
                    class="hover:bg-accent/10 flex w-full cursor-pointer items-center gap-3 border-t border-accent/20 px-4 py-3 text-left transition-colors"
                    @click.stop="selectType('sonarr')">
                    <SonarrIcon class="h-5 w-5" />
                    <span class="text-primary-content">Add Sonarr</span>
                </button>
            </div>
        </Transition>
    </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import PlusIcon from '@/components/icons/PlusIcon.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'

const emit = defineEmits<{
    (e: 'add', type: 'radarr' | 'sonarr'): void
}>()

const isOpen = ref(false)
const containerRef = ref<HTMLElement | null>(null)

const toggleMenu = () => {
    isOpen.value = !isOpen.value
}

const selectType = (type: 'radarr' | 'sonarr') => {
    emit('add', type)
    isOpen.value = false
}

const handleClickOutside = (event: MouseEvent) => {
    if (containerRef.value && !containerRef.value.contains(event.target as Node)) {
        isOpen.value = false
    }
}

onMounted(() => {
    document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
    document.removeEventListener('click', handleClickOutside)
})
</script>

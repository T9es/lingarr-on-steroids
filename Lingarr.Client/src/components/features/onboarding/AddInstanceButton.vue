<template>
    <div class="relative">
        <!-- Collapsed state: Add new button -->
        <button
            v-if="!isExpanded"
            class="border-accent hover:bg-accent/10 flex w-full cursor-pointer items-center justify-center gap-2 rounded-md border p-3 transition-colors"
            @click="isExpanded = true">
            <PlusIcon class="h-4 w-4" />
            <span>Add new</span>
        </button>

        <!-- Expanded state: Radarr/Sonarr buttons -->
        <div v-else class="flex gap-2">
            <button
                class="border-accent hover:bg-accent/10 flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-md border p-3 transition-colors"
                @click="$emit('add-radarr'); isExpanded = false">
                <RadarrIcon class="h-5 w-5" />
                <span>Add Radarr</span>
            </button>
            <button
                class="border-accent hover:bg-accent/10 flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-md border p-3 transition-colors"
                @click="$emit('add-sonarr'); isExpanded = false">
                <SonarrIcon class="h-5 w-5" />
                <span>Add Sonarr</span>
            </button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import PlusIcon from '@/components/icons/PlusIcon.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'

defineEmits<{
    'add-radarr': []
    'add-sonarr': []
}>()

const isExpanded = ref(false)
</script>

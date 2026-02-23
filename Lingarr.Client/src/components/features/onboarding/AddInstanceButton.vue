<template>
    <div class="relative h-full min-h-[120px]">
        <!-- Collapsed state: Add new button -->
        <button
            v-if="!isExpanded"
            class="border-accent hover:bg-accent/10 flex h-full w-full cursor-pointer flex-col items-center justify-center gap-2 rounded-md border border-dashed p-4 transition-colors"
            @click="isExpanded = true">
            <PlusIcon class="h-8 w-8 text-gray-400" />
            <span class="text-gray-400 font-medium">{{ translate('onboarding.addInstance.addNew') }}</span>
        </button>

        <!-- Expanded state: Radarr/Sonarr buttons -->
        <div v-else class="flex h-full flex-col gap-2">
            <button
                class="border-accent hover:bg-accent/10 flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-md border p-3 transition-colors"
                @click="$emit('add-radarr'); isExpanded = false">
                <RadarrIcon class="h-5 w-5" />
                <span>{{ translate('onboarding.addInstance.addRadarr') }}</span>
            </button>
            <button
                class="border-accent hover:bg-accent/10 flex flex-1 cursor-pointer items-center justify-center gap-2 rounded-md border p-3 transition-colors"
                @click="$emit('add-sonarr'); isExpanded = false">
                <SonarrIcon class="h-5 w-5" />
                <span>{{ translate('onboarding.addInstance.addSonarr') }}</span>
            </button>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import PlusIcon from '@/components/icons/PlusIcon.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

defineEmits<{
    'add-radarr': []
    'add-sonarr': []
}>()

const isExpanded = ref(false)
</script>

<template>
    <div ref="containerRef" class="relative w-full sm:w-[320px]">
        <div
            class="flex min-h-[228px] cursor-pointer flex-col items-center justify-center rounded-xl border-2 border-dashed px-6 text-center transition-all hover:-translate-y-1"
            :class="[isTypedButton ? 'bg-primary/35' : 'bg-primary/20']"
            :style="tileStyle"
            @click="handleClick">
            <component :is="displayIcon" class="mb-3 h-9 w-9 text-primary-content" />
            <span class="text-primary-content text-base font-semibold">
                {{ translate('onboarding.addInstance.addNew') }}
            </span>
            <span v-if="type" class="text-secondary-content mt-2 text-sm">
                {{
                    type === 'radarr'
                        ? translate('onboarding.addInstance.addRadarr')
                        : translate('onboarding.addInstance.addSonarr')
                }}
            </span>
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
                v-if="isOpen && !type"
                class="border-accent bg-primary absolute top-full right-0 left-0 z-10 mt-2 overflow-hidden rounded-md border shadow-lg">
                <button
                    type="button"
                    class="hover:bg-accent/10 flex w-full cursor-pointer items-center gap-3 px-4 py-3 text-left transition-colors"
                    @click.stop="selectType('radarr')">
                    <RadarrIcon class="h-5 w-5" />
                    <span class="text-primary-content">
                        {{ translate('onboarding.addInstance.addRadarr') }}
                    </span>
                </button>
                <button
                    type="button"
                    class="hover:bg-accent/10 border-accent/20 flex w-full cursor-pointer items-center gap-3 border-t px-4 py-3 text-left transition-colors"
                    @click.stop="selectType('sonarr')">
                    <SonarrIcon class="h-5 w-5" />
                    <span class="text-primary-content">
                        {{ translate('onboarding.addInstance.addSonarr') }}
                    </span>
                </button>
            </div>
        </Transition>
    </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue'
import PlusIcon from '@/components/icons/PlusIcon.vue'
import RadarrIcon from '@/components/icons/RadarrIcon.vue'
import SonarrIcon from '@/components/icons/SonarrIcon.vue'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

const props = defineProps<{
    type?: 'radarr' | 'sonarr'
}>()

const emit = defineEmits<{
    (e: 'add', type: 'radarr' | 'sonarr'): void
}>()

const isOpen = ref(false)
const containerRef = ref<HTMLElement | null>(null)

const typePalette = {
    radarr: {
        borderColor: 'rgba(255, 194, 48, 0.45)',
        backgroundColor: 'rgba(255, 194, 48, 0.08)'
    },
    sonarr: {
        borderColor: 'rgba(0, 204, 255, 0.42)',
        backgroundColor: 'rgba(0, 204, 255, 0.08)'
    }
} as const

const isTypedButton = computed(() => !!props.type)

const displayIcon = computed(() => {
    if (props.type === 'radarr') {
        return RadarrIcon
    }

    if (props.type === 'sonarr') {
        return SonarrIcon
    }

    return PlusIcon
})

const tileStyle = computed(() => {
    if (!props.type) {
        return undefined
    }

    return typePalette[props.type]
})

const toggleMenu = () => {
    isOpen.value = !isOpen.value
}

const handleClick = () => {
    if (props.type) {
        selectType(props.type)
        return
    }

    toggleMenu()
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

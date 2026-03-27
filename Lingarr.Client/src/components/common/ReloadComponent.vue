<template>
    <div :title="translate('common.refresh')" @click="handleClick">
        <ReloadIcon
            class="h-6 w-6 cursor-pointer transition-all duration-300 ease-in-out"
            :class="{ 'animate-spin': isLoading || localLoading }" />
    </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import ReloadIcon from '@/components/icons/ReloadIcon.vue'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()

const props = withDefaults(
    defineProps<{
        loading?: boolean
    }>(),
    {
        loading: false
    }
)

const emit = defineEmits<{
    (e: 'click'): void
    (e: 'toggle:update', value: boolean): void
}>()

const localLoading = ref(false)
const isLoading = computed(() => props.loading || localLoading.value)

async function handleClick() {
    if (isLoading.value) return

    localLoading.value = true
    emit('toggle:update', true)
    emit('click')

    if (!props.loading) {
        setTimeout(() => {
            localLoading.value = false
        }, 500)
    }
}
</script>

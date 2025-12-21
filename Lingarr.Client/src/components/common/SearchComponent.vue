<template>
    <div class="relative w-full md:w-96">
        <label :for="id" class="sr-only">Search media</label>
        <input
            :id="id"
            :value="modelValue.searchQuery"
            type="text"
            placeholder="Search media..."
            class="border-accent bg-primary text-primary-content placeholder-primary/60 focus:ring-accent focus:border-accent block w-full rounded-md border px-8 py-1 text-sm outline-hidden focus:ring-2"
            @input="search" />
        <SearchIcon
            aria-hidden="true"
            class="text-accent-content absolute top-1/2 left-2 h-4 w-4 -translate-y-1/2 transform" />
        <button
            v-if="modelValue.searchQuery"
            type="button"
            class="text-accent-content focus:ring-accent absolute top-1/2 right-1 -translate-y-1/2 transform cursor-pointer rounded-full p-1 transition-colors hover:bg-white/10 focus:ring-2 focus:outline-hidden"
            aria-label="Clear search"
            @click="clear">
            <TimesIcon class="h-4 w-4" />
        </button>
    </div>
</template>

<script lang="ts" setup>
import { useId } from 'vue'
import { IFilter } from '@/ts'
import SearchIcon from '@/components/icons/SearchIcon.vue'
import TimesIcon from '@/components/icons/TimesIcon.vue'

const emit = defineEmits(['update:modelValue'])
const { modelValue } = defineProps<{
    modelValue: IFilter
}>()

const id = useId()

const searchQuery = (value: string) => {
    emit('update:modelValue', {
        ...modelValue,
        searchQuery: value
    })
}

const search = (event: Event) => {
    searchQuery((event.target as HTMLInputElement).value)
}

const clear = () => {
    searchQuery('')
}
</script>

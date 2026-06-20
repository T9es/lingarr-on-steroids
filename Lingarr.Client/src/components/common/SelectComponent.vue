<template>
    <div class="relative">
        <label v-if="label" class="text-primary-content mb-1 block text-sm">
            {{ label }}
        </label>
        <div
            ref="triggerRef"
            class="border-accent flex h-12 cursor-pointer items-center justify-between rounded-md border px-4 py-2"
            @click="toggleDropdown">
            <span v-if="!selected" class="!text-secondary-content">{{ displayPlaceholder }}</span>
            <div v-else class="flex max-h-12 flex-wrap gap-2 overflow-auto">
                <span
                    class="bg-accent flex cursor-pointer items-center rounded-md px-3 py-1 text-sm font-medium">
                    <span class="text-accent-content mr-2">{{ displaySelectedLabel }}</span>
                </span>
            </div>
            <div class="flex items-center">
                <LoaderCircleIcon v-if="isLoading" class="mr-2 h-4 w-4 animate-spin" />
                <CaretRightIcon
                    :class="{ 'rotate-90': isOpen }"
                    class="arrow-right text-primary-content h-5 w-5 transition-transform duration-200" />
            </div>
        </div>
        <Teleport to="body">
            <ul
                v-show="isOpen"
                ref="dropdownRef"
                class="border-accent bg-primary z-[100] max-h-60 overflow-auto rounded-md border shadow-lg"
                :style="{
                    position: 'fixed',
                    top: dropdownPosition.top + 'px',
                    left: dropdownPosition.left + 'px',
                    width: dropdownPosition.width + 'px'
                }">
                <li v-if="enableSearch" class="border-secondary border-b p-2">
                    <input
                        v-model="searchQuery"
                        type="text"
                        class="border-secondary text-primary-content focus:ring-accent w-full rounded border bg-transparent px-2 py-1 text-sm outline-hidden focus:border-transparent focus:ring-2"
                        :placeholder="translate('settings.services.modelSearchPlaceholder')" />
                </li>
                <li v-if="!filteredOptions.length" class="text-primary-content p-3">
                    {{ displayNoOptions }}
                </li>
                <li
                    v-for="(option, index) in filteredOptions"
                    :key="`${option.value}-${index}`"
                    class="text-primary-content hover:bg-accent/20 cursor-pointer px-4 py-2"
                    :class="{ 'bg-accent/20': isSelected(option.value) }"
                    @click="selectOption(option)">
                    {{ option.label }}
                </li>
            </ul>
        </Teleport>
    </div>
</template>

<script setup lang="ts">
import { Ref, ref, nextTick, computed } from 'vue'
import CaretRightIcon from '@/components/icons/CaretRightIcon.vue'
import LoaderCircleIcon from '@/components/icons/LoaderCircleIcon.vue'
import useClickOutside from '@/composables/useClickOutside'
import { useI18n } from '@/plugins/i18n'

export interface ISelectOption {
    value: string
    label: string
}

const props = withDefaults(
    defineProps<{
        label?: string
        options: ISelectOption[]
        selected?: string
        disabled?: boolean
        loadOnOpen?: boolean
        placeholder?: string
        noOptions?: string
        enableSearch?: boolean
    }>(),
    {
        label: '',
        options: () => [],
        selected: '',
        disabled: false,
        loadOnOpen: false,
        placeholder: '',
        noOptions: '',
        selectedLabel: '',
        enableSearch: false
    }
)

const emit = defineEmits(['update:selected', 'fetch-options'])
const { translate } = useI18n()

// Computed properties for placeholder and noOptions that use translations as defaults
const displayPlaceholder = computed(() => props.placeholder || translate('common.selectItems'))
const displayNoOptions = computed(
    () => props.noOptions || translate('common.selectSourceLanguageFirst')
)

const isOpen: Ref<boolean> = ref(false)
const isLoading: Ref<boolean> = ref(false)
const triggerRef: Ref<HTMLElement | undefined> = ref()
const dropdownRef: Ref<HTMLElement | undefined> = ref()
const searchQuery = ref('')
const dropdownPosition = ref({ top: 0, left: 0, width: 0 })

const sortedOptions = computed(() => {
    return [...props.options].sort((a, b) => a.label.localeCompare(b.label))
})

const filteredOptions = computed(() => {
    if (!props.enableSearch || searchQuery.value.trim() === '') {
        return sortedOptions.value
    }

    const query = searchQuery.value.toLowerCase()
    return sortedOptions.value.filter(
        (option) =>
            option.label.toLowerCase().includes(query) || option.value.toLowerCase().includes(query)
    )
})

const displaySelectedLabel = computed(() => {
    const option = props.options.find((item) => item.value === props.selected)
    return option ? option.label : props.selected
})

const toggleDropdown = async () => {
    if (props.disabled) return

    isOpen.value = !isOpen.value

    if (isOpen.value) {
        const rect = triggerRef.value?.getBoundingClientRect()
        if (rect) {
            dropdownPosition.value = {
                top: rect.bottom + window.scrollY,
                left: rect.left + window.scrollX,
                width: rect.width
            }
        }

        if (props.loadOnOpen) {
            isLoading.value = true
            emit('fetch-options')
        }

        if (props.enableSearch) {
            searchQuery.value = ''
        }
    }

    await nextTick()
}

const setLoadingState = async (loading: boolean) => {
    isLoading.value = loading
}

const selectOption = (option: ISelectOption) => {
    emit('update:selected', option.value)
    isOpen.value = false
    searchQuery.value = ''
}

const isSelected = (option: string) => {
    return props.selected == option
}

useClickOutside(
    dropdownRef,
    () => {
        isOpen.value = false
        searchQuery.value = ''
    },
    triggerRef
)

defineExpose({
    setLoadingState
})
</script>

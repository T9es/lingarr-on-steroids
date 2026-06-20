<template>
    <div class="relative">
        <div
            ref="triggerRef"
            class="border-accent flex h-12 cursor-pointer items-center justify-between rounded-md border px-4 py-2"
            :class="{ 'opacity-50 cursor-not-allowed': disabled }"
            @click="toggleDropdown">
            <span v-if="selectedItems.length === 0" class="text-primary-content/50">
                {{ translate('settings.translate.languageSelectPlaceholder') }}
            </span>
            <div v-else class="flex max-h-12 flex-wrap gap-2 overflow-auto">
                <span
                    v-for="(item, index) in selectedItems"
                    :key="`${item.code}-${index}`"
                    :data-key="`${item.code}-${index}`"
                    class="bg-accent text-secondary-content flex cursor-pointer items-center rounded-md px-3 py-1 text-sm font-medium"
                    :class="{ 'cursor-default': disabled }"
                    @click.stop="disabled ? null : removeItem(item)">
                    <span class="text-accent-content mr-2">{{ item.name }}</span>
                    <TimesIcon v-if="!disabled" class="mt-0.5 h-4 w-4" />
                </span>
            </div>
            <CaretRightIcon
                :class="{ 'rotate-90': isOpen }"
                class="arrow-right text-secondary-content h-5 w-5 transition-transform duration-200" />
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
                <li v-if="!options?.length" class="text-primary-content p-3">
                    {{ translate('settings.translate.languageSelectTargetNotification') }}
                </li>
                <li v-else class="relative flex items-center">
                    <input
                        ref="searchInput"
                        v-model="searchTerm"
                        class="border-accent text-primary-content relative w-full border-b bg-transparent p-2 outline-hidden"
                        :placeholder="
                            translate('settings.translate.selectOrSearchLanguagePlaceholder')
                        " />

                    <span
                        v-if="searchTerm"
                        class="text-primary-content absolute right-0 z-10 flex cursor-pointer items-center p-3"
                        @click="searchTerm = ''">
                        <TimesIcon class="h-4 w-4" />
                    </span>
                </li>
                <li
                    v-for="(language, index) in filteredLanguages"
                    :key="`${language.code}-${index}`"
                    :data-key="`${language.code}-${index}`"
                    class="text-primary-content hover:bg-accent/20 cursor-pointer px-4 py-2"
                    :class="{ 'bg-accent/20': isSelected(language) }"
                    @click="selectItem(language)">
                    {{ language.name }}
                </li>
            </ul>
        </Teleport>
    </div>
</template>

<script setup lang="ts">
import { Ref, ref, computed, watch, nextTick } from 'vue'
import { ILanguage } from '@/ts'
import CaretRightIcon from '@/components/icons/CaretRightIcon.vue'
import TimesIcon from '@/components/icons/TimesIcon.vue'
import useClickOutside from '@/composables/useClickOutside'

const {
    options,
    selected = [],
    disabled = false
} = defineProps<{
    options: ILanguage[]
    selected?: ILanguage[]
    disabled?: boolean
}>()

const emit = defineEmits(['update:selected'])

const isOpen: Ref<boolean> = ref(false)
const searchInput: Ref<HTMLInputElement | undefined> = ref()
const selectedItems: Ref<ILanguage[]> = ref(selected)
const triggerRef: Ref<HTMLElement | undefined> = ref()
const dropdownRef: Ref<HTMLElement | undefined> = ref()
const searchTerm: Ref<string> = ref('')
const dropdownPosition = ref({ top: 0, left: 0, width: 0 })

const filteredLanguages = computed(() => {
    return options
        .filter((option) => {
            if (!option) return false
            if (!searchTerm.value) return true
            return option.name.toLowerCase().includes(searchTerm.value.toLowerCase())
        })
        .sort((a, b) => a.name.localeCompare(b.name))
})

const toggleDropdown = async () => {
    if (disabled) return
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
    }
    await nextTick()
    searchInput.value?.focus()
}

const selectItem = (item: ILanguage) => {
    const index = selectedItems.value.findIndex((language) => language.code === item.code)
    if (index === -1) {
        selectedItems.value.push(item)
    } else {
        selectedItems.value.splice(index, 1)
    }
    emit('update:selected', selectedItems.value)
    isOpen.value = false
}

const removeItem = (item: ILanguage) => {
    const index = selectedItems.value.findIndex((language) => language.code === item.code)
    if (index !== -1) {
        selectedItems.value.splice(index, 1)
        emit('update:selected', selectedItems.value)
    }
}

const isSelected = (item: ILanguage) => {
    return selectedItems.value.some((language) => language.code === item.code)
}

watch(
    () => selected,
    (newValue) => {
        selectedItems.value = newValue
    },
    { deep: true }
)

useClickOutside(
    dropdownRef,
    () => {
        isOpen.value = false
    },
    triggerRef
)
</script>

<template>
    <div class="relative">
        <button
            ref="buttonRef"
            class="inline-flex h-10 w-10 cursor-pointer items-center justify-center"
            :title="translate('common.changeLanguage')"
            @click="toggleDropdown">
            <LanguageIcon
                class="text-secondary-content hover:text-primary-content h-5 w-5 cursor-pointer transition-colors" />
        </button>
        <Teleport to="body">
            <transition
                enter-active-class="transition ease-out duration-100"
                enter-from-class="transform opacity-0 scale-95"
                enter-to-class="transform opacity-100 scale-100"
                leave-active-class="transition ease-in duration-75"
                leave-from-class="transform opacity-100 scale-100"
                leave-to-class="transform opacity-0 scale-95">
                <div
                    v-if="isOpen"
                    ref="dropdownRef"
                    class="border-accent bg-secondary fixed z-[100] mt-2 w-48 origin-top-right rounded-md border shadow-lg"
                    :style="{
                        top: dropdownPosition.top + 'px',
                        left: dropdownPosition.left + 'px'
                    }">
                    <div class="py-1" role="menu">
                        <button
                            v-for="lang in languages"
                            :key="lang.code"
                            class="hover:bg-secondary-focus flex w-full cursor-pointer items-center justify-between px-4 py-2 text-left text-sm"
                            :class="[
                                currentLocale === lang.code
                                    ? 'text-accent'
                                    : 'text-secondary-content'
                            ]"
                            @click="selectLanguage(lang.code)">
                            <span>{{ lang.name }}</span>
                            <CheckMarkIcon
                                v-if="currentLocale === lang.code"
                                class="text-accent h-4 w-4" />
                        </button>
                    </div>
                </div>
            </transition>
        </Teleport>
    </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useI18n } from '@/plugins/i18n'
import { type ILocale } from '@/ts'
import LanguageIcon from '@/components/icons/LanguageIcon.vue'
import CheckMarkIcon from '@/components/icons/CheckMarkIcon.vue'

const { translate, languages, locale, setLocale } = useI18n()

// Current locale as a plain computed value for template usage
const currentLocale = computed(() => locale.value)

const isOpen = ref(false)
const buttonRef = ref<HTMLElement | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
const dropdownPosition = ref({ top: 0, left: 0 })

const toggleDropdown = (event: MouseEvent) => {
    isOpen.value = !isOpen.value
    if (isOpen.value) {
        const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
        dropdownPosition.value = {
            top: rect.bottom + window.scrollY,
            left: rect.left + window.scrollX
        }
    }
}

const selectLanguage = async (langCode: string) => {
    isOpen.value = false
    if (locale.value !== langCode) {
        await setLocale(langCode as ILocale)
    }
}

const handleClickOutside = (event: MouseEvent) => {
    if (
        buttonRef.value &&
        !buttonRef.value.contains(event.target as Node) &&
        dropdownRef.value &&
        !dropdownRef.value.contains(event.target as Node)
    ) {
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

<template>
    <div class="relative">
        <!-- Backdrop -->
        <div
            v-if="isOpen"
            class="bg-opacity-50 fixed inset-0 z-40 bg-black md:hidden"
            @click="isOpen = false"></div>
        <!-- Aside -->
        <aside
            :class="
                isOpen ? 'w-64 translate-x-0' : 'w-64 -translate-x-full md:w-20 md:translate-x-0'
            "
            class="border-accent bg-secondary fixed top-0 left-0 z-50 flex h-full shrink-0 flex-col overflow-hidden border-r transition-all duration-300 ease-in-out">
            <TimesIcon
                class="absolute top-1 right-1 block h-6 w-6 cursor-pointer md:hidden"
                @click="isOpen = false" />
            <div class="flex h-16 items-center justify-center">
                <h1
                    v-show="isOpen"
                    class="truncate text-xl font-bold transition-opacity duration-300">
                    Lingarr
                </h1>
                <h1
                    v-show="!isOpen"
                    class="truncate text-xl font-bold transition-opacity duration-300">
                    L
                </h1>
            </div>
            <!-- Navigation -->
            <nav class="grow overflow-x-hidden overflow-y-auto py-6">
                <ul class="space-y-2 px-2">
                    <li v-for="(item, index) in menuItems" :key="index">
                        <router-link
                            :to="{ name: item.route }"
                            class="hover:bg-primary/30 relative flex w-full items-center px-3 py-2 transition-colors"
                            :class="[
                                isActive(item)
                                    ? 'bg-primary/50 text-accent border-accent border-l-4 pl-2'
                                    : 'text-primary-content border-l-4 border-transparent pl-2'
                            ]"
                            @click="closeOnMobile">
                            <component :is="item.icon" class="mr-3 h-5 w-5 shrink-0" />
                            <span v-show="isOpen" class="truncate">{{ item.label }}</span>
                            <!-- Badge when sidebar OPEN - positioned relative to text area -->
                            <span
                                v-if="item.route == 'translations' && activeRequests > 0 && isOpen"
                                class="bg-accent text-secondary-content absolute top-0 left-0 inline-flex translate-x-full -translate-y-1/2 items-center justify-center rounded-full px-1.5 py-0.5 text-xs leading-none font-bold">
                                {{ activeRequests }}
                            </span>
                            <!-- Badge when sidebar COLLAPSED - next to icon -->
                            <span
                                v-if="item.route == 'translations' && activeRequests > 0 && !isOpen"
                                class="bg-accent text-secondary-content absolute top-1/2 right-1 inline-flex -translate-y-1/2 items-center justify-center rounded-full px-1.5 py-0.5 text-xs leading-none font-bold">
                                {{ activeRequests }}
                            </span>
                        </router-link>
                    </li>
                </ul>
            </nav>
            <button
                class="border-accent/30 hover:bg-primary/30 text-primary-content/70 hover:text-primary-content mt-auto hidden w-full items-center justify-center border-t p-4 transition-colors md:flex"
                @click="isOpen = !isOpen">
                <CaretRightIcon
                    class="h-5 w-5 transition-transform"
                    :class="isOpen ? 'rotate-180' : ''" />
            </button>
            <!-- Version and media section -->
            <div
                v-show="isOpen"
                class="border-accent/30 bg-tertiary relative mt-auto h-64 w-full flex-none overflow-hidden border-t">
                <img
                    v-if="instanceStore.getPoster"
                    :src="`/api/image/${instanceStore.getPoster}`"
                    class="mask-gradient h-full w-full object-cover"
                    alt="poster" />
                <div
                    class="from-secondary via-secondary/60 pointer-events-none absolute inset-0 bg-linear-to-t to-transparent"></div>
                <div
                    v-if="instanceStore.getVersion.currentVersion.length"
                    class="absolute right-0 bottom-0 z-10 flex w-full flex-col items-center gap-2 p-4">
                    <!-- Dev Build Badge - shown when running a dev/alpha/beta build -->
                    <BadgeComponent
                        v-if="instanceStore.getVersion.isDevBuild"
                        classes="text-primary-content border-purple-300 bg-purple-500/30">
                        {{ translate('common.devBuild') }}
                    </BadgeComponent>
                    <!-- Update Available Badge - shown for release builds with updates -->
                    <BadgeComponent
                        v-else-if="instanceStore.getVersion.newVersion"
                        classes="text-primary-content border-green-300 bg-green-500/30">
                        {{
                            translate('common.updateAvailable').format({
                                version: instanceStore.getVersion.latestVersion
                            })
                        }}
                    </BadgeComponent>
                    <!-- Current Version Badge - shown for release builds on latest version -->
                    <BadgeComponent
                        v-else
                        classes="text-primary-content border-primary-content/30 bg-secondary/50">
                        {{
                            translate('common.currentVersion').format({
                                version: instanceStore.getVersion.currentVersion
                            })
                        }}
                    </BadgeComponent>
                    <a
                        href="https://github.com/lingarr-translate/lingarr"
                        target="_blank"
                        rel="noopener noreferrer"
                        class="text-primary-content/60 hover:text-primary-content pointer-events-auto flex items-center gap-1.5 text-xs transition-colors">
                        <GithubIcon class="h-3.5 w-3.5" />
                        <span>{{ translate('common.basedOnLingarr') }}</span>
                    </a>
                </div>
            </div>
        </aside>
    </div>
</template>

<script setup lang="ts">
import { computed, ComputedRef } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from '@/plugins/i18n'
import { useInstanceStore } from '@/store/instance'
import { useTranslationRequestStore } from '@/store/translationRequest'
import { MenuItem } from '@/ts'
import HomeIcon from '@/components/icons/HomeIcon.vue'
import MovieIcon from '@/components/icons/MovieIcon.vue'
import ShowIcon from '@/components/icons/ShowIcon.vue'
import SettingIcon from '@/components/icons/SettingIcon.vue'
import TimesIcon from '@/components/icons/TimesIcon.vue'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import LanguageIcon from '@/components/icons/LanguageIcon.vue'
import TestIcon from '@/components/icons/TestIcon.vue'
import GithubIcon from '@/components/icons/GithubIcon.vue'
import QuestionMarkCircleIcon from '@/components/icons/QuestionMarkCircleIcon.vue'
import CaretRightIcon from '@/components/icons/CaretRightIcon.vue'

const translationRequestStore = useTranslationRequestStore()
const instanceStore = useInstanceStore()
const route = useRoute()
const { translate } = useI18n()

function closeOnMobile() {
    if (window.innerWidth < 768) {
        isOpen.value = false
    }
}

const activeRequests: ComputedRef<number> = computed(
    () => translationRequestStore.getActiveTranslationRequests
)

const isOpen = computed({
    get: () => instanceStore.getIsOpen,
    set: (value) => instanceStore.setIsOpen(value)
})

const menuItems = computed<MenuItem[]>(() => [
    { label: translate('navigation.dashboard'), icon: HomeIcon, route: 'dashboard', children: [] },
    { label: translate('navigation.movies'), icon: MovieIcon, route: 'movies', children: [] },
    { label: translate('navigation.tvShows'), icon: ShowIcon, route: 'shows', children: [] },
    {
        label: translate('navigation.translations'),
        icon: LanguageIcon,
        route: 'translations',
        children: []
    },
    {
        label: translate('navigation.translationTest'),
        icon: TestIcon,
        route: 'translation-test',
        children: []
    },
    {
        label: translate('navigation.settings'),
        icon: SettingIcon,
        route: 'integration-settings',
        children: [
            'integration-settings',
            'services-settings',
            'subtitle-settings',
            'integrity-settings',
            'custom-sources-settings',
            'upload-workspace-settings',
            'tasks-settings',
            'logs-settings'
        ]
    },
    {
        label: translate('navigation.help'),
        icon: QuestionMarkCircleIcon,
        route: 'help',
        children: []
    }
])

function isActive(item: MenuItem) {
    if (item.route == route.name) return true

    if (item.children?.length) {
        return item.children.includes(route.name as string)
    }
    return false
}
</script>

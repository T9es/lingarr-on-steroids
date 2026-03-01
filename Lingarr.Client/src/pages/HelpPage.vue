<template>
    <PageLayout>
        <div class="flex h-full flex-col">
            <nav
                class="bg-secondary border-accent/30 custom-scrollbar flex-none overflow-x-auto border-b">
                <ul class="flex w-max min-w-full">
                    <li v-for="(item, index) in menuItems" :key="index" class="flex-1 sm:flex-none">
                        <router-link
                            :to="{ name: item.route }"
                            :title="item.label"
                            class="hover:bg-primary/30 flex items-center justify-center px-4 py-3 text-sm font-medium transition-colors"
                            :class="[
                                $route.name === item.route
                                    ? 'border-accent text-accent bg-primary/50 border-b-2'
                                    : 'text-primary-content/70 hover:text-primary-content'
                            ]">
                            <component :is="item.icon" class="mr-2 h-4 w-4 shrink-0" />
                            <span class="whitespace-nowrap">{{ item.label }}</span>
                        </router-link>
                    </li>
                </ul>
            </nav>

            <main class="w-full flex-1 overflow-auto">
                <router-view></router-view>
            </main>
        </div>
    </PageLayout>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from '@/plugins/i18n'
import { MenuItem } from '@/ts'
import PageLayout from '@/components/layout/PageLayout.vue'
import QuestionMarkCircleIcon from '@/components/icons/QuestionMarkCircleIcon.vue'
import BuildingIcon from '@/components/icons/BuildingIcon.vue'

const { translate } = useI18n()

const menuItems = computed<MenuItem[]>(() => [
    {
        label: translate('navigation.onboarding'),
        icon: QuestionMarkCircleIcon,
        route: 'help-onboarding',
        children: []
    },
    {
        label: translate('navigation.about'),
        icon: BuildingIcon,
        route: 'help-about',
        children: []
    }
])
</script>

<template>
    <PageLayout>
        <div class="grid h-full grid-cols-[auto_1fr]">
            <aside class="bg-secondary w-[3.175rem] shrink-0 md:w-40">
                <nav class="pt-4 md:pt-8 md:pl-4">
                    <ul class="flex flex-col space-y-4">
                        <li
                            v-for="(item, index) in menuItems"
                            :key="index"
                            :class="[
                                'w-full hover:brightness-150',
                                { 'brightness-150': $route.name === item.route }
                            ]">
                            <router-link
                                :to="{ name: item.route }"
                                :title="item.label"
                                :aria-label="item.label"
                                class="flex w-full cursor-pointer items-center justify-center md:justify-start">
                                <component :is="item.icon" class="h-4 w-4 md:mr-3" />
                                <span class="hidden text-sm md:inline-block">
                                    {{ item.label }}
                                </span>
                            </router-link>
                        </li>
                    </ul>
                </nav>
            </aside>

            <main class="flex">
                <router-view></router-view>
            </main>
        </div>
    </PageLayout>
</template>

<script setup lang="ts">
import { useI18n } from '@/plugins/i18n'
import { MenuItem } from '@/ts'
import PageLayout from '@/components/layout/PageLayout.vue'
import QuestionMarkCircleIcon from '@/components/icons/QuestionMarkCircleIcon.vue'
import BuildingIcon from '@/components/icons/BuildingIcon.vue'

const { translate } = useI18n()

const menuItems: MenuItem[] = [
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
]
</script>

<template>
    <div
        class="grid grid-flow-row auto-rows-max grid-cols-1 gap-4 p-4 xl:grid-cols-2 2xl:grid-cols-3">
        <ServicesSettings />
        <PromptSettings
            v-show="
                [
                    SERVICE_TYPE.ANTHROPIC,
                    SERVICE_TYPE.DEEPSEEK,
                    SERVICE_TYPE.GEMINI,
                    SERVICE_TYPE.LOCALAI,
                    SERVICE_TYPE.NANOGPT,
                    SERVICE_TYPE.OPENAI,
                    SERVICE_TYPE.CHUTES
                ].includes(
                    serviceType as
                        | 'openai'
                        | 'anthropic'
                        | 'localai'
                        | 'nanogpt'
                        | 'gemini'
                        | 'deepseek'
                        | 'chutes'
                )
            " />
        <TranslationSettings
            v-show="
                [
                    SERVICE_TYPE.ANTHROPIC,
                    SERVICE_TYPE.DEEPSEEK,
                    SERVICE_TYPE.GEMINI,
                    SERVICE_TYPE.LOCALAI,
                    SERVICE_TYPE.NANOGPT,
                    SERVICE_TYPE.OPENAI,
                    SERVICE_TYPE.CHUTES
                ].includes(
                    serviceType as
                        | 'openai'
                        | 'anthropic'
                        | 'localai'
                        | 'nanogpt'
                        | 'gemini'
                        | 'deepseek'
                        | 'chutes'
                )
            " />
        <CustomAiParameters
            v-show="
                [
                    SERVICE_TYPE.ANTHROPIC,
                    SERVICE_TYPE.DEEPSEEK,
                    SERVICE_TYPE.GEMINI,
                    SERVICE_TYPE.LOCALAI,
                    SERVICE_TYPE.NANOGPT,
                    SERVICE_TYPE.OPENAI,
                    SERVICE_TYPE.CHUTES
                ].includes(
                    serviceType as
                        | 'openai'
                        | 'anthropic'
                        | 'localai'
                        | 'nanogpt'
                        | 'gemini'
                        | 'deepseek'
                        | 'chutes'
                )
            " />
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { SETTINGS, SERVICE_TYPE } from '@/ts'
import { useSettingStore } from '@/store/setting'
import ServicesSettings from '@/components/features/settings/ServicesSettings.vue'
import CustomAiParameters from '@/components/features/settings/CustomAiParameters.vue'
import PromptSettings from '@/components/features/settings/PromptSettings.vue'
import TranslationSettings from '@/components/features/settings/TranslationSettings.vue'

const settingsStore = useSettingStore()

const serviceType = computed(() => settingsStore.getSetting(SETTINGS.SERVICE_TYPE))
</script>

<script setup lang="ts">
import { computed } from 'vue'
import { normalizeServiceKey } from '@/utils/providerMetadata'
import anthropicLogo from '@/assets/providers/anthropic-official.svg'
import deepLLogo from '@/assets/providers/deepl-official.svg'
import libreTranslateLogo from '@/assets/providers/libretranslate-official.svg'
import openAiLogo from '@/assets/providers/openai.svg'

const props = withDefaults(
    defineProps<{
        service: string
        fallback?: 'chart' | 'none'
    }>(),
    {
        service: '',
        fallback: 'chart'
    }
)

const normalizedService = computed(() => {
    return normalizeServiceKey(props.service)
})

const officialLogos: Record<string, string> = {
    anthropic: anthropicLogo,
    deepl: deepLLogo,
    libretranslate: libreTranslateLogo,
    openai: openAiLogo
}

const officialLogoUrl = computed(() => {
    return officialLogos[normalizedService.value] ?? null
})
</script>

<template>
    <div v-if="officialLogoUrl" class="flex h-full w-full items-center justify-center">
        <img
            :src="officialLogoUrl"
            :alt="`${props.service} logo`"
            class="h-full w-full object-contain p-0.5"
            loading="lazy" />
    </div>

    <svg
        v-else-if="props.fallback !== 'none'"
        viewBox="0 0 24 24"
        fill="none"
        xmlns="http://www.w3.org/2000/svg"
        aria-hidden="true">
        <g stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
            <path d="M4 19h16" />
            <path d="M7 16v-4" />
            <path d="M12 16V8" />
            <path d="M17 16v-7" />
        </g>
    </svg>
</template>

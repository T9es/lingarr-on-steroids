<template>
    <div class="space-y-4">
        <h3 class="text-primary-content text-lg font-semibold">
            Configure {{ serviceName }}
        </h3>
        <component
            :is="serviceConfigComponent"
            v-if="serviceConfigComponent"
            @save="onSave" />
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS, SERVICE_TYPE } from '@/ts'
import LibreTranslateConfig from '@/components/features/settings/services/LibreTranslateConfig.vue'
import DeepLConfig from '@/components/features/settings/services/DeepLConfig.vue'
import FreeServiceConfig from '@/components/features/settings/services/FreeServiceConfig.vue'
import AnthropicConfig from '@/components/features/settings/services/AnthropicConfig.vue'
import OpenAiConfig from '@/components/features/settings/services/OpenAiConfig.vue'
import LocalAiConfig from '@/components/features/settings/services/LocalAiConfig.vue'
import GeminiConfig from '@/components/features/settings/services/GeminiConfig.vue'
import DeepSeekConfig from '@/components/features/settings/services/DeepSeekConfig.vue'
import ChutesConfig from '@/components/features/settings/services/ChutesConfig.vue'

const emit = defineEmits<{
    save: []
}>()

const settingsStore = useSettingStore()

const serviceType = computed(
    () => settingsStore.getSetting(SETTINGS.SERVICE_TYPE) as string
)

const serviceName = computed(() => {
    const names: Record<string, string> = {
        [SERVICE_TYPE.LIBRETRANSLATE]: 'LibreTranslate',
        [SERVICE_TYPE.OPENAI]: 'OpenAI',
        [SERVICE_TYPE.ANTHROPIC]: 'Anthropic',
        [SERVICE_TYPE.LOCALAI]: 'LocalAI',
        [SERVICE_TYPE.DEEPL]: 'DeepL',
        [SERVICE_TYPE.GEMINI]: 'Gemini',
        [SERVICE_TYPE.DEEPSEEK]: 'DeepSeek',
        [SERVICE_TYPE.CHUTES]: 'Chutes.ai',
        [SERVICE_TYPE.GOOGLE]: 'Google Translate',
        [SERVICE_TYPE.BING]: 'Bing Translate',
        [SERVICE_TYPE.MICROSOFT]: 'Microsoft Translator',
        [SERVICE_TYPE.YANDEX]: 'Yandex Translate'
    }
    return names[serviceType.value] || 'Service'
})

const serviceConfigComponent = computed(() => {
    switch (serviceType.value) {
        case SERVICE_TYPE.LIBRETRANSLATE:
            return LibreTranslateConfig
        case SERVICE_TYPE.OPENAI:
            return OpenAiConfig
        case SERVICE_TYPE.ANTHROPIC:
            return AnthropicConfig
        case SERVICE_TYPE.LOCALAI:
            return LocalAiConfig
        case SERVICE_TYPE.DEEPL:
            return DeepLConfig
        case SERVICE_TYPE.GEMINI:
            return GeminiConfig
        case SERVICE_TYPE.DEEPSEEK:
            return DeepSeekConfig
        case SERVICE_TYPE.CHUTES:
            return ChutesConfig
        case SERVICE_TYPE.GOOGLE:
        case SERVICE_TYPE.BING:
        case SERVICE_TYPE.MICROSOFT:
        case SERVICE_TYPE.YANDEX:
            return FreeServiceConfig
        default:
            return null
    }
})

function onSave(): void {
    emit('save')
}
</script>

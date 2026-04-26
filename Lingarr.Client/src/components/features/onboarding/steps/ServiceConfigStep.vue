<template>
    <div class="space-y-5">
        <div
            class="rounded-2xl border p-4 sm:p-5"
            :style="{
                borderColor: toRgba(providerMeta.color, 0.35),
                backgroundColor: toRgba(providerMeta.color, 0.08)
            }">
            <div class="flex min-w-0 flex-col gap-4 sm:flex-row sm:items-center">
                <div
                    class="flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl border"
                    :style="{
                        borderColor: toRgba(providerMeta.color, 0.28),
                        backgroundColor: toRgba(providerMeta.color, 0.14)
                    }">
                    <ProviderIcon
                        v-if="hasOfficialProviderLogo(serviceType)"
                        :service="serviceType"
                        fallback="none"
                        class="h-7 w-7"
                        :style="{ color: providerMeta.color }" />
                    <span
                        v-else
                        class="text-lg font-semibold"
                        :style="{ color: providerMeta.color }">
                        {{ providerMeta.label.slice(0, 2) }}
                    </span>
                </div>

                <div class="min-w-0 flex-1 space-y-1">
                    <p
                        class="text-secondary-content text-xs font-semibold tracking-[0.18em] uppercase">
                        {{ translate('onboardingSteps.configureService') }}
                    </p>
                    <h3 class="text-primary-content text-lg font-semibold sm:text-xl">
                        {{
                            translate('onboarding.service.configureSelected', {
                                service: serviceName
                            })
                        }}
                    </h3>
                    <p class="text-secondary-content text-sm leading-6">
                        {{ providerMeta.label }}
                    </p>
                </div>
            </div>
        </div>

        <div
            v-if="serviceConfigComponent"
            class="bg-primary/40 border-secondary/30 rounded-2xl border p-4 sm:p-5">
            <component :is="serviceConfigComponent" @save="onSave" />
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import ProviderIcon from '@/components/icons/ProviderIcon.vue'
import { useSettingStore } from '@/store/setting'
import { SETTINGS, SERVICE_TYPE } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import { getProviderMeta, hasOfficialProviderLogo, toRgba } from '@/utils/providerMetadata'
import LibreTranslateConfig from '@/components/features/settings/services/LibreTranslateConfig.vue'
import DeepLConfig from '@/components/features/settings/services/DeepLConfig.vue'
import FreeServiceConfig from '@/components/features/settings/services/FreeServiceConfig.vue'
import AnthropicConfig from '@/components/features/settings/services/AnthropicConfig.vue'
import OpenAiConfig from '@/components/features/settings/services/OpenAiConfig.vue'
import LocalAiConfig from '@/components/features/settings/services/LocalAiConfig.vue'
import GeminiConfig from '@/components/features/settings/services/GeminiConfig.vue'
import DeepSeekConfig from '@/components/features/settings/services/DeepSeekConfig.vue'
import ChutesConfig from '@/components/features/settings/services/ChutesConfig.vue'
import NanoGptConfig from '@/components/features/settings/services/NanoGptConfig.vue'

const emit = defineEmits<{
    save: []
}>()

const { translate } = useI18n()
const settingsStore = useSettingStore()

const serviceType = computed(() => settingsStore.getSetting(SETTINGS.SERVICE_TYPE) as string)
const providerMeta = computed(() => getProviderMeta(serviceType.value))

const serviceName = computed(() => {
    const nameKeys: Record<string, string> = {
        [SERVICE_TYPE.LIBRETRANSLATE]: 'services.serviceNames.libretranslate',
        [SERVICE_TYPE.OPENAI]: 'services.serviceNames.openai',
        [SERVICE_TYPE.ANTHROPIC]: 'services.serviceNames.anthropic',
        [SERVICE_TYPE.LOCALAI]: 'services.serviceNames.localai',
        [SERVICE_TYPE.DEEPL]: 'services.serviceNames.deepl',
        [SERVICE_TYPE.GEMINI]: 'services.serviceNames.gemini',
        [SERVICE_TYPE.DEEPSEEK]: 'services.serviceNames.deepseek',
        [SERVICE_TYPE.NANOGPT]: 'services.serviceNames.nanogpt',
        [SERVICE_TYPE.CHUTES]: 'services.serviceNames.chutes',
        [SERVICE_TYPE.GOOGLE]: 'services.serviceNames.google',
        [SERVICE_TYPE.BING]: 'services.serviceNames.bing',
        [SERVICE_TYPE.MICROSOFT]: 'services.serviceNames.microsoft',
        [SERVICE_TYPE.YANDEX]: 'services.serviceNames.yandex'
    }

    const nameKey = nameKeys[serviceType.value]
    return nameKey ? translate(nameKey) : translate('onboardingSteps.configureService')
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
        case SERVICE_TYPE.NANOGPT:
            return NanoGptConfig
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

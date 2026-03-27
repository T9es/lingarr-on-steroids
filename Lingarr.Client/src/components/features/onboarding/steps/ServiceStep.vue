<template>
    <div class="space-y-6 lg:space-y-7">
        <section
            v-for="group in serviceGroups"
            :key="group.titleKey"
            class="bg-primary/35 border-secondary/30 rounded-2xl border p-4 sm:p-5">
            <div class="mb-4 space-y-1">
                <h3 class="text-primary-content text-lg font-semibold">
                    {{ translate(group.titleKey) }}
                </h3>
                <p class="text-secondary-content text-sm leading-6">
                    {{ translate(group.descriptionKey) }}
                </p>
            </div>

            <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3">
                <button
                    v-for="service in group.services"
                    :key="service.value"
                    :class="[
                        'rounded-xl border p-4 text-left transition-all duration-200 hover:-translate-y-0.5',
                        selectedService === service.value
                            ? 'bg-secondary/55 shadow-lg'
                            : 'bg-primary/55 hover:bg-secondary/45'
                    ]"
                    :style="getCardStyle(service, selectedService === service.value)"
                    @click="selectService(service.value)">
                    <div class="flex min-w-0 items-start gap-3">
                        <div
                            class="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border"
                            :style="getIconShellStyle(service)">
                            <ProviderIcon
                                v-if="hasOfficialProviderLogo(service.providerKey)"
                                :service="service.providerKey"
                                fallback="none"
                                class="h-5 w-5"
                                :style="{ color: getProviderMeta(service.providerKey).color }" />
                            <span
                                v-else
                                class="text-sm font-semibold tracking-wide"
                                :style="{ color: getProviderMeta(service.providerKey).color }">
                                {{ getProviderMeta(service.providerKey).label.slice(0, 2) }}
                            </span>
                        </div>

                        <div class="min-w-0 flex-1">
                            <span class="text-primary-content block font-medium">
                                {{ translate(service.labelKey) }}
                            </span>
                            <span class="text-secondary-content mt-1 block text-xs leading-5">
                                {{ getProviderMeta(service.providerKey).label }}
                            </span>
                        </div>
                    </div>
                </button>
            </div>
        </section>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import ProviderIcon from '@/components/icons/ProviderIcon.vue'
import { useSettingStore } from '@/store/setting'
import { SERVICE_TYPE, SETTINGS } from '@/ts'
import { useI18n } from '@/plugins/i18n'
import { getProviderMeta, hasOfficialProviderLogo, toRgba } from '@/utils/providerMetadata'

interface ServiceOption {
    value: string
    labelKey: string
    providerKey: string
}

const { translate } = useI18n()
const settingStore = useSettingStore()

// Service groups (alphabetical within each group)
const aiServices: ServiceOption[] = [
    {
        value: SERVICE_TYPE.ANTHROPIC,
        labelKey: 'services.serviceNames.anthropic',
        providerKey: 'anthropic'
    },
    { value: SERVICE_TYPE.CHUTES, labelKey: 'services.serviceNames.chutes', providerKey: 'chutes' },
    {
        value: SERVICE_TYPE.DEEPSEEK,
        labelKey: 'services.serviceNames.deepseek',
        providerKey: 'deepseek'
    },
    { value: SERVICE_TYPE.GEMINI, labelKey: 'services.serviceNames.gemini', providerKey: 'gemini' },
    {
        value: SERVICE_TYPE.LOCALAI,
        labelKey: 'services.serviceNames.localai',
        providerKey: 'localai'
    },
    { value: SERVICE_TYPE.OPENAI, labelKey: 'services.serviceNames.openai', providerKey: 'openai' }
]

const cloudServices: ServiceOption[] = [
    { value: SERVICE_TYPE.DEEPL, labelKey: 'services.serviceNames.deepl', providerKey: 'deepl' },
    {
        value: SERVICE_TYPE.LIBRETRANSLATE,
        labelKey: 'services.serviceNames.libretranslate',
        providerKey: 'libretranslate'
    }
]

const freeServices: ServiceOption[] = [
    { value: SERVICE_TYPE.BING, labelKey: 'services.serviceNames.bing', providerKey: 'bing' },
    { value: SERVICE_TYPE.GOOGLE, labelKey: 'services.serviceNames.google', providerKey: 'google' },
    {
        value: SERVICE_TYPE.MICROSOFT,
        labelKey: 'services.serviceNames.microsoft',
        providerKey: 'microsoft'
    },
    { value: SERVICE_TYPE.YANDEX, labelKey: 'services.serviceNames.yandex', providerKey: 'yandex' }
]

const serviceGroups = [
    {
        titleKey: 'onboarding.service.aiServices',
        descriptionKey: 'onboarding.service.aiServicesDescription',
        services: aiServices
    },
    {
        titleKey: 'onboarding.service.cloudApis',
        descriptionKey: 'onboarding.service.cloudApisDescription',
        services: cloudServices
    },
    {
        titleKey: 'onboarding.service.freeWebApis',
        descriptionKey: 'onboarding.service.freeWebApisDescription',
        services: freeServices
    }
]

// Computed property for selected service
const selectedService = computed({
    get: () => settingStore.getSetting(SETTINGS.SERVICE_TYPE) as string,
    set: (value: string) => {
        settingStore.updateSetting(SETTINGS.SERVICE_TYPE, value, true)
    }
})

// Method to select a service
function selectService(value: string): void {
    selectedService.value = value
}

function getCardStyle(service: ServiceOption, isSelected: boolean): Record<string, string> {
    const meta = getProviderMeta(service.providerKey)

    return {
        borderColor: isSelected ? toRgba(meta.color, 0.7) : toRgba(meta.color, 0.35),
        backgroundColor: isSelected ? toRgba(meta.color, 0.14) : toRgba(meta.color, 0.06),
        boxShadow: isSelected ? `0 16px 40px ${toRgba(meta.color, 0.18)}` : 'none'
    }
}

function getIconShellStyle(service: ServiceOption): Record<string, string> {
    const meta = getProviderMeta(service.providerKey)

    return {
        borderColor: toRgba(meta.color, 0.24),
        backgroundColor: toRgba(meta.color, 0.12)
    }
}
</script>

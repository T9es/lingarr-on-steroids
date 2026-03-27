<template>
    <div class="space-y-6">
        <!-- AI Services Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('onboarding.service.aiServices') }}
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                {{ translate('onboarding.service.aiServicesDescription') }}
            </p>
            <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                <button
                    v-for="service in aiServices"
                    :key="service.value"
                    :class="[
                        'border-accent rounded-md border p-4 text-left transition-all hover:-translate-y-1 hover:shadow-lg',
                        selectedService === service.value
                            ? 'bg-accent/20 ring-accent ring-2'
                            : 'hover:bg-accent/10'
                    ]"
                    @click="selectService(service.value)">
                    <span class="text-primary-content block font-medium">
                        {{ translate(service.labelKey) }}
                    </span>
                </button>
            </div>
        </div>

        <!-- Cloud APIs Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('onboarding.service.cloudApis') }}
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                {{ translate('onboarding.service.cloudApisDescription') }}
            </p>
            <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                <button
                    v-for="service in cloudServices"
                    :key="service.value"
                    :class="[
                        'border-accent rounded-md border p-4 text-left transition-all hover:-translate-y-1 hover:shadow-lg',
                        selectedService === service.value
                            ? 'bg-accent/20 ring-accent ring-2'
                            : 'hover:bg-accent/10'
                    ]"
                    @click="selectService(service.value)">
                    <span class="text-primary-content block font-medium">
                        {{ translate(service.labelKey) }}
                    </span>
                </button>
            </div>
        </div>

        <!-- Free Web APIs Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                {{ translate('onboarding.service.freeWebApis') }}
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                {{ translate('onboarding.service.freeWebApisDescription') }}
            </p>
            <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
                <button
                    v-for="service in freeServices"
                    :key="service.value"
                    :class="[
                        'border-accent rounded-md border p-4 text-left transition-all hover:-translate-y-1 hover:shadow-lg',
                        selectedService === service.value
                            ? 'bg-accent/20 ring-accent ring-2'
                            : 'hover:bg-accent/10'
                    ]"
                    @click="selectService(service.value)">
                    <span class="text-primary-content block font-medium">
                        {{ translate(service.labelKey) }}
                    </span>
                </button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useSettingStore } from '@/store/setting'
import { SERVICE_TYPE, SETTINGS } from '@/ts'
import { useI18n } from '@/plugins/i18n'

const { translate } = useI18n()
const settingStore = useSettingStore()

// Service groups (alphabetical within each group)
const aiServices = [
    { value: SERVICE_TYPE.ANTHROPIC, labelKey: 'services.serviceNames.anthropic' },
    { value: SERVICE_TYPE.CHUTES, labelKey: 'services.serviceNames.chutes' },
    { value: SERVICE_TYPE.DEEPSEEK, labelKey: 'services.serviceNames.deepseek' },
    { value: SERVICE_TYPE.GEMINI, labelKey: 'services.serviceNames.gemini' },
    { value: SERVICE_TYPE.LOCALAI, labelKey: 'services.serviceNames.localai' },
    { value: SERVICE_TYPE.OPENAI, labelKey: 'services.serviceNames.openai' }
]

const cloudServices = [
    { value: SERVICE_TYPE.DEEPL, labelKey: 'services.serviceNames.deepl' },
    { value: SERVICE_TYPE.LIBRETRANSLATE, labelKey: 'services.serviceNames.libretranslate' }
]

const freeServices = [
    { value: SERVICE_TYPE.BING, labelKey: 'services.serviceNames.bing' },
    { value: SERVICE_TYPE.GOOGLE, labelKey: 'services.serviceNames.google' },
    { value: SERVICE_TYPE.MICROSOFT, labelKey: 'services.serviceNames.microsoft' },
    { value: SERVICE_TYPE.YANDEX, labelKey: 'services.serviceNames.yandex' }
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
</script>

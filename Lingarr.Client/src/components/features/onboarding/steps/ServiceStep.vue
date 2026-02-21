<template>
    <div class="space-y-6">
        <!-- AI Services Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                AI Services
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                Support batch translation for cost-effective processing
            </p>
            <div class="grid grid-cols-2 gap-2">
                <button
                    v-for="service in aiServices"
                    :key="service.value"
                    :class="[
                        'border-accent rounded-md border p-3 text-left transition-colors',
                        selectedService === service.value
                            ? 'bg-accent/20'
                            : 'hover:bg-accent/10'
                    ]"
                    @click="selectService(service.value)">
                    <span class="text-primary-content text-sm font-medium">
                        {{ service.label }}
                    </span>
                </button>
            </div>
        </div>

        <!-- Cloud APIs Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                Cloud APIs
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                Professional translation services with API keys
            </p>
            <div class="grid grid-cols-2 gap-2">
                <button
                    v-for="service in cloudServices"
                    :key="service.value"
                    :class="[
                        'border-accent rounded-md border p-3 text-left transition-colors',
                        selectedService === service.value
                            ? 'bg-accent/20'
                            : 'hover:bg-accent/10'
                    ]"
                    @click="selectService(service.value)">
                    <span class="text-primary-content text-sm font-medium">
                        {{ service.label }}
                    </span>
                </button>
            </div>
        </div>

        <!-- Free Web APIs Section -->
        <div>
            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                Free Web APIs
            </h3>
            <p class="text-secondary-content mb-3 text-sm">
                No API key required, but rate-limited
            </p>
            <div class="grid grid-cols-2 gap-2">
                <button
                    v-for="service in freeServices"
                    :key="service.value"
                    :class="[
                        'border-accent rounded-md border p-3 text-left transition-colors',
                        selectedService === service.value
                            ? 'bg-accent/20'
                            : 'hover:bg-accent/10'
                    ]"
                    @click="selectService(service.value)">
                    <span class="text-primary-content text-sm font-medium">
                        {{ service.label }}
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

const settingStore = useSettingStore()

// Service groups (alphabetical within each group)
const aiServices = [
    { value: SERVICE_TYPE.ANTHROPIC, label: 'Anthropic' },
    { value: SERVICE_TYPE.CHUTES, label: 'Chutes.ai' },
    { value: SERVICE_TYPE.DEEPSEEK, label: 'DeepSeek' },
    { value: SERVICE_TYPE.GEMINI, label: 'Gemini' },
    { value: SERVICE_TYPE.LOCALAI, label: 'LocalAI' },
    { value: SERVICE_TYPE.OPENAI, label: 'OpenAI' }
]

const cloudServices = [
    { value: SERVICE_TYPE.DEEPL, label: 'DeepL' },
    { value: SERVICE_TYPE.LIBRETRANSLATE, label: 'LibreTranslate' }
]

const freeServices = [
    { value: SERVICE_TYPE.BING, label: 'Bing' },
    { value: SERVICE_TYPE.GOOGLE, label: 'Google' },
    { value: SERVICE_TYPE.MICROSOFT, label: 'Microsoft' },
    { value: SERVICE_TYPE.YANDEX, label: 'Yandex' }
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

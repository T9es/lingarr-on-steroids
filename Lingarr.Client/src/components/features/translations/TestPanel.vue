<template>
    <div class="w-full">
        <!-- Active Test Info -->
        <div v-if="testStore.activeTest" class="bg-tertiary mb-4 rounded-lg p-4">
            <div class="flex items-center justify-between">
                <div>
                    <h2 class="text-lg font-semibold">{{ testStore.activeTest.title }}</h2>
                    <p class="text-secondary-content text-sm">
                        {{ testStore.activeTest.subtitlePath }}
                    </p>
                    <div class="mt-2 flex gap-2">
                        <BadgeComponent classes="text-primary-content border-accent bg-secondary">
                            {{ testStore.activeTest.sourceLanguage.toUpperCase() }}
                        </BadgeComponent>
                        <span class="text-secondary-content">→</span>
                        <BadgeComponent classes="text-primary-content border-accent bg-secondary">
                            {{ testStore.activeTest.targetLanguage.toUpperCase() }}
                        </BadgeComponent>
                    </div>
                </div>
                <div class="flex gap-2">
                    <button
                        v-if="!testStore.isRunning"
                        class="bg-accent hover:bg-accent/80 cursor-pointer rounded px-4 py-2 text-sm font-medium text-white transition"
                        @click="testStore.startTest()">
                        {{ translate('translations.startTest') }}
                    </button>
                    <button
                        v-else
                        class="bg-error hover:bg-error/80 cursor-pointer rounded px-4 py-2 text-sm font-medium text-white transition"
                        @click="testStore.cancelTest()">
                        {{ translate('translations.cancel') }}
                    </button>
                </div>
            </div>
        </div>

        <!-- No Active Test -->
        <div v-else class="bg-tertiary mb-4 rounded-lg p-8 text-center">
            <p class="text-secondary-content">
                {{ translate('translations.noTestSelected') }}
            </p>
        </div>

        <!-- Results Panel -->
        <div
            v-if="testStore.result"
            class="mb-4 rounded-lg p-4"
            :class="
                testStore.result.success
                    ? 'bg-success/20 border-success border'
                    : 'bg-error/20 border-error border'
            ">
            <h2 class="mb-2 text-lg font-semibold">
                {{
                    testStore.result.success
                        ? translate('translations.testSuccess')
                        : translate('translations.testFailed')
                }}
            </h2>
            <div class="text-sm">
                <p v-if="testStore.result.errorMessage" class="text-error">
                    {{ testStore.result.errorMessage }}
                </p>
                <p v-if="testStore.result.totalSubtitles">
                    {{ translate('translations.translated') }}:
                    {{ testStore.result.translatedCount }}/{{ testStore.result.totalSubtitles }}
                </p>
                <p v-if="testStore.result.duration">
                    {{ translate('translations.duration') }}:
                    {{ testStore.result.duration.toFixed(1) }}s
                </p>
            </div>
        </div>

        <!-- Log Console -->
        <div class="bg-secondary overflow-hidden rounded-lg">
            <div class="bg-tertiary flex items-center justify-between px-4 py-2">
                <h2 class="text-sm font-semibold">{{ translate('translations.logs') }}</h2>
                <button
                    class="bg-warning hover:bg-warning/80 cursor-pointer rounded px-2 py-1 text-xs text-white transition"
                    @click="testStore.clearLogs()">
                    {{ translate('translations.clearLogs') }}
                </button>
            </div>

            <div
                ref="logContainer"
                class="bg-primary h-[50vh] overflow-y-auto p-2 font-mono text-xs">
                <div
                    v-if="testStore.logs.length === 0"
                    class="flex h-full items-center justify-center text-gray-500">
                    {{ translate('translations.waitingForLogs') }}
                </div>

                <div
                    v-for="(log, index) in testStore.logs"
                    :key="index"
                    class="border-secondary/30 border-b py-1">
                    <span class="mr-2 text-gray-400">{{ formatTime(log.timestamp) }}</span>
                    <span :class="getLogLevelClass(log.level)" class="mr-2 font-semibold">
                        [{{ log.level }}]
                    </span>
                    <span>{{ log.message }}</span>
                    <div v-if="log.details" class="ml-4 text-xs whitespace-pre-wrap text-gray-500">
                        {{ log.details }}
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import { ref, watch, nextTick } from 'vue'
import { useI18n } from '@/plugins/i18n'
import { useTestTranslationStore } from '@/store/testTranslation'
import BadgeComponent from '@/components/common/BadgeComponent.vue'

const { translate } = useI18n()
const testStore = useTestTranslationStore()
const logContainer = ref<HTMLElement | null>(null)

function formatTime(timestamp: string): string {
    const date = new Date(timestamp)
    return date.toLocaleTimeString()
}

function getLogLevelClass(level: string): string {
    switch (level.toUpperCase()) {
        case 'ERROR':
            return 'text-red-500'
        case 'WARNING':
            return 'text-orange-500'
        case 'INFORMATION':
            return 'text-green-500'
        default:
            return 'text-blue-500'
    }
}

// Auto-scroll to bottom when new logs arrive
watch(
    () => testStore.logs.length,
    async () => {
        await nextTick()
        if (logContainer.value) {
            logContainer.value.scrollTop = logContainer.value.scrollHeight
        }
    }
)
</script>

<template>
    <div class="space-y-3">
        <div class="bg-tertiary rounded-lg p-4">
            <div class="grid grid-cols-4 gap-4 text-center">
                <div>
                    <div
                        class="text-2xl font-bold"
                        :class="result.success ? 'text-success' : 'text-error'">
                        {{ result.success ? '✓' : '✗' }}
                    </div>
                    <div class="text-secondary-content text-xs">Status</div>
                </div>
                <div>
                    <div class="text-2xl font-bold">{{ result.durationSeconds?.toFixed(1) }}s</div>
                    <div class="text-secondary-content text-xs">Duration</div>
                </div>
                <div>
                    <div class="text-2xl font-bold">
                        {{ result.translatedLines }}/{{ result.totalLines }}
                    </div>
                    <div class="text-secondary-content text-xs">Lines</div>
                </div>
                <div>
                    <div class="text-2xl font-bold">{{ totalTokens ?? '—' }}</div>
                    <div class="text-secondary-content text-xs">Tokens</div>
                </div>
            </div>
        </div>

        <details v-if="apiCalls.length > 0" class="bg-secondary rounded-lg">
            <summary class="cursor-pointer px-4 py-3 font-medium">
                {{ translate('translationTest.debugPanel.apiCalls') }} ({{ apiCalls.length }})
            </summary>
            <div class="border-t border-secondary/30 px-4 py-2">
                <div
                    v-for="(call, i) in apiCalls"
                    :key="i"
                    class="border-b border-secondary/20 py-2 last:border-0">
                    <div class="flex items-center justify-between">
                        <span class="text-secondary-content text-sm">
                            {{ translate('translationTest.debugPanel.call') }} #{{ i + 1 }}
                        </span>
                        <span class="text-xs">{{ call.durationMs }}ms</span>
                    </div>
                    <details class="mt-2">
                        <summary class="text-accent text-xs cursor-pointer">
                            {{ translate('translationTest.debugPanel.viewRequest') }}
                        </summary>
                        <pre
                            class="bg-primary mt-1 max-h-40 overflow-auto rounded p-2 text-xs whitespace-pre-wrap">{{ call.requestBody || 'N/A' }}</pre>
                    </details>
                    <details class="mt-1">
                        <summary class="text-accent text-xs cursor-pointer">
                            {{ translate('translationTest.debugPanel.viewResponse') }}
                        </summary>
                        <pre
                            class="bg-primary mt-1 max-h-40 overflow-auto rounded p-2 text-xs whitespace-pre-wrap">{{ call.responseBody || 'N/A' }}</pre>
                    </details>
                </div>
            </div>
        </details>

        <details v-if="lineResults.length > 0" class="bg-secondary rounded-lg">
            <summary class="cursor-pointer px-4 py-3 font-medium">
                {{ translate('translationTest.debugPanel.lineResults') }}
            </summary>
            <div class="border-t border-secondary/30">
                <div class="max-h-64 overflow-y-auto">
                    <div
                        v-for="line in lineResults"
                        :key="line.position"
                        class="hover:bg-tertiary grid grid-cols-12 gap-2 px-4 py-2 text-xs">
                        <span class="text-secondary-content col-span-1">{{ line.position }}</span>
                        <span class="col-span-5 truncate" :title="line.original">{{
                            line.original
                        }}</span>
                        <span
                            class="col-span-5 truncate"
                            :class="line.success ? '' : 'text-error'"
                            :title="line.translated || line.error">
                            {{ line.translated || line.error }}
                        </span>
                        <span class="text-secondary-content col-span-1 text-right"
                            >{{ line.durationMs?.toFixed(0) }}ms</span
                        >
                    </div>
                </div>
            </div>
        </details>

        <details v-if="Object.keys(timings).length > 0" class="bg-secondary rounded-lg">
            <summary class="cursor-pointer px-4 py-3 font-medium">
                {{ translate('translationTest.debugPanel.timing') }}
            </summary>
            <div class="border-t border-secondary/30 px-4 py-2">
                <div
                    v-for="(ms, step) in timings"
                    :key="step"
                    class="flex justify-between py-1 text-sm">
                    <span class="text-secondary-content">{{ step }}</span>
                    <span>{{ ms }}ms</span>
                </div>
            </div>
        </details>
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from '@/plugins/i18n'

interface TestResultDetail {
    id: number
    success: boolean
    totalLines: number
    translatedLines: number
    durationSeconds: number
    tokenUsagePrompt?: number
    tokenUsageCompletion?: number
    apiCallsJson?: string
    lineResultsJson?: string
    timingJson?: string
}

const props = defineProps<{
    result: TestResultDetail
}>()

const { translate } = useI18n()

interface ApiCall {
    durationMs: number
    requestBody?: string
    responseBody?: string
}

interface LineResult {
    position: number
    original: string
    translated?: string
    success: boolean
    error?: string
    durationMs?: number
}

const apiCalls = computed<ApiCall[]>(() => {
    if (!props.result.apiCallsJson) return []
    try {
        return JSON.parse(props.result.apiCallsJson)
    } catch {
        return []
    }
})

const lineResults = computed<LineResult[]>(() => {
    if (!props.result.lineResultsJson) return []
    try {
        return JSON.parse(props.result.lineResultsJson)
    } catch {
        return []
    }
})

const timings = computed<Record<string, number>>(() => {
    if (!props.result.timingJson) return {}
    try {
        return JSON.parse(props.result.timingJson)
    } catch {
        return {}
    }
})

const totalTokens = computed(() => {
    const prompt = props.result.tokenUsagePrompt ?? 0
    const completion = props.result.tokenUsageCompletion ?? 0
    return prompt + completion > 0 ? prompt + completion : null
})
</script>
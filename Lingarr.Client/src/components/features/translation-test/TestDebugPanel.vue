<template>
    <div class="space-y-3">
        <div class="bg-tertiary rounded-lg p-4">
            <div class="grid grid-cols-4 gap-4 text-center">
                <div>
                    <div
                        class="text-2xl font-bold"
                        :class="result.success ? 'text-success' : 'text-error'">
                        {{ result.success ? 'OK' : 'ERR' }}
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
                    <div class="text-2xl font-bold">{{ totalTokens ?? '-' }}</div>
                    <div class="text-secondary-content text-xs">Tokens</div>
                </div>
            </div>
        </div>

        <details v-if="apiCalls.length > 0" class="bg-secondary rounded-lg">
            <summary class="cursor-pointer px-4 py-3 font-medium">
                {{ translate('translationTest.debugPanel.apiCalls') }} ({{ apiCalls.length }})
            </summary>
            <div class="border-secondary/30 border-t px-4 py-2">
                <div
                    v-for="(call, index) in apiCalls"
                    :key="index"
                    class="border-secondary/20 border-b py-2 last:border-0">
                    <div class="flex items-center justify-between">
                        <span class="text-secondary-content text-sm">
                            {{ translate('translationTest.debugPanel.call') }} #{{ index + 1 }}
                        </span>
                        <span class="text-xs">{{ call.durationMs?.toFixed(0) ?? '-' }}ms</span>
                    </div>
                    <details class="mt-2">
                        <summary class="text-accent cursor-pointer text-xs">
                            {{ translate('translationTest.debugPanel.viewRequest') }}
                        </summary>
                        <pre
                            class="bg-primary mt-1 max-h-40 overflow-auto rounded p-2 text-xs whitespace-pre-wrap"
                            >{{ call.requestBody || 'N/A' }}</pre
                        >
                    </details>
                    <details class="mt-1">
                        <summary class="text-accent cursor-pointer text-xs">
                            {{ translate('translationTest.debugPanel.viewResponse') }}
                        </summary>
                        <pre
                            class="bg-primary mt-1 max-h-40 overflow-auto rounded p-2 text-xs whitespace-pre-wrap"
                            >{{ call.responseBody || 'N/A' }}</pre
                        >
                    </details>
                </div>
            </div>
        </details>

        <details v-if="lineResults.length > 0" class="bg-secondary rounded-lg" open>
            <summary class="cursor-pointer px-4 py-3 font-medium">
                {{ translate('translationTest.debugPanel.lineResults') }} ({{ lineResults.length }})
            </summary>
            <div class="border-secondary/30 border-t px-4 py-3">
                <div class="flex flex-wrap items-center justify-between gap-3">
                    <p class="text-secondary-content text-sm">
                        {{
                            t(
                                'translationTest.debugPanel.compareDescription',
                                'Open the side-by-side viewer to inspect original and translated lines together.'
                            )
                        }}
                    </p>
                    <button
                        @click="showCompareModal = true"
                        class="bg-accent text-primary-content rounded px-3 py-2 text-sm">
                        {{ t('translationTest.debugPanel.openCompare', 'Open Compare View') }}
                    </button>
                </div>
            </div>
        </details>

        <details v-if="Object.keys(timings).length > 0" class="bg-secondary rounded-lg">
            <summary class="cursor-pointer px-4 py-3 font-medium">
                {{ translate('translationTest.debugPanel.timing') }}
            </summary>
            <div class="border-secondary/30 border-t px-4 py-2">
                <div
                    v-for="(milliseconds, step) in timings"
                    :key="step"
                    class="flex justify-between py-1 text-sm">
                    <span class="text-secondary-content">{{ step }}</span>
                    <span>{{ milliseconds }}ms</span>
                </div>
            </div>
        </details>
    </div>

    <TestLineCompareModal
        :is-open="showCompareModal"
        :lines="lineResults"
        @close="showCompareModal = false" />
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from '@/plugins/i18n'
import TestLineCompareModal, { type CompareLineResult } from './TestLineCompareModal.vue'

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

interface ApiCall {
    durationMs?: number
    requestBody?: string
    responseBody?: string
}

const props = defineProps<{
    result: TestResultDetail
}>()

const { translate } = useI18n()

const showCompareModal = ref(false)

function t(key: string, fallback: string): string {
    const value = translate(key)
    return value === key ? fallback : value
}

function parseJson<T>(value?: string): T | null {
    if (!value) {
        return null
    }

    try {
        return JSON.parse(value) as T
    } catch {
        return null
    }
}

const apiCalls = computed<ApiCall[]>(() => {
    const parsed = parseJson<Record<string, unknown>[]>(props.result.apiCallsJson)

    if (!parsed) {
        return []
    }

    return parsed.map((call) => ({
        durationMs: Number(call.durationMs ?? call.DurationMs ?? 0),
        requestBody: (call.requestBody ?? call.RequestBody ?? '') as string,
        responseBody: (call.responseBody ?? call.ResponseBody ?? '') as string
    }))
})

const lineResults = computed<CompareLineResult[]>(() => {
    const parsed = parseJson<Record<string, unknown>[]>(props.result.lineResultsJson)

    if (!parsed) {
        return []
    }

    return parsed.map((line) => ({
        position: Number(line.position ?? line.Position ?? 0),
        original: String(line.original ?? line.Original ?? ''),
        translated: String(line.translated ?? line.Translated ?? ''),
        success: Boolean(line.success ?? line.Success),
        error: (line.error ?? line.Error ?? undefined) as string | undefined,
        durationMs:
            line.durationMs !== undefined || line.DurationMs !== undefined
                ? Number(line.durationMs ?? line.DurationMs)
                : undefined,
        startTimeMs:
            line.startTimeMs !== undefined || line.StartTimeMs !== undefined
                ? Number(line.startTimeMs ?? line.StartTimeMs)
                : undefined,
        endTimeMs:
            line.endTimeMs !== undefined || line.EndTimeMs !== undefined
                ? Number(line.endTimeMs ?? line.EndTimeMs)
                : undefined
    }))
})

const timings = computed<Record<string, number>>(() => {
    const parsed = parseJson<Record<string, number>>(props.result.timingJson)
    return parsed || {}
})

const totalTokens = computed(() => {
    const prompt = props.result.tokenUsagePrompt ?? 0
    const completion = props.result.tokenUsageCompletion ?? 0
    return prompt + completion > 0 ? prompt + completion : null
})
</script>

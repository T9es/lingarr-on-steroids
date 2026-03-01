<script setup lang="ts">
import { ref, onMounted, onUnmounted, nextTick } from 'vue'
import { useI18n } from '@/plugins/i18n'
import RefreshIcon from '@/components/icons/RefreshIcon.vue'
import axios from 'axios'

const i18n = useI18n()

interface ErrorLog {
    id: number
    timestamp: string
    type: 'error' | 'warning' | 'info'
    message: string
    source?: string
    details?: string
}

const errors = ref<ErrorLog[]>([])
const isLoading = ref(false)
const localLoading = ref(false)
const expandedId = ref<number | null>(null)
const hasMore = ref(true)
const isLoadingMore = ref(false)
const scrollContainer = ref<HTMLElement | null>(null)
const sentinel = ref<HTMLElement | null>(null)
let observer: IntersectionObserver | null = null

const fetchErrors = async (reset = false) => {
    if (reset) {
        isLoading.value = true
        localLoading.value = true
        errors.value = []
        hasMore.value = true
    }

    try {
        const offset = reset ? 0 : errors.value.length
        const response = await axios.get(`/api/dashboard/errors?limit=20&offset=${offset}`)
        const newErrors = response.data || []

        if (reset) {
            errors.value = newErrors
        } else {
            errors.value = [...errors.value, ...newErrors]
        }

        hasMore.value = newErrors.length === 20
    } catch (e) {
        console.error('Failed to fetch error log:', e)
    } finally {
        isLoading.value = false
        isLoadingMore.value = false
        if (reset) {
            setTimeout(() => {
                localLoading.value = false
            }, 500)
        }
    }
}

const loadMore = async () => {
    if (isLoadingMore.value || !hasMore.value) return
    isLoadingMore.value = true
    await fetchErrors(false)
}

const setupObserver = () => {
    if (observer) observer.disconnect()

    observer = new IntersectionObserver(
        (entries) => {
            if (entries[0].isIntersecting && hasMore.value && !isLoadingMore.value) {
                loadMore()
            }
        },
        { root: scrollContainer.value, threshold: 0.1 }
    )

    if (sentinel.value) {
        observer.observe(sentinel.value)
    }
}

onMounted(async () => {
    await fetchErrors(true)
    await nextTick()
    setupObserver()
})

onUnmounted(() => {
    if (observer) {
        observer.disconnect()
    }
})

const getTypeColor = (type: string): string => {
    switch (type) {
        case 'error':
            return 'text-red-400 bg-red-500/10'
        case 'warning':
            return 'text-yellow-400 bg-yellow-500/10'
        case 'info':
            return 'text-accent bg-accent/10'
        default:
            return 'text-secondary-content bg-secondary/30'
    }
}

const toggleExpand = (id: number) => {
    expandedId.value = expandedId.value === id ? null : id
}

const errorCount = () => errors.value.filter((e) => e.type === 'error').length
const warningCount = () => errors.value.filter((e) => e.type === 'warning').length
</script>

<template>
    <div class="flex h-full flex-col">
        <div class="mb-4 flex items-center justify-between">
            <div class="flex items-center gap-3">
                <h3 class="text-primary-content/70 text-sm font-medium">
                    {{ i18n.translate('statistics.errorLog') }}
                </h3>
                <div class="flex gap-2 text-xs">
                    <span v-if="errorCount() > 0" class="text-red-400">
                        {{ errorCount() }} {{ i18n.translate('statistics.errors') }}
                    </span>
                    <span v-if="warningCount() > 0" class="text-yellow-400">
                        {{ warningCount() }} {{ i18n.translate('statistics.warnings') }}
                    </span>
                </div>
            </div>
            <button
                @click="fetchErrors(true)"
                :disabled="isLoading"
                class="text-secondary-content hover:text-primary-content p-1 transition-colors"
                :title="i18n.translate('statistics.refresh')">
                <RefreshIcon
                    class="h-4 w-4"
                    :class="{ 'animate-spin': localLoading || isLoading }" />
            </button>
        </div>

        <div
            v-if="isLoading && errors.length === 0"
            class="text-secondary-content py-8 text-center text-sm italic opacity-70">
            {{ i18n.translate('common.loading') }}
        </div>

        <div
            v-else-if="errors.length === 0"
            class="text-secondary-content py-8 text-center text-sm italic opacity-70">
            {{ i18n.translate('statistics.noErrors') }}
        </div>

        <div v-else ref="scrollContainer" class="min-h-0 flex-1 space-y-3 overflow-y-auto pr-1">
            <div
                v-for="error in errors"
                :key="error.id"
                class="border-secondary/20 cursor-pointer border-b pb-3 transition-opacity last:border-0 last:pb-0 hover:opacity-80"
                @click="toggleExpand(error.id)">
                <div class="flex items-start justify-between">
                    <div class="min-w-0 flex-1">
                        <div class="text-primary-content truncate text-sm font-medium">
                            {{ error.message }}
                        </div>
                        <div class="text-secondary-content mt-0.5 text-xs">
                            {{ error.timestamp }}
                            <span v-if="error.source">• {{ error.source }}</span>
                        </div>
                    </div>
                    <span
                        class="ml-2 shrink-0 rounded px-2 py-0.5 text-xs font-medium uppercase"
                        :class="getTypeColor(error.type)">
                        {{ error.type }}
                    </span>
                </div>

                <div
                    v-if="expandedId === error.id && error.details"
                    class="text-secondary-content border-secondary/20 mt-2 border-t pt-2 text-xs">
                    {{ error.details }}
                </div>
            </div>

            <div ref="sentinel" class="h-4">
                <div v-if="isLoadingMore" class="text-primary-content/50 text-center text-xs">
                    {{ i18n.translate('common.loading') }}
                </div>
                <div
                    v-else-if="!hasMore && errors.length > 20"
                    class="text-primary-content/30 text-center text-xs">
                    No more errors
                </div>
            </div>
        </div>
    </div>
</template>

<template>
    <PageLayout>
        <div class="w-full">
            <!-- Tabs -->
            <div class="bg-tertiary border-accent flex border-b">
                <button
                    class="px-6 py-3 font-medium transition-colors"
                    :class="
                        activeTab === 'list'
                            ? 'border-accent border-b-2 text-accent'
                            : 'text-secondary-content hover:text-primary-content'
                    "
                    @click="activeTab = 'list'">
                    {{ translate('translations.tabList') }}
                </button>
                <button
                    class="relative px-6 py-3 font-medium transition-colors"
                    :class="
                        activeTab === 'test'
                            ? 'border-accent border-b-2 text-accent'
                            : 'text-secondary-content hover:text-primary-content'
                    "
                    @click="activeTab = 'test'">
                    {{ translate('translations.tabRunTest') }}
                    <span
                        v-if="testStore.isRunning"
                        class="bg-accent absolute -right-1 -top-1 h-2 w-2 animate-pulse rounded-full"></span>
                </button>
            </div>

            <!-- List Tab Content -->
            <div v-show="activeTab === 'list'">
                <!-- Search and Filters -->
                <div class="bg-tertiary flex flex-wrap items-center justify-between gap-2 p-4">
                    <SearchComponent v-model="filter" />
                    <div
                        class="flex w-full flex-col gap-2 md:w-fit md:flex-row md:justify-between md:space-x-2">
                        <button
                            v-if="isSelectMode"
                            class="border-accent text-primary-content hover:text-primary-content/50 cursor-pointer rounded-md border px-2 py-1 transition-colors"
                            @click="handleDelete">
                            {{ translate('translations.delete') }}
                            ({{ translationRequestStore.selectedRequests.length }})
                        </button>
                        <button
                            class="border-accent text-primary-content hover:text-primary-content/50 cursor-pointer rounded-md border px-2 py-1 transition-colors"
                            @click="toggleSelectMode">
                            {{
                                isSelectMode
                                    ? translate('translations.cancel')
                                    : translate('translations.select')
                            }}
                        </button>
                        <SortControls
                            v-model="filter"
                            :options="[
                                {
                                    label: translate('common.sortByAdded'),
                                    value: 'CreatedAt'
                                },
                                {
                                    label: translate('common.sortByCompleted'),
                                    value: 'CompletedAt'
                                },
                                {
                                    label: translate('common.sortByTitle'),
                                    value: 'Title'
                                }
                            ]" />
                    </div>
                </div>

                <div class="w-full space-y-4 px-4 py-4">
                    <!-- Active translations -->
                    <div
                        v-if="inProgressRequests.length"
                        class="relative overflow-hidden rounded-lg border border-blue-500/30 bg-gradient-to-br from-blue-500/10 via-secondary to-purple-500/10 p-4 shadow-lg backdrop-blur-sm">
                        <div class="absolute inset-0 bg-gradient-to-r from-blue-500/5 to-purple-500/5 animate-pulse" />
                        <div class="relative mb-4 flex items-center justify-between">
                            <div class="flex items-center gap-3">
                                <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-blue-500 to-purple-500">
                                    <span class="text-sm font-bold text-white">▶</span>
                                </div>
                                <h2 class="text-base font-bold uppercase tracking-wide">
                                    {{ translate('common.statusInProgress') }}
                                </h2>
                            </div>
                            <span class="rounded-full bg-gradient-to-r from-blue-500 to-purple-500 px-3 py-1 text-xs font-bold text-white shadow-lg">
                                {{ inProgressRequests.length }}
                                {{ translate('common.items') }}
                            </span>
                        </div>
                        <div class="relative space-y-3">
                            <div
                                v-for="item in inProgressRequests"
                                :key="`active-${item.id}`"
                                class="group relative overflow-hidden rounded-lg border border-blue-500/20 bg-tertiary/80 px-4 py-3 shadow-md transition-all duration-300 hover:border-blue-500/40 hover:shadow-blue-500/10 md:flex md:items-center md:justify-between">
                                <div class="space-y-1">
                                    <div class="flex items-center gap-2">
                                        <span class="font-semibold">
                                            {{ item.title }}
                                        </span>
                                        <BadgeComponent
                                            v-if="item.isPriorityMedia"
                                            classes="border-accent bg-accent text-xs text-primary-content">
                                            {{ translate('translations.priority') }}
                                        </BadgeComponent>
                                        <BadgeComponent classes="text-primary-content border-accent bg-secondary text-xs">
                                            {{ item.sourceLanguage.toUpperCase() }} →
                                            {{ item.targetLanguage.toUpperCase() }}
                                        </BadgeComponent>
                                    </div>
                                    <div class="text-secondary-content text-xs">
                                        <TranslationStatus :translation-status="item.status" />
                                    </div>
                                </div>
                                <div class="flex w-full items-center gap-3 md:w-1/2">
                                    <TranslationProgress :progress="item.progress ?? 0" />
                                    <span class="min-w-[3.5rem] rounded-full bg-secondary/80 px-2 py-0.5 text-center text-xs font-semibold text-primary-content">
                                        {{ (item.progress ?? 0).toString() }}%
                                    </span>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Failed translations -->
                    <div
                        v-if="failedRequests.length"
                        class="relative overflow-hidden rounded-lg border border-red-500/30 bg-gradient-to-br from-red-500/10 via-secondary to-orange-500/10 p-4 shadow-lg backdrop-blur-sm">
                        <div class="relative mb-4 flex items-center justify-between">
                            <div class="flex items-center gap-3">
                                <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-gradient-to-br from-red-500 to-orange-500">
                                    <span class="text-sm font-bold text-white">✕</span>
                                </div>
                                <h2 class="text-base font-bold uppercase tracking-wide">
                                    {{ translate('common.statusFailed') }}
                                </h2>
                            </div>
                            <button
                                class="flex items-center gap-2 rounded-full bg-gradient-to-r from-red-500 to-orange-500 px-4 py-1.5 text-xs font-bold text-white shadow-lg transition-all duration-300 hover:shadow-red-500/30 hover:scale-105 disabled:cursor-not-allowed disabled:opacity-60"
                                :disabled="retryingFailed"
                                @click="retryAllFailed">
                                <span v-if="retryingFailed" class="animate-spin">⟳</span>
                                <span>{{ translate('common.retry') }} ({{ failedRequests.length }})</span>
                            </button>
                        </div>
                        <div class="max-h-64 space-y-3 overflow-y-auto pr-1 scrollbar-thin scrollbar-thumb-red-500/20 scrollbar-track-transparent">
                            <div
                                v-for="item in failedRequests"
                                :key="`failed-${item.id}`"
                                class="group relative overflow-hidden rounded-lg border border-red-500/20 bg-tertiary/80 px-4 py-3 shadow-md transition-all duration-300 hover:border-red-500/40 md:flex md:items-center md:justify-between">
                                <div>
                                    <div class="flex items-center gap-2">
                                        <span class="font-semibold">
                                            {{ item.title }}
                                        </span>
                                        <BadgeComponent
                                            v-if="item.isPriorityMedia"
                                            classes="border-accent bg-accent text-xs text-primary-content">
                                            {{ translate('translations.priority') }}
                                        </BadgeComponent>
                                    </div>
                                    <div class="mt-1 flex flex-wrap items-center gap-2 text-xs">
                                        <BadgeComponent classes="text-primary-content border-accent bg-secondary">
                                            {{ item.sourceLanguage.toUpperCase() }} →
                                            {{ item.targetLanguage.toUpperCase() }}
                                        </BadgeComponent>
                                        <span class="text-secondary-content">
                                            <TranslationCompletedAt
                                                v-if="item.completedAt"
                                                :completed-at="item.completedAt" />
                                        </span>
                                    </div>
                                </div>
                                <div class="flex items-center gap-2">
                                    <button
                                        class="border-accent hover:bg-accent cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                        :title="translate('translations.viewLogs')"
                                        @click.stop="openLogs(item)">
                                        {{ translate('translations.logs') }}
                                    </button>
                                    <TranslationAction
                                        :status="item.status"
                                        :on-action="(action) => handleAction(item, action)" />
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Full queue table -->
                    <div class="border-accent hidden border-b font-bold md:grid md:grid-cols-12">
                        <div class="col-span-4 px-4 py-2">
                            {{ translate('translations.title') }}
                        </div>
                        <div class="col-span-1 px-4 py-2">{{ translate('translations.source') }}</div>
                        <div class="col-span-1 px-4 py-2">{{ translate('translations.target') }}</div>
                        <div class="col-span-1 px-4 py-2">{{ translate('translations.status') }}</div>
                        <div class="px-4 py-2" :class="isSelectMode ? 'col-span-1' : 'col-span-2'">
                            {{ translate('translations.progress') }}
                        </div>
                        <div class="col-span-1 px-4 py-2">
                            {{ translate('translations.completed') }}
                        </div>
                        <div class="col-span-1 flex justify-end px-4 py-2">
                            <ReloadComponent @toggle:update="translationRequestStore.fetch()" />
                        </div>
                        <div
                            v-if="isSelectMode"
                            class="col-span-1 flex items-center justify-center px-4 py-2">
                            <CheckboxComponent
                                :model-value="translationRequestStore.selectAll"
                                @change="translationRequestStore.toggleSelectAll()" />
                        </div>
                    </div>
                    <div
                        v-for="item in translationRequests.items"
                        :key="item.id"
                        class="md:border-accent rounded-lg py-4 shadow-sm md:grid md:grid-cols-12 md:rounded-none md:border-b md:bg-transparent md:p-0 md:shadow-none">
                        <div class="deletable float-right w-5 md:hidden">
                            <TranslationAction
                                :status="item.status"
                                :on-action="(action) => handleAction(item, action)" />
                        </div>
                        <div class="mb-2 md:col-span-4 md:mb-0 md:px-4 md:py-2">
                            <span :id="`deletable-${item.id}`" class="font-bold md:hidden">
                                {{ translate('translations.title') }}:&nbsp;
                            </span>
                            <div class="flex items-center gap-2">
                                <span
                                    v-if="item.mediaType === MEDIA_TYPE.EPISODE"
                                    v-show-title
                                    class="block cursor-help"
                                    :title="item.title">
                                    {{ item.title }}
                                </span>
                                <span v-else>
                                    {{ item.title }}
                                </span>
                                <BadgeComponent
                                    v-if="item.isPriorityMedia"
                                    classes="border-accent bg-accent text-xs text-primary-content">
                                    {{ translate('translations.priority') }}
                                </BadgeComponent>
                            </div>
                        </div>
                        <div class="mb-2 md:col-span-1 md:mb-0 md:px-4 md:py-2">
                            <span class="font-bold md:hidden">
                                {{ translate('translations.source') }}:&nbsp;
                            </span>
                            <BadgeComponent classes="text-primary-content border-accent bg-secondary">
                                {{ item.sourceLanguage.toUpperCase() }}
                            </BadgeComponent>
                        </div>
                        <div class="mb-2 md:col-span-1 md:mb-0 md:px-4 md:py-2">
                            <span class="font-bold md:hidden">
                                {{ translate('translations.target') }}:&nbsp;
                            </span>
                            <BadgeComponent classes="text-primary-content border-accent bg-secondary">
                                {{ item.targetLanguage.toUpperCase() }}
                            </BadgeComponent>
                        </div>
                        <div class="mb-2 md:col-span-1 md:mb-0 md:px-4 md:py-2">
                            <span class="font-bold md:hidden">
                                {{ translate('translations.status') }}:&nbsp;
                            </span>
                            <TranslationStatus :translation-status="item.status" />
                        </div>
                        <div
                            class="mb-2 flex items-center md:mb-0 md:px-4 md:py-2"
                            :class="isSelectMode ? 'md:col-span-1' : 'md:col-span-2'">
                            <div
                                v-if="item.status === TRANSLATION_STATUS.INPROGRESS"
                                class="w-full">
                                <span class="mr-2 font-bold md:hidden">
                                    {{ translate('translations.progress') }}:&nbsp;
                                </span>
                                <TranslationProgress :progress="item.progress ?? 0" />
                            </div>
                        </div>
                        <div class="mb-2 md:col-span-1 md:mb-0 md:px-4 md:py-2">
                            <span class="font-bold md:hidden">
                                {{ translate('translations.completed') }}:&nbsp;
                            </span>
                            <TranslationCompletedAt
                                v-if="item.completedAt"
                                :completed-at="item.completedAt" />
                        </div>
                        <div
                            class="hidden items-center justify-between md:col-span-1 md:flex md:justify-end md:py-2">
                            <div class="flex items-center gap-1">
                                <!-- Run Test Button -->
                                <button
                                    v-if="item.subtitleToTranslate"
                                    class="border-accent hover:bg-accent cursor-pointer rounded border p-1 transition-colors"
                                    :title="translate('translations.runTest')"
                                    @click.stop="runTestForItem(item)">
                                    <TestIcon class="h-4 w-4" />
                                </button>
                                <!-- View Logs Button (failed translations only) -->
                                <button
                                    v-if="item.status === TRANSLATION_STATUS.FAILED"
                                    class="border-accent hover:bg-accent cursor-pointer rounded border px-2 py-1 text-xs transition-colors"
                                    :title="translate('translations.viewLogs')"
                                    @click.stop="openLogs(item)">
                                    {{ translate('translations.logs') }}
                                </button>
                                <TranslationAction
                                    :status="item.status"
                                    :on-action="(action) => handleAction(item, action)" />
                            </div>
                        </div>
                        <div
                            v-if="isSelectMode"
                            class="col-span-1 flex items-center justify-end py-2 md:justify-center md:px-4">
                            <CheckboxComponent
                                :model-value="
                                    translationRequestStore.selectedRequests.some(
                                        (request) => request.id === item.id
                                    )
                                "
                                @change="translationRequestStore.toggleSelect(item)" />
                        </div>
                    </div>
                </div>
                <PaginationComponent
                    v-if="translationRequests.totalCount"
                    v-model="filter"
                    :total-count="translationRequests.totalCount"
                    :page-size="translationRequests.pageSize" />
            </div>

            <!-- Test Tab Content -->
            <div v-show="activeTab === 'test'" class="p-4">
                <TestPanel />
            </div>

            <!-- Logs Modal -->
            <div
                v-if="logsModalOpen"
                class="bg-black/60 fixed inset-0 z-40 flex items-center justify-center">
                <div class="bg-tertiary max-h-[80vh] w-full max-w-3xl overflow-hidden rounded-lg shadow-lg">
                    <div class="border-accent flex items-center justify-between border-b px-4 py-2">
                        <div>
                            <h2 class="text-lg font-semibold">
                                {{ translate('translations.logs') }}
                            </h2>
                            <p v-if="activeLogRequest" class="text-secondary-content text-xs">
                                {{ activeLogRequest.title }}
                            </p>
                        </div>
                        <button
                            class="border-accent hover:bg-accent cursor-pointer rounded border px-3 py-1 text-xs transition-colors"
                            @click="closeLogs">
                            {{ translate('translations.close') }}
                        </button>
                    </div>
                    <div class="bg-secondary h-[60vh] overflow-y-auto p-3 font-mono text-xs">
                        <div v-if="logsLoading" class="flex h-full items-center justify-center text-gray-400">
                            {{ translate('translations.waitingForLogs') }}
                        </div>
                        <div v-else-if="logsError" class="text-error">
                            {{ logsError }}
                        </div>
                        <div
                            v-else-if="requestLogs.length === 0"
                            class="flex h-full items-center justify-center text-gray-400">
                            {{ translate('translations.noLogs') }}
                        </div>
                        <div v-else class="space-y-1">
                            <div
                                v-for="log in requestLogs"
                                :key="log.id"
                                class="border-secondary/30 border-b pb-1">
                                <span class="mr-2 text-gray-400">
                                    {{ new Date(log.createdAt).toLocaleTimeString() }}
                                </span>
                                <span class="mr-2 font-semibold" :class="getLogLevelClass(log.level)">
                                    [{{ log.level }}]
                                </span>
                                <span>{{ log.message }}</span>
                                <div
                                    v-if="log.details"
                                    class="ml-4 whitespace-pre-wrap text-[0.7rem] text-gray-500">
                                    {{ log.details }}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </PageLayout>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, ComputedRef, computed } from 'vue'
import {
    Hub,
    IFilter,
    IPagedResult,
    ITranslationRequest,
    ITranslationRequestLog,
    MEDIA_TYPE,
    TRANSLATION_ACTIONS,
    TRANSLATION_STATUS
} from '@/ts'
import { useTranslationRequestStore } from '@/store/translationRequest'
import { useTestTranslationStore } from '@/store/testTranslation'
import { useSignalR } from '@/composables/useSignalR'
import useDebounce from '@/composables/useDebounce'
import { useI18n } from '@/plugins/i18n'
import PaginationComponent from '@/components/common/PaginationComponent.vue'
import SortControls from '@/components/common/SortControls.vue'
import SearchComponent from '@/components/common/SearchComponent.vue'
import ReloadComponent from '@/components/common/ReloadComponent.vue'
import TranslationStatus from '@/components/common/TranslationStatus.vue'
import TranslationProgress from '@/components/common/TranslationProgress.vue'
import TranslationAction from '@/components/common/TranslationAction.vue'
import TranslationCompletedAt from '@/components/common/TranslationCompletedAt.vue'
import BadgeComponent from '@/components/common/BadgeComponent.vue'
import PageLayout from '@/components/layout/PageLayout.vue'
import CheckboxComponent from '@/components/common/CheckboxComponent.vue'
import TestPanel from '@/components/features/translations/TestPanel.vue'
import TestIcon from '@/components/icons/TestIcon.vue'

const { translate } = useI18n()
const signalR = useSignalR()
const hubConnection = ref<Hub>()
const translationRequestStore = useTranslationRequestStore()
const testStore = useTestTranslationStore()
const requestActiveCallback = (payload: { count: number }) => {
    translationRequestStore.updateActiveCount(payload.count)
}

const logsModalOpen = ref(false)
const logsLoading = ref(false)
const logsError = ref<string | null>(null)
const activeLogRequest = ref<ITranslationRequest | null>(null)
const requestLogs = ref<ITranslationRequestLog[]>([])
const retryingFailed = ref(false)

const activeTab = ref<'list' | 'test'>('list')

const translationRequests: ComputedRef<IPagedResult<ITranslationRequest>> = computed(
    () => translationRequestStore.getTranslationRequests
)

const inProgressRequests = computed(() =>
    translationRequests.value.items.filter(
        (request) => request.status === TRANSLATION_STATUS.INPROGRESS
    )
)

const failedRequests = computed(() =>
    translationRequests.value.items.filter(
        (request) => request.status === TRANSLATION_STATUS.FAILED
    )
)

const filter: ComputedRef<IFilter> = computed({
    get: () => translationRequestStore.filter,
    set: useDebounce((value: IFilter) => {
        translationRequestStore.setFilter(value)
    }, 300)
})

async function handleAction(translationRequest: ITranslationRequest, action: TRANSLATION_ACTIONS) {
    switch (action) {
        case TRANSLATION_ACTIONS.CANCEL:
            return await translationRequestStore.cancel(translationRequest)
        case TRANSLATION_ACTIONS.REMOVE:
            return await translationRequestStore.remove(translationRequest)
        case TRANSLATION_ACTIONS.RETRY:
            return await translationRequestStore.retry(translationRequest)
        default:
            console.error('unknown translation request action: ' + action)
    }
}

async function openLogs(item: ITranslationRequest) {
    if (item.status !== TRANSLATION_STATUS.FAILED) return

    logsModalOpen.value = true
    logsLoading.value = true
    logsError.value = null
    activeLogRequest.value = item
    requestLogs.value = []

    try {
        requestLogs.value = await translationRequestStore.getLogs(item.id)
    } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Failed to load translation request logs', error)
        logsError.value = translate('translations.loadLogsError')
    } finally {
        logsLoading.value = false
    }
}

function closeLogs() {
    logsModalOpen.value = false
    logsLoading.value = false
    logsError.value = null
    activeLogRequest.value = null
    requestLogs.value = []
}

const retryAllFailed = async () => {
    if (!failedRequests.value.length || retryingFailed.value) return

    retryingFailed.value = true
    try {
        const snapshot = [...failedRequests.value]
        for (const request of snapshot) {
            await translationRequestStore.retry(request)
            await translationRequestStore.remove(request)
        }
        await translationRequestStore.fetch()
    } finally {
        retryingFailed.value = false
    }
}

function runTestForItem(item: ITranslationRequest) {
    if (!item.subtitleToTranslate) return

    testStore.setActiveTest({
        subtitlePath: item.subtitleToTranslate,
        sourceLanguage: item.sourceLanguage,
        targetLanguage: item.targetLanguage,
        title: item.title
    })

    activeTab.value = 'test'
    testStore.startTest()
}

onMounted(async () => {
    await translationRequestStore.fetch()
    await translationRequestStore.getActiveCount()
    hubConnection.value = await signalR.connect(
        'TranslationRequests',
        '/signalr/TranslationRequests'
    )

    await hubConnection.value.joinGroup({ group: 'TranslationRequests' })
    hubConnection.value.on('RequestProgress', translationRequestStore.updateProgress)
    hubConnection.value.on('RequestActive', requestActiveCallback)
})

onUnmounted(async () => {
    hubConnection.value?.off('RequestProgress', translationRequestStore.updateProgress)
    hubConnection.value?.off('RequestActive', requestActiveCallback)
})

const isSelectMode = ref(false)

const toggleSelectMode = () => {
    isSelectMode.value = !isSelectMode.value
    if (!isSelectMode.value) {
        translationRequestStore.clearSelection()
    }
}

const handleDelete = async () => {
    for (const request of translationRequestStore.getSelectedRequests) {
        await translationRequestStore.remove(request)
    }
    translationRequestStore.clearSelection()
    translationRequestStore.fetch()
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
</script>

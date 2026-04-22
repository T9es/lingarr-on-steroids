<template>
    <PageLayout>
        <div class="w-full">
            <!-- Tabs -->
            <div class="bg-tertiary border-accent flex border-b">
                <button
                    class="px-6 py-3 font-medium transition-colors"
                    :class="
                        activeTab === 'list'
                            ? 'border-accent text-accent border-b-2'
                            : 'text-secondary-content hover:text-primary-content'
                    "
                    @click="setActiveTab('list')">
                    {{ translate('translations.tabList') }}
                </button>
                <button
                    class="px-6 py-3 font-medium transition-colors"
                    :class="
                        activeTab === 'uploadWorkspace'
                            ? 'border-accent text-accent border-b-2'
                            : 'text-secondary-content hover:text-primary-content'
                    "
                    @click="setActiveTab('uploadWorkspace')">
                    {{ translate('navigation.uploadWorkspace') }}
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
                    <div class="border-accent bg-secondary rounded-md border p-4 shadow-sm">
                        <div class="mb-3 flex items-center justify-between">
                            <h2 class="text-sm font-semibold tracking-wide uppercase">
                                {{ translate('common.statusInProgress') }}
                            </h2>
                            <span class="text-secondary-content text-xs">
                                {{ inProgressRequests.length }}
                                {{ translate('common.items') }}
                            </span>
                        </div>
                        <div v-if="inProgressRequests.length" class="space-y-3">
                            <div
                                v-for="item in inProgressRequests"
                                :key="`active-${item.id}`"
                                class="border-secondary/40 bg-tertiary flex flex-col gap-2 rounded-md border px-3 py-2 md:flex-row md:items-center md:justify-between">
                                <div class="min-w-0 space-y-1">
                                    <div class="flex min-w-0 flex-wrap items-center gap-2 md:flex-nowrap">
                                        <div class="min-w-0 flex-1 overflow-hidden">
                                            <span
                                                v-if="item.mediaType === MEDIA_TYPE.EPISODE"
                                                v-show-title
                                                class="block min-w-0 flex-1 cursor-help font-semibold"
                                                :title="item.title">
                                                {{ item.title }}
                                            </span>
                                            <span
                                                v-else
                                                class="block min-w-0 truncate font-semibold"
                                                :title="item.title">
                                                {{ item.title }}
                                            </span>
                                        </div>
                                        <div class="flex shrink-0 flex-wrap items-center gap-2">
                                            <BadgeComponent
                                                v-if="item.isPriority"
                                                classes="border-accent bg-accent text-xs text-primary-content">
                                                {{ translate('translations.priority') }}
                                            </BadgeComponent>
                                            <BadgeComponent
                                                classes="border-accent/30 bg-secondary text-xs text-primary-content/80">
                                                {{ getWorkloadLabel(item.workloadKind) }}
                                            </BadgeComponent>
                                            <BadgeComponent
                                                v-if="getWorkloadSourceLabel(item)"
                                                classes="border-accent/20 bg-secondary text-xs text-primary-content/70">
                                                {{ getWorkloadSourceLabel(item) }}
                                            </BadgeComponent>
                                            <BadgeComponent
                                                classes="text-primary-content border-accent bg-secondary text-xs">
                                                {{ item.sourceLanguage.toUpperCase() }} →
                                                {{ item.targetLanguage.toUpperCase() }}
                                            </BadgeComponent>
                                        </div>
                                    </div>
                                    <div class="text-secondary-content text-xs">
                                        <TranslationStatus :translation-status="item.status" />
                                    </div>
                                </div>
                                <div class="flex w-full items-center gap-2 md:w-1/2">
                                    <TranslationProgress :progress="item.progress ?? 0" />
                                    <span
                                        class="text-secondary-content min-w-[3rem] text-right text-xs">
                                        {{ (item.progress ?? 0).toString() }}%
                                    </span>
                                    <TranslationAction
                                        :status="item.status"
                                        :on-action="(action) => handleAction(item, action)" />
                                </div>
                            </div>
                        </div>
                        <div v-else class="text-secondary-content py-4 text-center text-sm">
                            {{ translate('translations.noActiveTranslations') }}
                        </div>
                    </div>

                    <!-- Failed translations -->
                    <div class="border-accent bg-secondary rounded-md border p-4 shadow-sm">
                        <div class="mb-3 flex items-center justify-between">
                            <h2 class="text-sm font-semibold tracking-wide uppercase">
                                {{ translate('common.statusFailed') }}
                            </h2>
                            <div v-if="failedRequests.length" class="flex gap-2">
                                <button
                                    class="cursor-pointer rounded-md border border-red-500/50 px-3 py-1 text-xs text-red-400 transition-colors hover:bg-red-500/20 disabled:cursor-not-allowed disabled:opacity-60"
                                    :disabled="removingFailed || retryingFailed"
                                    @click="showRemoveConfirm = true">
                                    {{ translate('translations.removeAllFailed') }}
                                    ({{ failedRequests.length }})
                                </button>
                                <button
                                    class="border-accent text-primary-content hover:bg-accent cursor-pointer rounded-md border px-3 py-1 text-xs transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                                    :disabled="retryingFailed || removingFailed"
                                    @click="retryAllFailed">
                                    {{ translate('common.retry') }}
                                    ({{ failedRequests.length }})
                                </button>
                            </div>
                            <span v-else class="text-secondary-content text-xs">
                                0 {{ translate('common.items') }}
                            </span>
                        </div>
                        <div
                            v-if="retryResultBanner"
                            class="border-accent/40 bg-tertiary text-secondary-content mb-3 rounded-md border px-3 py-2 text-xs">
                            {{
                                translate('translations.retryResultSummary', {
                                    retried: retryResultBanner.retried,
                                    blocked: retryResultBanner.blocked
                                })
                            }}
                        </div>
                        <div
                            v-if="failedRequests.length"
                            class="max-h-64 space-y-3 overflow-y-auto pr-1">
                            <div
                                v-for="item in failedRequests"
                                :key="`failed-${item.id}`"
                                class="border-secondary/40 bg-tertiary flex flex-col gap-2 rounded-md border px-3 py-2 md:flex-row md:items-center md:justify-between">
                                <div class="min-w-0">
                                    <div class="flex min-w-0 flex-wrap items-center gap-2 md:flex-nowrap">
                                        <div class="min-w-0 flex-1 overflow-hidden">
                                            <span
                                                v-if="item.mediaType === MEDIA_TYPE.EPISODE"
                                                v-show-title
                                                class="block min-w-0 flex-1 cursor-help font-semibold"
                                                :title="item.title">
                                                {{ item.title }}
                                            </span>
                                            <span
                                                v-else
                                                class="block min-w-0 truncate font-semibold"
                                                :title="item.title">
                                                {{ item.title }}
                                            </span>
                                        </div>
                                        <div class="flex shrink-0 flex-wrap items-center gap-2">
                                            <BadgeComponent
                                                v-if="item.isPriority"
                                                classes="border-accent bg-accent text-xs text-primary-content">
                                                {{ translate('translations.priority') }}
                                            </BadgeComponent>
                                            <BadgeComponent
                                                classes="border-accent/30 bg-secondary text-xs text-primary-content/80">
                                                {{ getWorkloadLabel(item.workloadKind) }}
                                            </BadgeComponent>
                                            <BadgeComponent
                                                v-if="getWorkloadSourceLabel(item)"
                                                classes="border-accent/20 bg-secondary text-xs text-primary-content/70">
                                                {{ getWorkloadSourceLabel(item) }}
                                            </BadgeComponent>
                                        </div>
                                    </div>
                                    <div class="mt-1 flex flex-wrap items-center gap-2 text-xs">
                                        <BadgeComponent
                                            classes="text-primary-content border-accent bg-secondary">
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
                        <div v-else class="text-secondary-content py-4 text-center text-sm">
                            {{ translate('translations.noFailedTranslations') }}
                        </div>
                    </div>

                    <!-- Remove All Failed Confirmation Modal -->
                    <div
                        v-if="showRemoveConfirm"
                        class="bg-secondary/80 fixed inset-0 z-50 flex items-center justify-center backdrop-blur-sm">
                        <div
                            class="bg-secondary border-secondary mx-4 max-w-sm rounded-lg border p-6">
                            <h3 class="text-primary-content mb-2 text-lg font-semibold">
                                {{ translate('translations.removeAllFailed') }}
                            </h3>
                            <p class="text-primary-content/70 mb-4 text-sm">
                                {{ translate('translations.removeAllFailedConfirm') }}
                            </p>
                            <div class="flex justify-end gap-2">
                                <button
                                    @click="showRemoveConfirm = false"
                                    class="text-primary-content/60 hover:text-primary-content rounded-md px-4 py-2 text-sm transition-colors hover:bg-white/5">
                                    {{ translate('common.cancel') }}
                                </button>
                                <button
                                    @click="removeAllFailed"
                                    class="text-primary-content rounded-md bg-red-500/80 px-4 py-2 text-sm font-medium transition-colors hover:bg-red-500">
                                    {{ translate('common.confirm') }}
                                </button>
                            </div>
                        </div>
                    </div>

                    <!-- Queued translations -->
                    <div class="border-accent bg-secondary rounded-md border p-4 shadow-sm">
                        <div class="mb-3 flex items-center justify-between">
                            <h2 class="text-sm font-semibold tracking-wide uppercase">
                                {{ translate('common.statusPending') }}
                            </h2>
                            <div class="flex items-center gap-2">
                                <button
                                    v-if="queuedRequests.length"
                                    class="border-accent text-primary-content hover:bg-accent cursor-pointer rounded-md border px-3 py-1 text-xs transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                                    :disabled="reenqueuingQueued"
                                    @click="reenqueueQueued">
                                    {{ translate('translations.reenqueueQueue') }}
                                </button>
                                <button
                                    v-if="queuedRequests.length"
                                    class="border-accent text-primary-content hover:bg-accent cursor-pointer rounded-md border px-3 py-1 text-xs transition-colors disabled:cursor-not-allowed disabled:opacity-60"
                                    :disabled="cancellingQueued"
                                    @click="cancelAllQueued">
                                    {{ translate('translations.cancelAll') }}
                                </button>
                                <span class="text-secondary-content text-xs">
                                    {{ queuedRequests.length }}
                                    {{ translate('common.items') }}
                                </span>
                            </div>
                        </div>

                        <template v-if="queuedRequests.length">
                            <!-- Queue table header -->
                            <div
                                class="border-accent hidden border-b font-bold md:grid md:grid-cols-12">
                                <div class="col-span-5 px-4 py-2">
                                    {{ translate('translations.title') }}
                                </div>
                                <div class="col-span-1 px-4 py-2">
                                    {{ translate('translations.source') }}
                                </div>
                                <div class="col-span-1 px-4 py-2">
                                    {{ translate('translations.target') }}
                                </div>
                                <div class="col-span-1 px-4 py-2">
                                    {{ translate('translations.status') }}
                                </div>
                                <div class="col-span-2 px-4 py-2">
                                    {{ translate('translations.completed') }}
                                </div>
                                <div class="col-span-1 flex justify-end px-4 py-2">
                                    <ReloadComponent
                                        @toggle:update="translationRequestStore.fetch()" />
                                </div>
                                <div
                                    v-if="isSelectMode"
                                    class="col-span-1 flex items-center justify-center px-4 py-2">
                                    <CheckboxComponent
                                        :model-value="translationRequestStore.selectAll"
                                        @change="translationRequestStore.toggleSelectAll()" />
                                </div>
                            </div>

                            <!-- Queue table rows -->
                            <div
                                v-for="item in queuedRequests"
                                :key="item.id"
                                class="md:border-accent rounded-lg py-4 shadow-sm md:grid md:grid-cols-12 md:rounded-none md:border-b md:bg-transparent md:p-0 md:shadow-none">
                                <div class="deletable float-right w-5 md:hidden">
                                    <TranslationAction
                                        :status="item.status"
                                        :on-action="(action) => handleAction(item, action)" />
                                </div>
                                <div
                                    class="mb-2 overflow-hidden md:col-span-5 md:mb-0 md:px-4 md:py-2">
                                    <span :id="`deletable-${item.id}`" class="font-bold md:hidden">
                                        {{ translate('translations.title') }}:&nbsp;
                                    </span>
                                    <div class="flex min-w-0 flex-wrap items-center gap-2 md:flex-nowrap">
                                        <div class="min-w-0 flex-1 overflow-hidden">
                                            <span
                                                v-if="item.mediaType === MEDIA_TYPE.EPISODE"
                                                v-show-title
                                                class="block min-w-0 flex-1 cursor-help"
                                                :title="item.title">
                                                {{ item.title }}
                                            </span>
                                            <span
                                                v-else
                                                class="block min-w-0 truncate"
                                                :title="item.title">
                                                {{ item.title }}
                                            </span>
                                        </div>
                                        <div class="flex shrink-0 flex-wrap items-center gap-2">
                                            <BadgeComponent
                                                v-if="item.isPriority"
                                                classes="border-accent bg-accent text-xs text-primary-content">
                                                {{ translate('translations.priority') }}
                                            </BadgeComponent>
                                            <BadgeComponent
                                                classes="border-accent/30 bg-secondary text-xs text-primary-content/80">
                                                {{ getWorkloadLabel(item.workloadKind) }}
                                            </BadgeComponent>
                                            <BadgeComponent
                                                v-if="getWorkloadSourceLabel(item)"
                                                classes="border-accent/20 bg-secondary text-xs text-primary-content/70">
                                                {{ getWorkloadSourceLabel(item) }}
                                            </BadgeComponent>
                                        </div>
                                    </div>
                                </div>
                                <div class="mb-2 md:col-span-1 md:mb-0 md:px-4 md:py-2">
                                    <span class="font-bold md:hidden">
                                        {{ translate('translations.source') }}:&nbsp;
                                    </span>
                                    <BadgeComponent
                                        classes="text-primary-content border-accent bg-secondary">
                                        {{ item.sourceLanguage.toUpperCase() }}
                                    </BadgeComponent>
                                </div>
                                <div class="mb-2 md:col-span-1 md:mb-0 md:px-4 md:py-2">
                                    <span class="font-bold md:hidden">
                                        {{ translate('translations.target') }}:&nbsp;
                                    </span>
                                    <BadgeComponent
                                        classes="text-primary-content border-accent bg-secondary">
                                        {{ item.targetLanguage.toUpperCase() }}
                                    </BadgeComponent>
                                </div>
                                <div class="mb-2 md:col-span-1 md:mb-0 md:px-4 md:py-2">
                                    <span class="font-bold md:hidden">
                                        {{ translate('translations.status') }}:&nbsp;
                                    </span>
                                    <TranslationStatus :translation-status="item.status" />
                                </div>
                                <div class="mb-2 md:col-span-2 md:mb-0 md:px-4 md:py-2">
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
                                            v-if="
                                                item.subtitleToTranslate ||
                                                (item.mediaId && item.mediaType)
                                            "
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
                        </template>
                        <div v-else class="text-secondary-content py-4 text-center text-sm">
                            {{ translate('translations.noQueuedTranslations') }}
                        </div>
                    </div>
                </div>
                <PaginationComponent
                    v-if="translationRequests.totalCount"
                    v-model="filter"
                    :total-count="translationRequests.totalCount"
                    :page-size="translationRequests.pageSize" />
            </div>

            <div v-if="hasOpenedUploadWorkspaceTab" v-show="activeTab === 'uploadWorkspace'">
                <UploadWorkspaceTab />
            </div>

            <!-- Logs Modal -->
            <div
                v-if="logsModalOpen"
                class="fixed inset-0 z-40 flex items-center justify-center bg-black/60">
                <div
                    class="bg-tertiary max-h-[80vh] w-full max-w-3xl overflow-hidden rounded-lg shadow-lg">
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
                        <div
                            v-if="logsLoading"
                            class="text-secondary-content/60 flex h-full items-center justify-center">
                            {{ translate('translations.waitingForLogs') }}
                        </div>
                        <div v-else-if="logsError" class="text-error">
                            {{ logsError }}
                        </div>
                        <div
                            v-else-if="requestLogs.length === 0"
                            class="text-secondary-content/60 flex h-full items-center justify-center">
                            {{ translate('translations.noLogs') }}
                        </div>
                        <div v-else class="space-y-1">
                            <div
                                v-for="log in requestLogs"
                                :key="log.id"
                                class="border-secondary/30 border-b pb-1">
                                <span class="text-secondary-content/70 mr-2">
                                    {{ new Date(log.createdAt).toLocaleTimeString() }}
                                </span>
                                <span
                                    class="mr-2 font-semibold"
                                    :class="getLogLevelClass(log.level)">
                                    [{{ log.level }}]
                                </span>
                                <span>{{ log.message }}</span>
                                <div
                                    v-if="log.details"
                                    class="text-secondary-content/60 ml-4 text-[0.7rem] whitespace-pre-wrap">
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
    IRequestProgress,
    ITranslationRequest,
    ITranslationRequestLog,
    MEDIA_TYPE,
    TRANSLATION_ACTIONS,
    TRANSLATION_STATUS
} from '@/ts'
import { useTranslationRequestStore } from '@/store/translationRequest'
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
import TestIcon from '@/components/icons/TestIcon.vue'
import UploadWorkspaceTab from '@/components/features/upload-workspace/UploadWorkspaceTab.vue'
import { useRouter } from 'vue-router'

const { translate } = useI18n()
const router = useRouter()
const signalR = useSignalR()
const hubConnection = ref<Hub>()
const translationRequestStore = useTranslationRequestStore()

const logsModalOpen = ref(false)
const logsLoading = ref(false)
const logsError = ref<string | null>(null)
const activeLogRequest = ref<ITranslationRequest | null>(null)
const requestLogs = ref<ITranslationRequestLog[]>([])
const retryingFailed = ref(false)
const removingFailed = ref(false)
const showRemoveConfirm = ref(false)
const reenqueuingQueued = ref(false)
const cancellingQueued = ref(false)
const retryResultBanner = ref<{ retried: number; blocked: number } | null>(null)

type TranslationTab = 'list' | 'uploadWorkspace'

const activeTab = ref<TranslationTab>('list')
const hasOpenedUploadWorkspaceTab = ref(false)

const translationRequests: ComputedRef<IPagedResult<ITranslationRequest>> = computed(
    () => translationRequestStore.getTranslationRequests
)

const inProgressRequests = computed(() => translationRequestStore.inProgressRequests)

const failedRequests = computed(() => translationRequestStore.failedRequests)

const queuedRequests = computed(() =>
    translationRequests.value.items.filter(
        (request) => request.status === TRANSLATION_STATUS.PENDING
    )
)

const filter: ComputedRef<IFilter> = computed({
    get: () => translationRequestStore.filter,
    set: useDebounce((value: IFilter) => {
        translationRequestStore.setFilter(value)
    }, 300)
})

const handleRequestProgress = (requestProgress: IRequestProgress) => {
    translationRequestStore.queueProgressUpdate(requestProgress)
}

const handleRequestActive = ({ count }: { count: number }) => {
    translationRequestStore.handleRequestActive({ count })
}

const setRetryResultBanner = (retried: number, blocked: number) => {
    retryResultBanner.value = {
        retried,
        blocked
    }
}

function setActiveTab(tab: TranslationTab) {
    activeTab.value = tab
    if (tab === 'uploadWorkspace') {
        hasOpenedUploadWorkspaceTab.value = true
    }
}

async function handleAction(translationRequest: ITranslationRequest, action: TRANSLATION_ACTIONS) {
    switch (action) {
        case TRANSLATION_ACTIONS.CANCEL:
            await translationRequestStore.cancel(translationRequest)
            await translationRequestStore.forceRefreshSections()
            return
        case TRANSLATION_ACTIONS.REMOVE:
            await translationRequestStore.remove(translationRequest)
            await translationRequestStore.forceRefreshSections()
            return
        case TRANSLATION_ACTIONS.RETRY: {
            const result = await translationRequestStore.retry(translationRequest)
            setRetryResultBanner(result.retried ? 1 : 0, result.blockedByActiveRequest ? 1 : 0)
            await translationRequestStore.forceRefreshSections()
            return
        }
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
        const result = await translationRequestStore.retryAllFailed()
        setRetryResultBanner(result.retried, result.blockedByActiveRequest)
        await translationRequestStore.forceRefreshSections()
    } finally {
        retryingFailed.value = false
    }
}

const removeAllFailed = async () => {
    if (!failedRequests.value.length || removingFailed.value) return

    showRemoveConfirm.value = false
    removingFailed.value = true
    try {
        await translationRequestStore.removeAllFailed()
        await translationRequestStore.forceRefreshSections()
    } finally {
        removingFailed.value = false
    }
}

const reenqueueQueued = async () => {
    if (!queuedRequests.value.length || reenqueuingQueued.value) return

    reenqueuingQueued.value = true
    try {
        await translationRequestStore.reenqueueQueued(false)
    } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Failed to re-enqueue queued translation requests', error)
    } finally {
        reenqueuingQueued.value = false
    }
}

const cancelAllQueued = async () => {
    if (!queuedRequests.value.length || cancellingQueued.value) return

    cancellingQueued.value = true
    try {
        await translationRequestStore.cancelAllQueued(true)
    } catch (error) {
        // eslint-disable-next-line no-console
        console.error('Failed to cancel queued translation requests', error)
    } finally {
        cancellingQueued.value = false
    }
}

function runTestForItem(item: ITranslationRequest) {
    if (!item.subtitleToTranslate && !(item.mediaId && item.mediaType)) return

    router.push({
        path: '/translation-test',
        query: {
            subtitlePath: item.subtitleToTranslate,
            mediaId: item.mediaId?.toString(),
            mediaType: item.mediaType,
            sourceLanguage: item.sourceLanguage,
            targetLanguage: item.targetLanguage,
            title: item.title
        }
    })
}

onMounted(async () => {
    await translationRequestStore.fetchAllSections()
    hubConnection.value = await signalR.connect(
        'TranslationRequests',
        '/signalr/TranslationRequests'
    )

    await hubConnection.value.joinGroup({ group: 'TranslationRequests' })
    hubConnection.value.on('RequestProgress', handleRequestProgress)
    hubConnection.value.on('RequestActive', handleRequestActive)
})

onUnmounted(async () => {
    hubConnection.value?.off('RequestProgress', handleRequestProgress)
    hubConnection.value?.off('RequestActive', handleRequestActive)
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
    await translationRequestStore.forceRefreshSections()
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

function getWorkloadLabel(workloadKind?: string): string {
    switch (workloadKind) {
        case 'CustomSource':
            return 'Custom Source'
        case 'Upload':
            return 'Upload'
        default:
            return 'Library'
    }
}

function normalizeWorkloadSourceValue(value?: string): string | null {
    if (!value) {
        return null
    }

    const trimmed = value.trim()
    if (!trimmed) {
        return null
    }

    const prefixedValue = /^(upload|custom):(.+)$/i.exec(trimmed)
    if (!prefixedValue) {
        return trimmed
    }

    const strippedValue = prefixedValue[2]?.trim()
    return strippedValue || null
}

function getWorkloadSourceLabel(item: ITranslationRequest): string | null {
    const sourceValue =
        normalizeWorkloadSourceValue(item.workloadSourceLabel) ||
        normalizeWorkloadSourceValue(item.workloadItemKey)

    if (!sourceValue) {
        return null
    }

    const isUploadSource =
        item.workloadKind === 'Upload' || /^upload:/i.test(item.workloadItemKey || '')
    const isCustomSource =
        item.workloadKind === 'CustomSource' || /^custom:/i.test(item.workloadItemKey || '')

    if (isUploadSource) {
        return `Batch ${sourceValue}`
    }

    if (isCustomSource) {
        return `Source ${sourceValue}`
    }

    return null
}
</script>

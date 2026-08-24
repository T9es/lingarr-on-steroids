import { acceptHMRUpdate, defineStore } from 'pinia'
import {
    IFilter,
    IPagedResult,
    IRequestProgress,
    IRetryFailedRequestsResponse,
    IRetryTranslationRequestResponse,
    ITranslationRequest,
    ITranslationRequestLog,
    ITranslationRequestsOverview,
    IUseTranslationRequestStore,
    TRANSLATION_STATUS
} from '@/ts'
import services from '@/services'

const PROGRESS_FLUSH_DELAY_MS = 100
const SECTION_REFRESH_DELAY_MS = 1000

let queuedProgressUpdates = new Map<number, IRequestProgress>()
let progressFlushTimer: ReturnType<typeof setTimeout> | null = null
let sectionRefreshTimer: ReturnType<typeof setTimeout> | null = null

let translationRequestsFetchToken = 0
let failedFetchToken = 0
let inProgressFetchToken = 0
let sectionFetchToken = 0

const createRequestFromProgress = (
    requestProgress: IRequestProgress,
    existing?: ITranslationRequest
): ITranslationRequest => {
    return {
        id: requestProgress.id,
        jobId: requestProgress.jobId || existing?.jobId || '',
        title: requestProgress.title || existing?.title || `Translation #${requestProgress.id}`,
        workloadKind: existing?.workloadKind,
        workloadItemKey: existing?.workloadItemKey,
        workloadSourceLabel: existing?.workloadSourceLabel,
        sourceLanguage: requestProgress.sourceLanguage || existing?.sourceLanguage || '',
        targetLanguage: requestProgress.targetLanguage || existing?.targetLanguage || '',
        subtitleToTranslate: existing?.subtitleToTranslate,
        translatedSubtitle: existing?.translatedSubtitle,
        mediaType: requestProgress.mediaType || existing?.mediaType || 'Movie',
        mediaId: existing?.mediaId,
        customMediaItemId: existing?.customMediaItemId,
        uploadBatchFileId: existing?.uploadBatchFileId,
        status: requestProgress.status,
        progress: requestProgress.progress,
        completedAt: requestProgress.completedAt,
        isPriority: existing?.isPriority,
        isActive: existing?.isActive,
        startedAt: requestProgress.startedAt ?? existing?.startedAt
    }
}

export const useTranslationRequestStore = defineStore('translateRequest', {
    state: (): IUseTranslationRequestStore => ({
        activeTranslationRequests: 0,
        translationRequests: {
            totalCount: 0,
            pageSize: 0,
            pageNumber: 0,
            items: []
        },
        failedRequests: [] as ITranslationRequest[],
        failedTotalCount: 0,
        inProgressRequests: [] as ITranslationRequest[],
        inProgressTotalCount: 0,
        overviewInFlight: false,
        filter: {
            searchQuery: '',
            sortBy: 'Queue',
            isAscending: true,
            pageNumber: 1
        },
        selectedRequests: [] as ITranslationRequest[],
        selectAll: false
    }),
    getters: {
        getActiveTranslationRequests: (state: IUseTranslationRequestStore): number =>
            state.activeTranslationRequests,
        getTranslationRequests(): IPagedResult<ITranslationRequest> {
            return this.translationRequests
        },
        getFilter: (state: IUseTranslationRequestStore): IFilter => state.filter,
        getSelectedRequests: (state: IUseTranslationRequestStore): ITranslationRequest[] =>
            state.selectedRequests,
        getFailedRequests: (state: IUseTranslationRequestStore): ITranslationRequest[] => {
            const query = state.filter.searchQuery?.toLowerCase() || ''
            if (!query) return state.failedRequests

            return state.failedRequests.filter(
                (req) =>
                    req.title.toLowerCase().includes(query) ||
                    req.sourceLanguage.toLowerCase().includes(query) ||
                    req.targetLanguage.toLowerCase().includes(query)
            )
        },
        getInProgressRequests: (state: IUseTranslationRequestStore): ITranslationRequest[] => {
            const query = state.filter.searchQuery?.toLowerCase() || ''
            if (!query) return state.inProgressRequests

            return state.inProgressRequests.filter(
                (req) =>
                    req.title.toLowerCase().includes(query) ||
                    req.sourceLanguage.toLowerCase().includes(query) ||
                    req.targetLanguage.toLowerCase().includes(query)
            )
        }
    },
    actions: {
        async setFilter(filterVal: IFilter) {
            this.filter = filterVal.searchQuery ? { ...filterVal, pageNumber: 1 } : filterVal
            await this.fetch()
        },
        async fetch() {
            const currentToken = ++translationRequestsFetchToken
            const result = await services.translationRequest.requests<IPagedResult<ITranslationRequest>>(
                this.filter.pageNumber,
                this.filter.searchQuery,
                this.filter.sortBy,
                this.filter.isAscending
            )

            if (currentToken !== translationRequestsFetchToken) {
                return
            }

            this.translationRequests = result
        },
        async fetchFailedRequests() {
            const currentToken = ++failedFetchToken
            const result = await services.translationRequest.getFailedRequests<ITranslationRequest[]>()

            if (currentToken !== failedFetchToken) {
                return
            }

            this.failedRequests = result
            this.failedTotalCount = result.length
        },
        async fetchInProgressRequests() {
            const currentToken = ++inProgressFetchToken
            const result =
                await services.translationRequest.getInProgressRequests<ITranslationRequest[]>()

            if (currentToken !== inProgressFetchToken) {
                return
            }

            this.inProgressRequests = result
            this.inProgressTotalCount = result.length
        },
        async fetchAllSections() {
            if (this.overviewInFlight) {
                return
            }

            const currentToken = ++sectionFetchToken
            ++translationRequestsFetchToken
            ++failedFetchToken
            ++inProgressFetchToken
            this.overviewInFlight = true

            try {
                const overview: ITranslationRequestsOverview =
                    await services.translationRequest.overview(
                        this.filter.pageNumber,
                        this.filter.searchQuery,
                        this.filter.sortBy,
                        this.filter.isAscending
                    )

                if (currentToken !== sectionFetchToken) {
                    return
                }

                this.activeTranslationRequests = overview.activeCount
                this.translationRequests = overview.pending
                this.failedRequests = overview.failed.items
                this.failedTotalCount = overview.failed.totalCount
                this.inProgressRequests = overview.inProgress.items
                this.inProgressTotalCount = overview.inProgress.totalCount
            } finally {
                if (currentToken === sectionFetchToken) {
                    this.overviewInFlight = false
                }
            }
        },
        async forceRefreshSections() {
            if (sectionRefreshTimer) {
                clearTimeout(sectionRefreshTimer)
                sectionRefreshTimer = null
            }

            await this.fetchAllSections()
        },
        scheduleSectionRefresh() {
            if (sectionRefreshTimer) {
                return
            }

            sectionRefreshTimer = setTimeout(async () => {
                sectionRefreshTimer = null
                await this.fetchAllSections()
            }, SECTION_REFRESH_DELAY_MS)
        },
        async setActiveCount(activeTranslationRequests: number) {
            this.activeTranslationRequests = activeTranslationRequests
        },
        async getActiveCount() {
            const activeTranslationRequests =
                await services.translationRequest.getActiveCount<number>()
            await this.setActiveCount(activeTranslationRequests)
        },
        async cancel(translationRequest: ITranslationRequest) {
            await services.translationRequest.cancel<string>(translationRequest)
        },
        async remove(translationRequest: ITranslationRequest) {
            await services.translationRequest.remove<string>(translationRequest)
        },
        async retry(translationRequest: ITranslationRequest): Promise<IRetryTranslationRequestResponse> {
            return await services.translationRequest.retry(translationRequest)
        },
        async retryAllFailed(): Promise<IRetryFailedRequestsResponse> {
            return await services.translationRequest.retryAllFailed()
        },
        async removeAllFailed() {
            const count = await services.translationRequest.removeAllFailed<number>()
            this.failedRequests = []
            this.failedTotalCount = 0
            return count
        },
        async reenqueueQueued(includeInProgress = false) {
            const result = await services.translationRequest.reenqueueQueued<{
                reenqueued: number
                skippedProcessing: number
                message?: string
            }>(includeInProgress)
            await this.fetchAllSections()
            return result
        },
        async cancelAllQueued(includeInProgress = false) {
            const result = await services.translationRequest.cancelAll<{
                cancelled: number
                skippedProcessing: number
                message?: string
            }>(includeInProgress)
            await this.fetchAllSections()
            return result
        },
        async getLogs(translationRequestId: number): Promise<ITranslationRequestLog[]> {
            return await services.translationRequest.logs<ITranslationRequestLog[]>(translationRequestId)
        },
        queueProgressUpdate(requestProgress: IRequestProgress) {
            queuedProgressUpdates.set(requestProgress.id, requestProgress)

            if (progressFlushTimer) {
                return
            }

            progressFlushTimer = setTimeout(() => {
                progressFlushTimer = null
                this.flushProgressUpdates()
            }, PROGRESS_FLUSH_DELAY_MS)
        },
        flushProgressUpdates() {
            if (queuedProgressUpdates.size === 0) {
                return
            }

            const updates = Array.from(queuedProgressUpdates.values())
            queuedProgressUpdates.clear()

            const queueRequestsById = new Map(
                this.translationRequests.items.map((request) => [request.id, request])
            )
            const inProgressRequestsById = new Map(
                this.inProgressRequests.map((request) => [request.id, request])
            )
            const failedRequestsById = new Map(
                this.failedRequests.map((request) => [request.id, request])
            )
            const removeRequest = (
                requests: ITranslationRequest[],
                request: ITranslationRequest
            ) => {
                const index = requests.indexOf(request)
                if (index === -1) {
                    return false
                }

                requests.splice(index, 1)
                return true
            }

            for (const requestProgress of updates) {
                const queueRequest = queueRequestsById.get(requestProgress.id)
                const inProgressRequest = inProgressRequestsById.get(requestProgress.id)
                const failedRequest = failedRequestsById.get(requestProgress.id)
                const existingRequest = queueRequest || inProgressRequest || failedRequest
                const mergedRequest = {
                    ...(existingRequest || {}),
                    ...createRequestFromProgress(requestProgress, existingRequest)
                } as ITranslationRequest

                if (queueRequest) {
                    Object.assign(queueRequest, mergedRequest)
                }

                if (
                    requestProgress.status === TRANSLATION_STATUS.COMPLETED ||
                    requestProgress.status === TRANSLATION_STATUS.CANCELLED ||
                    requestProgress.status === TRANSLATION_STATUS.INTERRUPTED
                ) {
                    if (
                        queueRequest &&
                        removeRequest(this.translationRequests.items, queueRequest)
                    ) {
                        queueRequestsById.delete(requestProgress.id)
                        this.translationRequests.totalCount = Math.max(
                            0,
                            this.translationRequests.totalCount - 1
                        )
                    }
                    if (
                        inProgressRequest &&
                        removeRequest(this.inProgressRequests, inProgressRequest)
                    ) {
                        inProgressRequestsById.delete(requestProgress.id)
                    }
                    if (requestProgress.status === TRANSLATION_STATUS.INTERRUPTED) {
                        if (failedRequest) {
                            Object.assign(failedRequest, mergedRequest)
                        } else {
                            this.failedRequests.push(mergedRequest)
                            failedRequestsById.set(requestProgress.id, mergedRequest)
                        }
                    } else if (failedRequest && removeRequest(this.failedRequests, failedRequest)) {
                        failedRequestsById.delete(requestProgress.id)
                    }
                    continue
                }

                if (requestProgress.status === TRANSLATION_STATUS.INPROGRESS) {
                    if (inProgressRequest) {
                        Object.assign(inProgressRequest, mergedRequest)
                    } else {
                        this.inProgressRequests.push(mergedRequest)
                        inProgressRequestsById.set(requestProgress.id, mergedRequest)
                    }
                    if (failedRequest && removeRequest(this.failedRequests, failedRequest)) {
                        failedRequestsById.delete(requestProgress.id)
                    }
                    continue
                }

                if (requestProgress.status === TRANSLATION_STATUS.FAILED) {
                    if (failedRequest) {
                        Object.assign(failedRequest, mergedRequest)
                    } else {
                        this.failedRequests.push(mergedRequest)
                        failedRequestsById.set(requestProgress.id, mergedRequest)
                    }
                    if (
                        inProgressRequest &&
                        removeRequest(this.inProgressRequests, inProgressRequest)
                    ) {
                        inProgressRequestsById.delete(requestProgress.id)
                    }
                    continue
                }

                if (
                    inProgressRequest &&
                    removeRequest(this.inProgressRequests, inProgressRequest)
                ) {
                    inProgressRequestsById.delete(requestProgress.id)
                }
                if (failedRequest && removeRequest(this.failedRequests, failedRequest)) {
                    failedRequestsById.delete(requestProgress.id)
                }
            }
        },
        updateProgress(requestProgress: IRequestProgress) {
            this.queueProgressUpdate(requestProgress)
        },
        clearSelection() {
            this.selectedRequests = []
            this.selectAll = false
        },
        toggleSelectAll() {
            this.selectAll = !this.selectAll
            if (this.selectAll) {
                this.selectedRequests = [...this.translationRequests.items]
            } else {
                this.selectedRequests = []
            }
        },
        toggleSelect(request: ITranslationRequest) {
            const index = this.selectedRequests.findIndex((r) => r.id === request.id)
            if (index === -1) {
                this.selectedRequests.push(request)
            } else {
                this.selectedRequests.splice(index, 1)
            }
            this.selectAll = this.selectedRequests.length === this.translationRequests.items.length
        },
        handleRequestActive({ count }: { count: number }) {
            this.activeTranslationRequests = count
            this.scheduleSectionRefresh()
        }
    }
})

if (import.meta.hot) {
    import.meta.hot.accept(acceptHMRUpdate(useTranslationRequestStore, import.meta.hot))
}

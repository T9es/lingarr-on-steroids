import { acceptHMRUpdate, defineStore } from 'pinia'
import {
    IFilter,
    IPagedResult,
    IRequestProgress,
    ITranslationRequest,
    ITranslationRequestLog,
    IUseTranslationRequestStore
} from '@/ts'
import services from '@/services'

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
        inProgressRequests: [] as ITranslationRequest[],
        filter: {
            searchQuery: '',
            sortBy: 'CreatedAt',
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
        getFailedRequests: (state: IUseTranslationRequestStore): ITranslationRequest[] =>
            state.failedRequests,
        getInProgressRequests: (state: IUseTranslationRequestStore): ITranslationRequest[] =>
            state.inProgressRequests
    },
    actions: {
        async setFilter(filterVal: IFilter) {
            this.filter = filterVal.searchQuery ? { ...filterVal, pageNumber: 1 } : filterVal
            await this.fetch()
        },
        async fetch() {
            this.translationRequests = await services.translationRequest.requests<
                IPagedResult<ITranslationRequest>
            >(
                this.filter.pageNumber,
                this.filter.searchQuery,
                this.filter.sortBy,
                this.filter.isAscending
            )
        },
        async fetchFailedRequests() {
            this.failedRequests = await services.translationRequest.getFailedRequests<ITranslationRequest[]>()
        },
        async fetchInProgressRequests() {
            this.inProgressRequests = await services.translationRequest.getInProgressRequests<ITranslationRequest[]>()
        },
        async fetchAllSections() {
            await Promise.all([
                this.fetch(),
                this.fetchFailedRequests(),
                this.fetchInProgressRequests()
            ])
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
            await services.translationRequest.remove<string>(translationRequest).finally(() => {
                this.translationRequests.items = this.translationRequests.items.filter(
                    (request) => request.id !== translationRequest.id
                )
            })
        },
	        async retry(translationRequest: ITranslationRequest) {
	            await services.translationRequest.retry<string>(translationRequest)
	            // Immediately remove from failedRequests so UI updates instantly
	            this.failedRequests = this.failedRequests.filter(
	                (request) => request.id !== translationRequest.id
	            )
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
	        async getLogs(translationRequestId: number): Promise<ITranslationRequestLog[]> {
	            return await services.translationRequest.logs<ITranslationRequestLog[]>(translationRequestId)
	        },
        async updateProgress(requestProgress: IRequestProgress) {
            const updatedRequest: Partial<ITranslationRequest> = {
                status: requestProgress.status,
                progress: requestProgress.progress,
                completedAt: requestProgress.completedAt
            }

            // Update in main translationRequests.items
            this.translationRequests.items = this.translationRequests.items.map(
                (request: ITranslationRequest) => {
                    if (request.id === requestProgress.id) {
                        return { ...request, ...updatedRequest }
                    }
                    return request
                }
            )

            // Handle status transitions for inProgressRequests
            const inProgressIndex = this.inProgressRequests.findIndex(r => r.id === requestProgress.id)
            if (requestProgress.status === 'InProgress') {
                // Should be in inProgressRequests
                if (inProgressIndex === -1) {
                    // Find the full request data from main list or create minimal entry
                    const existingRequest = this.translationRequests.items.find(r => r.id === requestProgress.id)
                    if (existingRequest) {
                        this.inProgressRequests.push({ ...existingRequest, ...updatedRequest })
                    }
                } else {
                    // Update existing entry
                    this.inProgressRequests[inProgressIndex] = {
                        ...this.inProgressRequests[inProgressIndex],
                        ...updatedRequest
                    }
                }
            } else if (inProgressIndex !== -1) {
                // Status changed from InProgress to something else - remove from inProgressRequests
                this.inProgressRequests.splice(inProgressIndex, 1)
            }

            // Handle status transitions for failedRequests
            const failedIndex = this.failedRequests.findIndex(r => r.id === requestProgress.id)
            if (requestProgress.status === 'Failed') {
                // Should be in failedRequests
                if (failedIndex === -1) {
                    const existingRequest = this.translationRequests.items.find(r => r.id === requestProgress.id)
                    if (existingRequest) {
                        this.failedRequests.push({ ...existingRequest, ...updatedRequest })
                    }
                } else {
                    // Update existing entry
                    this.failedRequests[failedIndex] = {
                        ...this.failedRequests[failedIndex],
                        ...updatedRequest
                    }
                }
            } else if (failedIndex !== -1) {
                // Status changed from Failed to something else - remove from failedRequests
                this.failedRequests.splice(failedIndex, 1)
            }

            // Remove completed/cancelled items from main queue (they shouldn't show in pending)
            if (requestProgress.status === 'Completed' || requestProgress.status === 'Cancelled') {
                this.translationRequests.items = this.translationRequests.items.filter(
                    r => r.id !== requestProgress.id
                )
            }
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
        }
    }
})

if (import.meta.hot) {
    import.meta.hot.accept(acceptHMRUpdate(useTranslationRequestStore, import.meta.hot))
}

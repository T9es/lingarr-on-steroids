// Store Pattern Example - translationRequest.ts
// Demonstrates Pinia store with Options API style and HMR

import { acceptHMRUpdate, defineStore } from 'pinia'
import {
    IFilter,
    IPagedResult,
    ITranslationRequest,
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
        getActiveTranslationRequests: (state): number =>
            state.activeTranslationRequests,
        
        getFailedRequests: (state): ITranslationRequest[] => {
            const query = state.filter.searchQuery?.toLowerCase() || ''
            if (!query) return state.failedRequests
            
            return state.failedRequests.filter((req) => 
                req.title.toLowerCase().includes(query) || 
                req.sourceLanguage.toLowerCase().includes(query)
            )
        }
    },
    
    actions: {
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
        
        async cancel(translationRequest: ITranslationRequest) {
            await services.translationRequest.cancel<string>(translationRequest)
        },
        
        async retry(translationRequest: ITranslationRequest) {
            await services.translationRequest.retry<string>(translationRequest)
            // Optimistic update
            this.failedRequests = this.failedRequests.filter(
                (request) => request.id !== translationRequest.id
            )
        }
    }
})

// HMR support
if (import.meta.hot) {
    import.meta.hot.accept(acceptHMRUpdate(useTranslationRequestStore, import.meta.hot))
}

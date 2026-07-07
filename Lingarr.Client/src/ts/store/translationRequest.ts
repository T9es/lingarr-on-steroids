import { IFilter, IPagedResult, ITranslationRequest } from '@/ts'

export interface IUseTranslationRequestStore {
    activeTranslationRequests: number
    translationRequests: IPagedResult<ITranslationRequest>
    failedRequests: ITranslationRequest[]
    failedTotalCount: number
    inProgressRequests: ITranslationRequest[]
    inProgressTotalCount: number
    overviewInFlight: boolean
    filter: IFilter
    selectedRequests: ITranslationRequest[]
    selectAll: boolean
}

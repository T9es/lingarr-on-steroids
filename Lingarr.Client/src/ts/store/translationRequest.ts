import { IFilter, IPagedResult, ITranslationRequest } from '@/ts'

export interface IUseTranslationRequestStore {
    activeTranslationRequests: number
    translationRequests: IPagedResult<ITranslationRequest>
    failedRequests: ITranslationRequest[]
    inProgressRequests: ITranslationRequest[]
    filter: IFilter
    selectedRequests: ITranslationRequest[]
    selectAll: boolean
}

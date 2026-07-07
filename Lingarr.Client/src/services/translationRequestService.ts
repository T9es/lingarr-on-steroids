import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import {
    IRetryFailedRequestsResponse,
    IRetryTranslationRequestResponse,
    ITranslationRequest,
    ITranslationRequestLog,
    ITranslationRequestsOverview,
    ITranslationRequestService
} from '@/ts'

const service = (
    http: AxiosStatic,
    resource = '/api/translationRequest'
): ITranslationRequestService => ({
    getActiveCount<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/active`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    getFailedRequests<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/failed`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    getInProgressRequests<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/inprogress`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    overview(
        pageNumber: number,
        searchQuery: string,
        orderBy: string,
        ascending: boolean,
        pageSize = 20,
        sectionLimit = 100
    ): Promise<ITranslationRequestsOverview> {
        return new Promise((resolve, reject) => {
            http.get(
                `${resource}/overview`.addParams({
                    pageNumber,
                    searchQuery,
                    orderBy,
                    ascending,
                    pageSize,
                    sectionLimit
                })
            )
                .then((response: AxiosResponse<ITranslationRequestsOverview>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    getRecentCompleted<T>(offset = 0, limit = 10): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/recent`.addParams({ offset, limit }))
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    requests<T>(
        pageNumber: number,
        searchQuery: string,
        orderBy: string,
        ascending: boolean
    ): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(
                `${resource}/requests`.addParams({
                    pageNumber: pageNumber,
                    searchQuery: searchQuery,
                    orderBy: orderBy,
                    ascending: ascending
                })
            )
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    cancel<T>(translationRequest: ITranslationRequest): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/cancel`, translationRequest)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    remove<T>(translationRequest: ITranslationRequest): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/remove`, translationRequest)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    retry(translationRequest: ITranslationRequest): Promise<IRetryTranslationRequestResponse> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/retry`, translationRequest)
                .then((response: AxiosResponse<IRetryTranslationRequestResponse>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    retryAllFailed(): Promise<IRetryFailedRequestsResponse> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/retry-all-failed`)
                .then((response: AxiosResponse<IRetryFailedRequestsResponse>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    removeAllFailed<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/remove-all-failed`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    reenqueueQueued<T>(includeInProgress = false): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/reenqueue`.addParams({ includeInProgress }), null)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    cancelAll<T>(includeInProgress = false): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/cancel-all`.addParams({ includeInProgress }), null)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },

    logs<T extends ITranslationRequestLog[]>(translationRequestId: number): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/logs/${translationRequestId}`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    }
})

export const translationRequestService = (axios: AxiosStatic): ITranslationRequestService => {
    return service(axios)
}

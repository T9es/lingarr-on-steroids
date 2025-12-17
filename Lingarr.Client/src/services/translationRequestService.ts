import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { ITranslationRequest, ITranslationRequestLog, ITranslationRequestService } from '@/ts'

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
    retry<T>(translationRequest: ITranslationRequest): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/retry`, translationRequest)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    retryAllFailed<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/retry-all-failed`)
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

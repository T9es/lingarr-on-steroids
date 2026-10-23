import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import {
    IBlockedMediaPage,
    IBlockedMediaRecheckResult,
    IBlockedMediaService
} from '@/ts'

export interface BlockedMediaQuery {
    page?: number
    pageSize?: number
    state?: string
}

const noCacheHeaders = {
    'Cache-Control': 'no-cache, no-store, must-revalidate',
    Pragma: 'no-cache',
    Expires: '0'
}

const service = (http: AxiosStatic, resource = '/api/blocked-media'): IBlockedMediaService => ({
    getBlockedMedia({
        page = 1,
        pageSize = 50,
        state
    }: BlockedMediaQuery = {}): Promise<IBlockedMediaPage> {
        return new Promise((resolve, reject) => {
            http
                .get(resource, {
                    params: state ? { page, pageSize, state } : { page, pageSize },
                    headers: noCacheHeaders
                })
                .then((response: AxiosResponse<IBlockedMediaPage>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },
    recheck(
        mediaType: 'movie' | 'episode',
        mediaId: number
    ): Promise<IBlockedMediaRecheckResult> {
        return new Promise((resolve, reject) => {
            http
                .post(`${resource}/${mediaType}/${mediaId}/recheck`)
                .then((response: AxiosResponse<IBlockedMediaRecheckResult>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    }
})

export const blockedMediaService = (axios: AxiosStatic): IBlockedMediaService => {
    return service(axios)
}

import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { IBlockedMediaItem, IBlockedMediaService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/blocked-media'): IBlockedMediaService => ({
    getBlockedMedia<T = IBlockedMediaItem[]>(limit = 200): Promise<T> {
        return new Promise((resolve, reject) => {
            http
                .get(`${resource}`, {
                    params: { limit },
                    headers: {
                        'Cache-Control': 'no-cache, no-store, must-revalidate',
                        Pragma: 'no-cache',
                        Expires: '0'
                    }
                })
                .then((response: AxiosResponse<T>) => {
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

import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { IChutesService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/providers/chutes'): IChutesService => ({
    getUsage<T>(forceRefresh: boolean = false): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/usage`, {
                params: {
                    refresh: forceRefresh
                }
            })
                .then((response: AxiosResponse<T>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    }
})

export const chutesService = (axios: AxiosStatic): IChutesService => service(axios)

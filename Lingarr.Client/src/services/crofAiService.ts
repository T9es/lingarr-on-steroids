import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { ICrofAiService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/providers/crofai'): ICrofAiService => ({
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

export const crofAiService = (axios: AxiosStatic): ICrofAiService => service(axios)

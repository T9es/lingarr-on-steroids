import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { ILogsService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/logs'): ILogsService => ({
    getStream(includeRecent = true): EventSource {
        const streamUrl = new URL(`${window.location.origin}${resource}/stream`)
        streamUrl.searchParams.set('includeRecent', includeRecent ? 'true' : 'false')
        return new EventSource(streamUrl)
    },
    getRecent<T>(take: number = 1000): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/recent`, {
                params: {
                    take
                }
            })
                .then((response: AxiosResponse<T>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error))
        })
    }
})

export const logsService = (axios: AxiosStatic): ILogsService => {
    return service(axios)
}

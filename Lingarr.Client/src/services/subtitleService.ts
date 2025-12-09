import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { ISubtitleService } from '@/ts'

interface ExtractSubtitleResponse {
    success: boolean
    extractedPath: string | null
    error: string | null
}

const service = (http: AxiosStatic, resource = '/api/subtitle'): ISubtitleService => ({
    collect<T>(path: string): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(
                `${resource}/all`,
                {
                    path: path
                },
                {
                    headers: {
                        'Cache-Control': 'no-cache, no-store, must-revalidate',
                        Pragma: 'no-cache',
                        Expires: '0'
                    }
                }
            )
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },

    getEmbeddedSubtitles<T>(mediaType: 'movie' | 'episode', mediaId: number): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/${mediaType}/${mediaId}/embedded`, {
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
    },

    extractSubtitle(
        mediaType: 'movie' | 'episode',
        mediaId: number,
        streamIndex: number
    ): Promise<ExtractSubtitleResponse> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/${mediaType}/${mediaId}/extract/${streamIndex}`)
                .then((response: AxiosResponse<ExtractSubtitleResponse>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },

    probeEmbeddedSubtitles<T>(mediaType: 'movie' | 'episode', mediaId: number): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/${mediaType}/${mediaId}/probe`)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    }
})

export const subtitleService = (axios: AxiosStatic): ISubtitleService => {
    return service(axios)
}

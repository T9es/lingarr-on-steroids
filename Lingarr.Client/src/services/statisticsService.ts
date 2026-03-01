import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { IStatisticsService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/statistics'): IStatisticsService => ({
    getStatistics<T>(): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(resource)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    },

    getDailyStatistics<T>(days?: number): Promise<T> {
        return new Promise((resolve, reject) => {
            const url = days ? `${resource}/daily/${days}` : `${resource}/daily`
            http.get(url)
                .then((response: AxiosResponse<T>) => {
                    resolve(response.data)
                })
                .catch((error: AxiosError) => {
                    reject(error.response)
                })
        })
    }
})

export const statisticsService = (axios: AxiosStatic): IStatisticsService => {
    return service(axios)
}

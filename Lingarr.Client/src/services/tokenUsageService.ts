import { AxiosInstance } from 'axios'
import { ITokenUsageService } from '@/ts'

export const tokenUsageService = (axios: AxiosInstance): ITokenUsageService => ({
    async getUsage<T>(service: string): Promise<T> {
        const response = await axios.get(`/api/token-usage/${service}`)
        return response.data
    },

    async setChutesMode(mode: 'subscription' | 'payg'): Promise<void> {
        await axios.put('/api/token-usage/chutes-mode', { mode })
    },

    async getChutesMode<T>(): Promise<T> {
        const response = await axios.get('/api/token-usage/chutes-mode')
        return response.data
    }
})

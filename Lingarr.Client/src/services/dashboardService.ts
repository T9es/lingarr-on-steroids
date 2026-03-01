import { AxiosStatic } from 'axios'

export const dashboardService = (axios: AxiosStatic) => ({
    getLayout: async <T>(): Promise<T | null> => {
        const response = await axios.get('/api/dashboard/layout')
        return response.data
    },

    saveLayout: async (layoutJson: string): Promise<void> => {
        await axios.put('/api/dashboard/layout', layoutJson, {
            headers: { 'Content-Type': 'application/json' }
        })
    },

    resetLayout: async (): Promise<void> => {
        await axios.post('/api/dashboard/layout/reset')
    },

    getJobs: async <T>(): Promise<T> => {
        const response = await axios.get('/api/dashboard/jobs')
        return response.data
    },

    getApiUsage: async <T>(): Promise<T> => {
        const response = await axios.get('/api/dashboard/api-usage')
        return response.data
    },

    clearFailedJobs: async (): Promise<{ cleared: number }> => {
        const response = await axios.delete('/api/dashboard/jobs/failed')
        return response.data
    },

    getFailedJobs: async (
        offset: number = 0,
        limit: number = 10
    ): Promise<{
        jobs: any[]
        totalCount: number
        hasMore: boolean
    }> => {
        const response = await axios.get('/api/dashboard/jobs/failed', {
            params: { offset, limit }
        })
        return response.data
    }
})

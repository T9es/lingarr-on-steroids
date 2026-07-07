import { AxiosError, AxiosResponse, AxiosStatic } from 'axios'
import { ICustomMediaItem, ICustomSource, ICustomSourceService } from '@/ts'

const service = (http: AxiosStatic, resource = '/api/customsources'): ICustomSourceService => ({
    getSources(): Promise<ICustomSource[]> {
        return new Promise((resolve, reject) => {
            http.get(resource)
                .then((response: AxiosResponse<ICustomSource[]>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    getSource<T>(id: number): Promise<T> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/${id}`)
                .then((response: AxiosResponse<T>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    createSource<T>(source: Partial<ICustomSource>): Promise<T> {
        return new Promise((resolve, reject) => {
            http.post(resource, source)
                .then((response: AxiosResponse<T>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    updateSource<T>(id: number, source: Partial<ICustomSource>): Promise<T> {
        return new Promise((resolve, reject) => {
            http.put(`${resource}/${id}`, source)
                .then((response: AxiosResponse<T>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    deleteSource(id: number): Promise<void> {
        return new Promise((resolve, reject) => {
            http.delete(`${resource}/${id}`)
                .then(() => resolve())
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    getItems(id: number): Promise<ICustomMediaItem[]> {
        return new Promise((resolve, reject) => {
            http.get(`${resource}/${id}/items`)
                .then((response: AxiosResponse<ICustomMediaItem[]>) => resolve(response.data))
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    rescan(id: number): Promise<void> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/${id}/rescan`)
                .then(() => resolve())
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    rescanAll(): Promise<void> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/rescan`)
                .then(() => resolve())
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    setExcluded(itemId: number, excluded: boolean): Promise<void> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/items/${itemId}/exclude?excluded=${excluded}`)
                .then(() => resolve())
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    setPriority(itemId: number, priority: boolean): Promise<void> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/items/${itemId}/priority?priority=${priority}`)
                .then(() => resolve())
                .catch((error: AxiosError) => reject(error.response))
        })
    },

    translate(itemId: number, forceRecreate = false): Promise<{ translationsQueued: number; message: string }> {
        return new Promise((resolve, reject) => {
            http.post(`${resource}/items/${itemId}/translate?forceRecreate=${forceRecreate}`)
                .then((response: AxiosResponse<{ translationsQueued: number; message: string }>) =>
                    resolve(response.data)
                )
                .catch((error: AxiosError) => reject(error.response))
        })
    }
})

export const customSourceService = (axios: AxiosStatic): ICustomSourceService => {
    return service(axios)
}

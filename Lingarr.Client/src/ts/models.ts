export interface LabelValue {
    label: string
    value: string
}

export interface SelectComponentExpose {
    setLoadingState: (loading: boolean) => void
}

export interface TranslateModelsResponse {
    message?: string
    options: LabelValue[]
}

export interface ChutesUsageSnapshot {
    date: string
    plan?: string
    modelId?: string
    chuteId?: string
    requestsUsed: number
    remoteRequestsUsed: number
    allowedRequestsPerDay: number
    planRequestsPerDay?: number
    overrideRequestsPerDay?: number
    resetAt?: string
    lastSyncedUtc: string
    hasApiKey: boolean
    message?: string
}

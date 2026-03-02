export interface TokenUsageResponse {
    service: string
    tokensUsedToday: number
    tokenLimit: number | null
    resetAt: string | null
    limitEnabled: boolean
    percentUsed: number
    resetTimeSetting: string
}

export type ChutesMode = 'subscription' | 'payg'

export interface NanoGptUsageWindow {
    limit?: number | null
    used: number
    remaining?: number | null
    percentUsed: number
    resetAt?: string | null
}

export interface NanoGptUsageSnapshot {
    active: boolean
    state?: string | null
    daily: NanoGptUsageWindow
    monthly: NanoGptUsageWindow
    dailyImages: NanoGptUsageWindow
    weeklyTokens: NanoGptUsageWindow
    currentPeriodEnd?: string | null
    lastSyncedUtc: string
    hasApiKey: boolean
    message?: string | null
}

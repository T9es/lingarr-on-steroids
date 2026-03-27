export interface IVersion {
    newVersion: boolean
    isDevBuild: boolean
    currentVersion: string
    displayVersion?: string
    latestVersion: string
    branchName?: string
    commitSha?: string
    baseTag?: string
    commitsSinceTag?: number | null
}

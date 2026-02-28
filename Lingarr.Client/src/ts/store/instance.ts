import { IVersion } from '@/ts/version'

export interface IUseInstanceStore {
    version: IVersion
    isOpen: boolean
    theme: ITheme
    poster: string
}

export const THEMES = {
    SOLARIZED_LIGHT: 'solarized-light',
    SOLARIZED_DARK: 'solarized-dark',
    DRACULA: 'dracula',
    NORD: 'nord',
    MONOKAI: 'monokai',
    MATERIAL_DARK: 'material-dark',
    GOTHAM: 'gotham',
    GRUVBOX: 'gruvbox',
    CYBERPUNK_NEON: 'cyberpunk-neon',
    HORIZON: 'horizon',
    LINGARR: 'lingarr'
} as const

export type ITheme = (typeof THEMES)[keyof typeof THEMES]

export const LOCALE = {
    ENGLISH: 'en',
    DUTCH: 'nl',
    GERMAN: 'de',
    FRENCH: 'fr',
    SPANISH: 'es',
    CHINESE: 'zh',
    POLISH: 'pl'
} as const

export type ILocale = (typeof LOCALE)[keyof typeof LOCALE]

/**
 * List of all supported locale codes for validation
 */
export const SUPPORTED_LOCALES: readonly ILocale[] = Object.values(LOCALE)

export type IFilter = {
    pageNumber: number
    searchQuery: string
    sortBy: string
    isAscending: boolean
}
export type IOptions = {
    label: string
    value: string
}

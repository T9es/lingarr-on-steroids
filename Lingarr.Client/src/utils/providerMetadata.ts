export interface ProviderMeta {
    key: string
    label: string
    color: string
}

const providerMap: Record<string, ProviderMeta> = {
    openai: { key: 'openai', label: 'OpenAI', color: '#10a37f' },
    anthropic: { key: 'anthropic', label: 'Anthropic', color: '#d97757' },
    gemini: { key: 'gemini', label: 'Gemini', color: '#8b5cf6' },
    deepseek: { key: 'deepseek', label: 'DeepSeek', color: '#4f86ff' },
    deepl: { key: 'deepl', label: 'DeepL', color: '#0f2b46' },
    google: { key: 'google', label: 'Google', color: '#4285f4' },
    bing: { key: 'bing', label: 'Bing', color: '#008373' },
    microsoft: { key: 'microsoft', label: 'Microsoft', color: '#2563eb' },
    yandex: { key: 'yandex', label: 'Yandex', color: '#ef4444' },
    chutes: { key: 'chutes', label: 'Chutes.ai', color: '#06b6d4' },
    localai: { key: 'localai', label: 'LocalAI', color: '#f97316' },
    libretranslate: { key: 'libretranslate', label: 'LibreTranslate', color: '#22c55e' }
}

const aliasMap: Record<string, string> = {
    openai: 'openai',
    chatgpt: 'openai',
    anthropic: 'anthropic',
    claude: 'anthropic',
    gemini: 'gemini',
    googlegemini: 'gemini',
    deepseek: 'deepseek',
    deepl: 'deepl',
    google: 'google',
    googletranslate: 'google',
    bing: 'bing',
    bingtranslate: 'bing',
    microsoft: 'microsoft',
    microsofttranslator: 'microsoft',
    azure: 'microsoft',
    azuretranslator: 'microsoft',
    yandex: 'yandex',
    yandextranslate: 'yandex',
    chutes: 'chutes',
    chutesai: 'chutes',
    localai: 'localai',
    libretranslate: 'libretranslate'
}

export const normalizeServiceKey = (service: string): string => {
    const normalized = service.toLowerCase().replace(/[^a-z]/g, '')

    return aliasMap[normalized] ?? normalized
}

export const getProviderMeta = (service: string): ProviderMeta => {
    const key = normalizeServiceKey(service)

    return (
        providerMap[key] ?? {
            key,
            label: service,
            color: '#9333ea'
        }
    )
}

export const toRgba = (hex: string, opacity: number): string => {
    const sanitized = hex.replace('#', '')
    const normalized =
        sanitized.length === 3
            ? sanitized
                  .split('')
                  .map((char) => char + char)
                  .join('')
            : sanitized

    const value = Number.parseInt(normalized, 16)
    const red = (value >> 16) & 255
    const green = (value >> 8) & 255
    const blue = value & 255

    return `rgba(${red}, ${green}, ${blue}, ${opacity})`
}

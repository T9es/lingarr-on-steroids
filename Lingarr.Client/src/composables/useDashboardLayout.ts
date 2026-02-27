import { ref, watch, computed } from 'vue'
import services from '@/services'

/**
 * Widget layout item for grid-layout-plus
 * i = id, x = column, y = row, w = width (columns), h = height (rows), minW/minH = minimums
 */
export interface LayoutItem {
    i: string
    x: number
    y: number
    w: number
    h: number
    minW?: number
    minH?: number
    maxW?: number
    maxH?: number
    static?: boolean
}

/**
 * Widget metadata for display
 */
export interface WidgetMeta {
    id: string
    title: string
    visible: boolean
}

/**
 * Dashboard layout state
 */
export interface DashboardLayout {
    layout: LayoutItem[]
    widgets: WidgetMeta[]
    version: number
}

const STORAGE_KEY = 'lingarr-dashboard-layout'
const LAYOUT_VERSION = 3

// Grid configuration
export const GRID_COLS = 12
export const ROW_HEIGHT = 80
export const MARGIN = [16, 16] as [number, number]

/**
 * Default widget layout configuration
 * Each widget has position (x, y), size (w, h), and constraints
 */
const DEFAULT_LAYOUT: LayoutItem[] = [
    { i: 'active-translations', x: 0, y: 0, w: 12, h: 3, minW: 6, minH: 2 },
    { i: 'media-overview', x: 0, y: 3, w: 12, h: 2, minW: 6, minH: 2 },
    { i: 'translation-activity', x: 0, y: 5, w: 6, h: 3, minW: 4, minH: 2 },
    { i: 'language-statistics', x: 6, y: 5, w: 6, h: 4, minW: 4, minH: 3 },
    { i: 'translation-history', x: 0, y: 8, w: 6, h: 5, minW: 4, minH: 3 },
    { i: 'job-queue', x: 6, y: 8, w: 3, h: 4, minW: 3, minH: 3 },
    { i: 'api-usage', x: 9, y: 8, w: 3, h: 4, minW: 3, minH: 3 },
    { i: 'error-log', x: 0, y: 12, w: 12, h: 3, minW: 6, minH: 2 }
]

const DEFAULT_WIDGETS: WidgetMeta[] = [
    { id: 'active-translations', title: 'statistics.activeTranslations', visible: true },
    { id: 'media-overview', title: 'statistics.mediaOverview', visible: true },
    { id: 'translation-activity', title: 'statistics.translationActivity', visible: true },
    { id: 'language-statistics', title: 'statistics.languageStatistics', visible: true },
    { id: 'translation-history', title: 'statistics.translationHistory', visible: true },
    { id: 'job-queue', title: 'statistics.jobQueue', visible: true },
    { id: 'api-usage', title: 'statistics.apiUsage', visible: true },
    { id: 'error-log', title: 'statistics.errorLog', visible: true }
]

/**
 * Load layout from localStorage
 */
function loadFromLocalStorage(): DashboardLayout | null {
    try {
        const stored = localStorage.getItem(STORAGE_KEY)
        if (stored) {
            return JSON.parse(stored) as DashboardLayout
        }
    } catch (error) {
        console.warn('Failed to load dashboard layout from localStorage:', error)
    }
    return null
}

/**
 * Save layout to localStorage
 */
function saveToLocalStorage(layout: DashboardLayout): void {
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(layout))
    } catch (error) {
        console.warn('Failed to save dashboard layout to localStorage:', error)
    }
}

/**
 * Validate and migrate layout
 */
function validateLayout(layout: DashboardLayout): DashboardLayout {
    // Version mismatch - return defaults
    if (layout.version !== LAYOUT_VERSION) {
        return { layout: [...DEFAULT_LAYOUT], widgets: [...DEFAULT_WIDGETS], version: LAYOUT_VERSION }
    }
    
    // Ensure all widgets exist
    const missingWidgets = DEFAULT_WIDGETS.filter(
        (w) => !layout.widgets.find((lw) => lw.id === w.id)
    )
    if (missingWidgets.length > 0) {
        layout.widgets.push(...missingWidgets)
        // Add missing layout items
        const missingLayout = DEFAULT_LAYOUT.filter(
            (l) => !layout.layout.find((ll) => ll.i === l.i)
        )
        layout.layout.push(...missingLayout)
    }
    
    return layout
}

/**
 * Composable for managing dashboard widget layout with grid-layout-plus
 */
export function useDashboardLayout() {
    const state = ref<DashboardLayout>({ layout: [], widgets: [], version: LAYOUT_VERSION })
    const isConfigMode = ref(false)
    const isLoading = ref(true)
    const saveTimeout = ref<ReturnType<typeof setTimeout> | null>(null)

    /**
     * Load layout from server, fallback to localStorage, then defaults
     */
    async function loadLayout(): Promise<void> {
        isLoading.value = true
        try {
            // Try server first
            const serverLayout = await services.dashboard.getLayout<string>()
            if (serverLayout) {
                const parsed = JSON.parse(serverLayout) as DashboardLayout
                state.value = validateLayout(parsed)
                // Cache to localStorage
                saveToLocalStorage(state.value)
                isLoading.value = false
                return
            }
        } catch (error) {
            console.warn('Failed to load dashboard layout from server:', error)
        }

        // Fallback to localStorage
        const cachedLayout = loadFromLocalStorage()
        if (cachedLayout) {
            state.value = validateLayout(cachedLayout)
            isLoading.value = false
            return
        }

        // Final fallback: defaults
        state.value = { layout: [...DEFAULT_LAYOUT], widgets: [...DEFAULT_WIDGETS], version: LAYOUT_VERSION }
        isLoading.value = false
    }

    /**
     * Save layout to server (debounced)
     */
    async function saveToServer(): Promise<void> {
        try {
            const layoutJson = JSON.stringify(state.value)
            await services.dashboard.saveLayout(layoutJson)
        } catch (error) {
            console.warn('Failed to save dashboard layout to server:', error)
        }
    }

    /**
     * Debounced save - saves to localStorage immediately, server after 1s delay
     */
    function debouncedSave(): void {
        // Save to localStorage immediately
        saveToLocalStorage(state.value)
        
        // Debounce server save
        if (saveTimeout.value) {
            clearTimeout(saveTimeout.value)
        }
        saveTimeout.value = setTimeout(() => {
            saveToServer()
        }, 1000)
    }

    // Watch for changes and persist (debounced)
    watch(
        state,
        () => {
            if (!isLoading.value) {
                debouncedSave()
            }
        },
        { deep: true }
    )

    /**
     * Get visible layout items sorted by position
     */
    const visibleLayout = computed(() => {
        return state.value.layout.filter((item) => {
            const widget = state.value.widgets.find((w) => w.id === item.i)
            return widget?.visible ?? true
        })
    })

    /**
     * Get all layout items
     */
    const allLayout = computed(() => state.value.layout)

    /**
     * Get hidden widgets
     */
    const hiddenWidgets = computed(() => {
        return state.value.widgets.filter((w) => !w.visible)
    })

    /**
     * Toggle configuration mode
     */
    function toggleConfigMode(): void {
        isConfigMode.value = !isConfigMode.value
    }

    /**
     * Enter configuration mode
     */
    function enterConfigMode(): void {
        isConfigMode.value = true
    }

    /**
     * Exit configuration mode
     */
    function exitConfigMode(): void {
        isConfigMode.value = false
    }

    /**
     * Check if a widget is visible
     */
    function isWidgetVisible(widgetId: string): boolean {
        const widget = state.value.widgets.find((w) => w.id === widgetId)
        return widget?.visible ?? true
    }

    /**
     * Toggle widget visibility
     */
    function toggleWidgetVisibility(widgetId: string): void {
        const widget = state.value.widgets.find((w) => w.id === widgetId)
        if (widget) {
            widget.visible = !widget.visible
        }
    }

    /**
     * Show a hidden widget
     */
    function showWidget(widgetId: string): void {
        const widget = state.value.widgets.find((w) => w.id === widgetId)
        if (widget) {
            widget.visible = true
            // Add back to layout if missing
            if (!state.value.layout.find((l) => l.i === widgetId)) {
                const defaultItem = DEFAULT_LAYOUT.find((l) => l.i === widgetId)
                if (defaultItem) {
                    state.value.layout.push({ ...defaultItem })
                }
            }
        }
    }

    /**
     * Update layout after drag/resize
     */
    function updateLayout(newLayout: LayoutItem[]): void {
        const fullLayout = [...state.value.layout]
        newLayout.forEach((newItem) => {
            const index = fullLayout.findIndex((item) => item.i === newItem.i)
            if (index !== -1) {
                fullLayout[index] = { ...fullLayout[index], ...newItem }
            } else {
                fullLayout.push(newItem)
            }
        })
        state.value.layout = fullLayout
    }

    /**
     * Reset to default layout
     */
    async function resetLayout(): Promise<void> {
        state.value = {
            layout: [...DEFAULT_LAYOUT],
            widgets: [...DEFAULT_WIDGETS],
            version: LAYOUT_VERSION
        }
        saveToLocalStorage(state.value)
        try {
            await services.dashboard.resetLayout()
        } catch (error) {
            console.warn('Failed to reset dashboard layout on server:', error)
        }
    }

    /**
     * Get widget metadata by ID
     */
    function getWidgetMeta(widgetId: string): WidgetMeta | undefined {
        return state.value.widgets.find((w) => w.id === widgetId)
    }

    /**
     * Get layout item by ID
     */
    function getLayoutItem(widgetId: string): LayoutItem | undefined {
        return state.value.layout.find((l) => l.i === widgetId)
    }

    // Initialize on first use
    loadLayout()

    return {
        // State
        layout: allLayout,
        visibleLayout,
        hiddenWidgets,
        isConfigMode: computed(() => isConfigMode.value),
        isLoading: computed(() => isLoading.value),

        // Grid config
        gridCols: GRID_COLS,
        rowHeight: ROW_HEIGHT,
        margin: MARGIN,

        // Actions
        toggleConfigMode,
        enterConfigMode,
        exitConfigMode,
        isWidgetVisible,
        toggleWidgetVisibility,
        showWidget,
        updateLayout,
        resetLayout,
        getWidgetMeta,
        getLayoutItem,
        
        // Expose for initialization
        loadLayout
    }
}

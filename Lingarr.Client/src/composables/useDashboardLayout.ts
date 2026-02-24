import { ref, watch, computed } from 'vue'

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
const LAYOUT_VERSION = 2

// Grid configuration
export const GRID_COLS = 12
export const ROW_HEIGHT = 80
export const MARGIN = [16, 16] as [number, number]

/**
 * Default widget layout configuration
 * Each widget has position (x, y), size (w, h), and constraints
 */
const DEFAULT_LAYOUT: LayoutItem[] = [
    { i: 'active-translations', x: 0, y: 0, w: 12, h: 4, minW: 6, minH: 3 },
    { i: 'media-overview', x: 0, y: 4, w: 12, h: 4, minW: 6, minH: 3 },
    { i: 'translation-activity', x: 0, y: 8, w: 6, h: 5, minW: 4, minH: 3 },
    { i: 'language-statistics', x: 6, y: 8, w: 6, h: 5, minW: 4, minH: 4 },
    { i: 'translation-history', x: 0, y: 13, w: 6, h: 5, minW: 4, minH: 3 },
    { i: 'job-queue', x: 6, y: 13, w: 3, h: 5, minW: 3, minH: 3 },
    { i: 'api-usage', x: 9, y: 13, w: 3, h: 5, minW: 3, minH: 3 },
    { i: 'error-log', x: 0, y: 18, w: 12, h: 4, minW: 6, minH: 3 }
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
function loadLayout(): DashboardLayout {
    try {
        const stored = localStorage.getItem(STORAGE_KEY)
        if (stored) {
            const layout = JSON.parse(stored) as DashboardLayout
            // Migrate if version mismatch
            if (layout.version !== LAYOUT_VERSION) {
                return { layout: DEFAULT_LAYOUT, widgets: DEFAULT_WIDGETS, version: LAYOUT_VERSION }
            }
            // Ensure all widgets exist
            const missingWidgets = DEFAULT_WIDGETS.filter(
                w => !layout.widgets.find(lw => lw.id === w.id)
            )
            if (missingWidgets.length > 0) {
                layout.widgets.push(...missingWidgets)
                // Add missing layout items
                const missingLayout = DEFAULT_LAYOUT.filter(
                    l => !layout.layout.find(ll => ll.i === l.i)
                )
                layout.layout.push(...missingLayout)
            }
            return layout
        }
    } catch (error) {
        console.warn('Failed to load dashboard layout:', error)
    }
    return { layout: DEFAULT_LAYOUT, widgets: DEFAULT_WIDGETS, version: LAYOUT_VERSION }
}

/**
 * Save layout to localStorage
 */
function saveLayout(layout: DashboardLayout): void {
    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(layout))
    } catch (error) {
        console.warn('Failed to save dashboard layout:', error)
    }
}

/**
 * Composable for managing dashboard widget layout with grid-layout-plus
 */
export function useDashboardLayout() {
    const state = ref<DashboardLayout>(loadLayout())
    const isConfigMode = ref(false)

    // Watch for changes and persist
    watch(state, (newState) => {
        saveLayout(newState)
    }, { deep: true })

    /**
     * Get visible layout items sorted by position
     */
    const visibleLayout = computed(() => {
        return state.value.layout.filter(item => {
            const widget = state.value.widgets.find(w => w.id === item.i)
            return widget?.visible ?? true
        })
    })

    /**
     * Get all layout items
     */
    const allLayout = computed(() => state.value.layout)

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
        const widget = state.value.widgets.find(w => w.id === widgetId)
        return widget?.visible ?? true
    }

    /**
     * Toggle widget visibility
     */
    function toggleWidgetVisibility(widgetId: string): void {
        const widget = state.value.widgets.find(w => w.id === widgetId)
        if (widget) {
            widget.visible = !widget.visible
        }
    }

    /**
     * Update layout after drag/resize
     */
    function updateLayout(newLayout: LayoutItem[]): void {
        state.value.layout = newLayout
    }

    /**
     * Reset to default layout
     */
    function resetLayout(): void {
        state.value = {
            layout: [...DEFAULT_LAYOUT],
            widgets: [...DEFAULT_WIDGETS],
            version: LAYOUT_VERSION
        }
    }

    /**
     * Get widget metadata by ID
     */
    function getWidgetMeta(widgetId: string): WidgetMeta | undefined {
        return state.value.widgets.find(w => w.id === widgetId)
    }

    /**
     * Get layout item by ID
     */
    function getLayoutItem(widgetId: string): LayoutItem | undefined {
        return state.value.layout.find(l => l.i === widgetId)
    }

    return {
        // State
        layout: allLayout,
        visibleLayout,
        isConfigMode: computed(() => isConfigMode.value),
        
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
        updateLayout,
        resetLayout,
        getWidgetMeta,
        getLayoutItem
    }
}

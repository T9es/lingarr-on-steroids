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
const LAYOUT_VERSION = 6

// Grid configuration
export const GRID_COLS = 12
export const ROW_HEIGHT = 80
export const MARGIN = [16, 16] as [number, number]

/**
 * Default widget layout configuration
 * Each widget has position (x, y), size (w, h), and constraints
 * 
 * Layout structure:
 * Row 0:  active-translations (full width)
 * Row 3:  translation-history (full width - merged with activity)
 * Row 7:  media-overview (left) | error-log (right)
 * Row 11: job-queue (left) | api-usage (right)
 */
const DEFAULT_LAYOUT: LayoutItem[] = [
    { i: 'active-translations', x: 0, y: 0, w: 12, h: 3, minW: 6, minH: 2 },
    { i: 'translation-history', x: 0, y: 3, w: 12, h: 6, minW: 6, minH: 4 },
    { i: 'media-overview', x: 0, y: 9, w: 6, h: 2, minW: 4, minH: 2 },
    { i: 'error-log', x: 6, y: 9, w: 6, h: 4, minW: 4, minH: 2 },
    { i: 'job-queue', x: 0, y: 13, w: 6, h: 4, minW: 3, minH: 3 },
    { i: 'api-usage', x: 6, y: 13, w: 6, h: 4, minW: 3, minH: 3 }
]

const DEFAULT_WIDGETS: WidgetMeta[] = [
    { id: 'active-translations', title: 'statistics.activeTranslations', visible: true },
    { id: 'translation-history', title: 'statistics.translationHistory', visible: true },
    { id: 'media-overview', title: 'statistics.mediaOverview', visible: true },
    { id: 'error-log', title: 'statistics.errorLog', visible: true },
    { id: 'job-queue', title: 'statistics.jobQueue', visible: true },
    { id: 'api-usage', title: 'statistics.apiUsage', visible: true }
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
 * Migrate layout from older versions
 * Preserves user's widget positions while handling structural changes
 */
function migrateLayout(layout: DashboardLayout): DashboardLayout {
    if (layout.version >= LAYOUT_VERSION) {
        return layout
    }
    
    let migrated: DashboardLayout = {
        layout: [...layout.layout],
        widgets: [...layout.widgets],
        version: LAYOUT_VERSION
    }
    
    // Version 5 to 6: Extend translation-history widget height
    if (layout.version < 6) {
        const historyWidget = migrated.layout.find(item => item.i === 'translation-history')
        if (historyWidget) {
            historyWidget.h = 6
            historyWidget.minH = 4
        }
        
        // Recalculate y positions for widgets below translation-history
        const widgetsBelow = migrated.layout.filter(item => 
            item.i !== 'translation-history' && item.y >= 3
        )
        widgetsBelow.forEach(item => {
            item.y += 2
        })
    }
    
    // Version 4 to 5: Remove translation-activity widget
    if (layout.version < 5) {
        // Remove the deprecated widget from layout
        migrated.layout = migrated.layout.filter(item => item.i !== 'translation-activity')
        
        // Remove the deprecated widget from widgets list
        migrated.widgets = migrated.widgets.filter(w => w.id !== 'translation-activity')
        
        // Add any missing widgets that should exist in v5
        DEFAULT_WIDGETS.forEach(defaultWidget => {
            if (!migrated.widgets.find(w => w.id === defaultWidget.id)) {
                migrated.widgets.push({ ...defaultWidget })
                
                // Add layout item if missing
                const defaultLayoutItem = DEFAULT_LAYOUT.find(l => l.i === defaultWidget.id)
                if (defaultLayoutItem && !migrated.layout.find(l => l.i === defaultWidget.id)) {
                    migrated.layout.push({ ...defaultLayoutItem })
                }
            }
        })
    }
    
    return migrated
}

/**
 * Validate layout has all required widgets
 */
function validateLayout(layout: DashboardLayout): DashboardLayout {
    // Ensure all default widgets exist
    const missingWidgets = DEFAULT_WIDGETS.filter(
        (defaultWidget) => !layout.widgets.find((w) => w.id === defaultWidget.id)
    )
    
    if (missingWidgets.length > 0) {
        layout.widgets.push(...missingWidgets)
        
        // Add missing layout items
        missingWidgets.forEach((widget) => {
            const defaultItem = DEFAULT_LAYOUT.find((l) => l.i === widget.id)
            if (defaultItem && !layout.layout.find((l) => l.i === widget.id)) {
                layout.layout.push({ ...defaultItem })
            }
        })
    }
    
    return layout
}

// Singleton state
const state = ref<DashboardLayout>({
    layout: [...DEFAULT_LAYOUT],
    widgets: [...DEFAULT_WIDGETS],
    version: LAYOUT_VERSION
})
const isConfigMode = ref(false)
const isLoading = ref(false)

// Debounce save to server
let saveTimeout: ReturnType<typeof setTimeout> | null = null

/**
 * Save layout to server (debounced)
 */
async function saveToServer(layout: DashboardLayout): Promise<void> {
    if (saveTimeout) {
        clearTimeout(saveTimeout)
    }
    
    saveTimeout = setTimeout(async () => {
        try {
            await services.dashboard.saveLayout(JSON.stringify(layout))
        } catch (error) {
            console.warn('Failed to save dashboard layout to server:', error)
        }
    }, 1000)
}

/**
 * Load layout from server/localStorage/defaults
 */
async function loadLayout(): Promise<void> {
    isLoading.value = true
    
    try {
        // Try server first
        const serverLayout = await services.dashboard.getLayout<string>()
        
        if (serverLayout) {
            try {
                const parsed = JSON.parse(serverLayout) as DashboardLayout
                const migrated = migrateLayout(parsed)
                const validated = validateLayout(migrated)
                state.value = validated
                saveToLocalStorage(validated)
            } catch {
                // Invalid JSON, use localStorage
                const localLayout = loadFromLocalStorage()
                if (localLayout) {
                    const migrated = migrateLayout(localLayout)
                    const validated = validateLayout(migrated)
                    state.value = validated
                }
            }
        } else {
            // No server layout, try localStorage
            const localLayout = loadFromLocalStorage()
            if (localLayout) {
                const migrated = migrateLayout(localLayout)
                const validated = validateLayout(migrated)
                state.value = validated
            }
        }
    } catch (error) {
        console.warn('Failed to load dashboard layout from server:', error)
        
        // Fallback to localStorage
        const localLayout = loadFromLocalStorage()
        if (localLayout) {
            const migrated = migrateLayout(localLayout)
            const validated = validateLayout(migrated)
            state.value = validated
        }
    } finally {
        isLoading.value = false
    }
}

// Watch for changes and save
watch(
    state,
    (newLayout) => {
        saveToLocalStorage(newLayout)
        saveToServer(newLayout)
    },
    { deep: true }
)

export function useDashboardLayout() {
    /**
     * Get visible layout items
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
        
        // Helpers
        getWidgetMeta,
        getLayoutItem
    }
}
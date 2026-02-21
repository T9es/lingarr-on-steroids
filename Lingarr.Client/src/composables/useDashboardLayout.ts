import { ref, watch, readonly } from 'vue'

/**
 * Widget configuration interface
 */
export interface WidgetConfig {
    id: string
    title: string
    size: 'full' | 'half' | 'third'
    order: number
    visible: boolean
}

/**
 * Dashboard layout state
 */
export interface DashboardLayout {
    widgets: WidgetConfig[]
    version: number
}

const STORAGE_KEY = 'lingarr-dashboard-layout'
const LAYOUT_VERSION = 1

/**
 * Default widget configuration
 */
const DEFAULT_WIDGETS: WidgetConfig[] = [
    { id: 'active-translations', title: 'statistics.activeTranslations', size: 'full', order: 0, visible: true },
    { id: 'media-overview', title: 'statistics.mediaOverview', size: 'full', order: 1, visible: true },
    { id: 'translation-activity', title: 'statistics.translationActivity', size: 'half', order: 2, visible: true },
    { id: 'language-statistics', title: 'statistics.languageStatistics', size: 'half', order: 3, visible: true },
    { id: 'translation-history', title: 'statistics.translationHistory', size: 'half', order: 4, visible: true },
    { id: 'job-queue', title: 'statistics.jobQueue', size: 'third', order: 5, visible: true },
    { id: 'api-usage', title: 'statistics.apiUsage', size: 'third', order: 6, visible: true },
    { id: 'error-log', title: 'statistics.errorLog', size: 'third', order: 7, visible: true }
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
                return { widgets: DEFAULT_WIDGETS, version: LAYOUT_VERSION }
            }
            return layout
        }
    } catch (error) {
        console.warn('Failed to load dashboard layout:', error)
    }
    return { widgets: DEFAULT_WIDGETS, version: LAYOUT_VERSION }
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
 * Composable for managing dashboard widget layout
 */
export function useDashboardLayout() {
    const layout = ref<DashboardLayout>(loadLayout())
    const isConfigMode = ref(false)
    const draggedWidgetId = ref<string | null>(null)

    // Watch for changes and persist
    watch(layout, (newLayout) => {
        saveLayout(newLayout)
    }, { deep: true })

    /**
     * Get widgets sorted by order
     */
    function getSortedWidgets(): WidgetConfig[] {
        return [...layout.value.widgets]
            .filter(w => w.visible)
            .sort((a, b) => a.order - b.order)
    }

    /**
     * Get all widgets (including hidden)
     */
    function getAllWidgets(): WidgetConfig[] {
        return [...layout.value.widgets].sort((a, b) => a.order - b.order)
    }

    /**
     * Toggle configuration mode
     */
    function toggleConfigMode(): void {
        isConfigMode.value = !isConfigMode.value
        draggedWidgetId.value = null
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
        draggedWidgetId.value = null
    }

    /**
     * Check if a widget is visible
     */
    function isWidgetVisible(widgetId: string): boolean {
        const widget = layout.value.widgets.find(w => w.id === widgetId)
        return widget?.visible ?? true
    }

    /**
     * Handle drag start
     */
    function handleDragStart(widgetId: string): void {
        if (!isConfigMode.value) return
        draggedWidgetId.value = widgetId
    }

    /**
     * Handle drag end
     */
    function handleDragEnd(): void {
        draggedWidgetId.value = null
    }

    /**
     * Handle drag over (for visual feedback)
     */
    function handleDragOver(_widgetId: string): void {
        // This is just for visual feedback during drag
        // The actual reorder happens on drop
    }

    /**
     * Handle drop - reorder widgets
     */
    function handleDrop(targetWidgetId: string): void {
        if (!isConfigMode.value || !draggedWidgetId.value) return
        if (draggedWidgetId.value === targetWidgetId) return

        const widgets = [...layout.value.widgets]
        const draggedIndex = widgets.findIndex(w => w.id === draggedWidgetId.value)
        const targetIndex = widgets.findIndex(w => w.id === targetWidgetId)

        if (draggedIndex === -1 || targetIndex === -1) return

        // Swap orders
        const draggedOrder = widgets[draggedIndex].order
        const targetOrder = widgets[targetIndex].order

        widgets[draggedIndex].order = targetOrder
        widgets[targetIndex].order = draggedOrder

        layout.value.widgets = widgets
        draggedWidgetId.value = null
    }

    /**
     * Toggle widget visibility
     */
    function toggleWidgetVisibility(widgetId: string): void {
        const widget = layout.value.widgets.find(w => w.id === widgetId)
        if (widget) {
            widget.visible = !widget.visible
        }
    }

    /**
     * Reset to default layout
     */
    function resetLayout(): void {
        layout.value = { widgets: DEFAULT_WIDGETS, version: LAYOUT_VERSION }
    }

    /**
     * Get widget by ID
     */
    function getWidget(widgetId: string): WidgetConfig | undefined {
        return layout.value.widgets.find(w => w.id === widgetId)
    }

    return {
        layout: readonly(layout),
        isConfigMode: readonly(isConfigMode),
        draggedWidgetId: readonly(draggedWidgetId),
        getSortedWidgets,
        getAllWidgets,
        toggleConfigMode,
        enterConfigMode,
        exitConfigMode,
        isWidgetVisible,
        handleDragStart,
        handleDragEnd,
        handleDragOver,
        handleDrop,
        toggleWidgetVisibility,
        resetLayout,
        getWidget
    }
}

# Dashboard Layout Stability Fix

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix dashboard layout instability caused by sidebar toggle and resolution changes on 16:10 screens.

**Architecture:** Add dynamic margin to main content that tracks sidebar state, and use ResizeObserver to provide actual container width to the grid layout system instead of viewport-based breakpoints.

**Tech Stack:** Vue 3, TypeScript, grid-layout-plus, Tailwind CSS

---

## Problem Analysis

**Root Causes:**
1. `AsideNavigation.vue` uses `fixed` positioning (lines 9-13), but `PageLayout.vue` main content has NO margin to account for sidebar width
2. `StatisticsComponent.vue` uses viewport-based breakpoints (lines 59-61) that don't account for sidebar width changes
3. When sidebar toggles (256px ↔ 80px), the grid doesn't recalculate available space

**Files to modify:**
- `Lingarr.Client/src/components/layout/PageLayout.vue`
- `Lingarr.Client/src/components/features/dashboard/StatisticsComponent.vue`

---

### Task 1: Add Dynamic Margin to Main Content

**Files:**
- Modify: `Lingarr.Client/src/components/layout/PageLayout.vue`

**Current State (line 6):**
```vue
<div class="flex w-full flex-col drop-shadow-xl">
```

**Step 1: Add dynamic margin classes to main content div**

Change line 6 to:

```vue
<div 
    class="flex flex-col drop-shadow-xl transition-all duration-300 ease-in-out w-full"
    :class="isOpen ? 'md:ml-64' : 'md:ml-20'">
```

**Explanation:**
- `md:ml-64` = 256px margin on md+ screens (matches open sidebar width)
- `md:ml-20` = 80px margin on md+ screens (matches collapsed sidebar width)
- `transition-all duration-300 ease-in-out` = smooth animation matching sidebar transition
- Mobile screens don't get margin (sidebar uses overlay mode)

**Step 2: Verify the isOpen computed is available**

The `isOpen` computed property already exists at lines 59-62:
```typescript
const isOpen: ComputedRef<boolean> = computed({
    get: () => instanceStore.getIsOpen,
    set: (value: boolean) => instanceStore.setIsOpen(value)
})
```

No additional imports needed.

**Step 3: Test manually**

1. Run `cd Lingarr.Client && npm run dev`
2. Open dashboard in browser
3. Click sidebar toggle button
4. Verify: Main content should smoothly slide left/right to fill available space
5. Test on mobile (< 768px): Sidebar should overlay, content should NOT have margin

**Step 4: Commit**

```bash
git add Lingarr.Client/src/components/layout/PageLayout.vue
git commit -m "fix: add dynamic margin to main content for sidebar state"
```

---

### Task 2: Add Container Width Tracking to Grid

**Files:**
- Modify: `Lingarr.Client/src/components/features/dashboard/StatisticsComponent.vue`

**Current Issue:** Grid uses viewport breakpoints that don't account for sidebar width.

**Step 1: Add container ref and width tracking**

Add after line 186 (after the existing composables):

```typescript
const gridContainerRef = ref<HTMLElement | null>(null)
const containerWidth = ref(1200)
let resizeObserver: ResizeObserver | null = null
```

**Step 2: Modify onMounted to set up ResizeObserver**

Change the `onMounted` block (lines 281-286) to:

```typescript
onMounted(async () => {
    await connectSignalR()
    await loadInitialTranslations()
    await fetchDailyStats()
    await fetchStatistics()
    
    // Set up resize observer for grid container
    if (gridContainerRef.value) {
        resizeObserver = new ResizeObserver((entries) => {
            for (const entry of entries) {
                containerWidth.value = entry.contentRect.width
            }
        })
        resizeObserver.observe(gridContainerRef.value)
    }
})
```

**Step 3: Modify onUnmounted to clean up observer**

Change the `onUnmounted` block (lines 288-290) to:

```typescript
onUnmounted(() => {
    disconnectSignalR()
    if (resizeObserver) {
        resizeObserver.disconnect()
        resizeObserver = null
    }
})
```

**Step 4: Wrap GridLayout in container div with ref**

Change lines 48-62 from:

```vue
<!-- Widget Grid -->
<GridLayout
    v-else
    v-model:layout="currentLayout"
    :col-num="gridCols"
    ...
```

To:

```vue
<!-- Widget Grid -->
<div ref="gridContainerRef" class="w-full">
    <GridLayout
        v-else
        v-model:layout="currentLayout"
        :col-num="gridCols"
        :row-height="rowHeight"
        :margin="margin"
        :is-draggable="isConfigMode"
        :is-resizable="isConfigMode"
        :vertical-compact="true"
        :use-css-transforms="true"
        :responsive="true"
        :breakpoints="{ lg: 1200, md: 996, sm: 768, xs: 480, xxs: 0 }"
        :cols="{ lg: 12, md: 8, sm: 4, xs: 2, xxs: 1 }"
        class="min-h-[200px]">
```

And close the wrapper div after the GridLayout closing tag (after line 113):

```vue
        </GridLayout>
    </div>
```

**Step 5: Verify changes compile**

Run: `cd Lingarr.Client && npm run build`
Expected: Build succeeds without errors

**Step 6: Test manually**

1. Run `npm run dev`
2. Open dashboard
3. Toggle sidebar open/closed
4. Verify: Widgets should reflow smoothly without jumping
5. Resize browser window - widgets should adapt to available space

**Step 7: Commit**

```bash
git add Lingarr.Client/src/components/features/dashboard/StatisticsComponent.vue
git commit -m "fix: add ResizeObserver to track actual grid container width"
```

---

### Task 3: Verify Full Solution

**Step 1: Run frontend build**

```bash
cd Lingarr.Client && npm run build
```

Expected: Build succeeds with no TypeScript errors

**Step 2: Manual testing checklist**

Test on both 16:9 and 16:10 aspect ratios:

- [ ] Sidebar toggle: Content smoothly adjusts margin
- [ ] Grid widgets: No jumping when sidebar toggles
- [ ] Window resize: Widgets reflow smoothly
- [ ] Mobile (< 768px): Sidebar overlays correctly, no margin on content
- [ ] 16:10 screen: Layout stable at various widths

**Step 3: Final commit if all tests pass**

```bash
git status
# If any uncommitted changes remain:
git add -A
git commit -m "fix: resolve dashboard layout stability issues"
```

---

## Files Changed Summary

| File | Change |
|------|--------|
| `PageLayout.vue` | Added dynamic `:class` for margin based on sidebar state |
| `StatisticsComponent.vue` | Added ResizeObserver to track actual container width |

## No Backend Changes Required

This is a pure frontend CSS/Vue fix. No backend changes, no migrations, no API changes.

## Testing Strategy

Manual testing required - this is a visual/layout fix. Test on:
- 16:9 screens (1920x1080, 2560x1440)
- 16:10 screens (1920x1200, 2560x1600)
- Mobile view (< 768px width)
- Various sidebar states (open/closed)
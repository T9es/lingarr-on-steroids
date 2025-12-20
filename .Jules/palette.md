# Palette's Journal

## 2024-05-21 - Accessibility in Icon-Only Buttons
**Learning:** Found pattern of icon-only buttons implemented without `aria-label` or `title` attributes, making them inaccessible to screen readers.
**Action:** When working on components with icon-only actions, always enforce `aria-label` and `title` attributes using localized strings. Ensure proper focus and hover states are added as default styles were missing.

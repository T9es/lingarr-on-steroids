# Palette's Journal

## 2024-05-21 - Accessibility in Icon-Only Buttons
**Learning:** Found pattern of icon-only buttons implemented without `aria-label` or `title` attributes, making them inaccessible to screen readers.
**Action:** When working on components with icon-only actions, always enforce `aria-label` and `title` attributes using localized strings. Ensure proper focus and hover states are added as default styles were missing.

## 2025-02-18 - Semantic HTML in Interactive Components
**Learning:** Found components (SearchComponent) using `div` or icons with click handlers instead of native `<button>` elements, compromising accessibility and keyboard navigation.
**Action:** Replace click-bound divs/icons with `<button type="button">`. Ensure `aria-label` is present for icon-only buttons and visible focus styles are applied (e.g., `focus:ring`).

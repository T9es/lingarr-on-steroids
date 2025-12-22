# Palette's Journal

## 2025-12-22 - Progress Bar Accessibility
**Learning:** Custom visual progress bars built with `div`s are completely invisible to screen readers, leaving users unaware of status or completion rates.
**Action:** Always wrap visual progress bars with `role="progressbar"` and provide `aria-valuenow`, `aria-valuemin`, `aria-valuemax`, and a descriptive `aria-label`.

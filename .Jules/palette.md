# Palette's Journal

## 2025-12-22 - Progress Bar Accessibility
**Learning:** Custom visual progress bars built with `div`s are completely invisible to screen readers, leaving users unaware of status or completion rates.
**Action:** Always wrap visual progress bars with `role="progressbar"` and provide `aria-valuenow`, `aria-valuemin`, `aria-valuemax`, and a descriptive `aria-label`.

## 2025-01-20 - Async Feedback in Data-Heavy Tables
**Learning:** Users often click "Retry" or "Refresh" actions multiple times when there's no immediate visual feedback, especially in tables where the row state might not update instantly.
**Action:** Always couple async table actions with a local loading state (like a spinner inside the button) rather than relying solely on global loading indicators or eventual data refresh.

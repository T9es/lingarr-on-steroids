## 2025-12-20 - EF Core Over-fetching in Display Logic
**Learning:** Found `FormatMediaTitle` fetching full entity graphs (Episode -> Season -> Show) including all columns just to format a display string. This causes unnecessary data transfer and object tracking overhead.
**Action:** Use `Select` projections to fetch only the specific columns needed for read-only operations. Avoid `Include` when not modifying the related entities.

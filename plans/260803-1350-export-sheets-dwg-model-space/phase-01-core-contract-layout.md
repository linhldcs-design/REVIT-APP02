---
phase: 1
title: "Core contracts và layout planner"
status: completed
priority: P1
effort: "1 ngày"
dependencies: [0]
---

# Phase 01: Core contracts và layout planner

## Overview

Định nghĩa manifest/version/result và toàn bộ toán tỷ lệ, thứ tự, đơn vị, X-offset ở layer thuần để test ngoài Revit/AutoCAD.

## Requirements

- DTO immutable, JSON schema versioned và bounded.
- Không reference RevitAPI hoặc AutoCAD managed assemblies.
- Planner deterministic với sheet/view order đầu vào.

## Related Files

- Create: `RevitAPP.Core/Models/DwgExport/DwgExportJob.cs`
- Create: `RevitAPP.Core/Models/DwgExport/DwgSheetPlan.cs`
- Create: `RevitAPP.Core/Models/DwgExport/DwgViewportPlan.cs`
- Create: `RevitAPP.Core/Models/DwgExport/DwgPostProcessResult.cs`
- Create: `RevitAPP.Core/Services/DwgSheetLayoutPlanner.cs`
- Create: `RevitAPP.Core/Services/DwgExportJobStore.cs`
- Create: `tests/RevitAPP.Tests/DwgExport/DwgSheetLayoutPlannerTests.cs`
- Create: `tests/RevitAPP.Tests/DwgExport/DwgExportJobStoreTests.cs`

## Implementation Steps

1. Định nghĩa job id, source RVT fingerprint, setup/version/unit, ordered sheets, ordered viewports, source/output paths và timeout.
2. Lưu denominator, center/outline, rotation/crop metadata cần thiết theo contract Phase 00.
3. Implement scale factors với guard scale <= 0, NaN/Infinity và overflow.
4. Implement sheet X-offset từ extents + gap vật lý đã convert.
5. Implement atomic manifest/result write; giới hạn count/path length/file size.
6. Canonicalize và validate mọi path nằm trong owned staging root, trừ final output do user chọn.

## Tests

- 1:75 + 1:25 -> 1:75 là reference; geometry factor lần lượt `1`/`3` và dimension factor lần lượt `1`/`1/3` theo contract.
- Nhiều view cùng scale; rotated view; sheet rỗng; invalid scale.
- Sheet extents khác nhau vẫn không overlap và giữ order.
- JSON round-trip, schema mismatch, corrupt/oversized manifest, path traversal.

## Success Criteria

- [x] Core tests không load Autodesk DLL.
- [x] Công thức scale và layout đúng bằng test số.
- [x] Manifest reject dữ liệu/path không an toàn.
- [x] Contract đủ để truyền setup, sheet, viewport, tỷ lệ, staging và output giữa Revit host/AutoCAD automation.

## Risks

- Metadata thiếu sẽ buộc worker suy đoán; review manifest với evidence Phase 00 trước khi khóa schema v1.

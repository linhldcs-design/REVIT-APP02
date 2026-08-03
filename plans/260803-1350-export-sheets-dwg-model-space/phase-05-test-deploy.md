---
phase: 5
title: "Test matrix, deploy và hand-off"
status: in-progress
priority: P1
effort: "1 ngày"
dependencies: [2, 3, 4]
---

# Phase 05: Test matrix, deploy và hand-off

## Overview

Chạy toàn bộ gate unit/build/runtime, đóng gói worker và viết hướng dẫn vận hành/khắc phục lỗi theo hành vi thực tế.

## Requirements

- Pure tests pass.
- Revit builds R22-R27 pass.
- AutoCAD COM automation chỉ được claim trên bản đầy đủ thực sự cài/chạy.
- End-to-end smoke từ Revit command đến file DWG cuối.

## Related Files

- Modify: `RevitAI.slnx` hoặc build orchestration tương ứng.
- Modify/Create: packaging/deploy entries cho `RevitDwgPostProcessor`.
- Create: `docs/export-revit-sheets-to-dwg.md`.
- Create: test fixture checklist/report; tránh commit binary lớn nếu không cần.

## Implementation Steps

1. Chạy Core tests và toàn bộ regression `RevitAPP.Tests`.
2. Build Revit Debug/Release R22-R27; ghi rõ R22 order fallback.
3. Xác minh AutoCAD COM ProgID/instance ownership trên bản AutoCAD đầy đủ thực sự cài đặt.
4. Deploy add-in R25 sau khi Revit đóng; xác minh đường dẫn có space/Unicode.
5. Smoke các case: one-scale, mixed-scale, nhiều sheet, R2007, missing dependency, cancel, timeout, existing output.
6. So sánh visual baseline và đo dimension; chạy AutoCAD `AUDIT`.
7. Red-team lại process execution, path ownership, atomic output và cleanup.
8. Cập nhật docs theo matrix đã thực sự verify và tạo hand-off ngắn.

## Verification Commands

```powershell
dotnet test tests\RevitAPP.Tests\RevitAPP.Tests.csproj -c Debug
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R22
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R23
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R24
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R25
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R26
dotnet build RevitAPP\RevitAPP.csproj -c Debug.R27
dotnet build RevitAPP\RevitAPP.csproj -c Release.R25
```

Runtime gate dùng AutoCAD đầy đủ qua COM Automation; không dùng kết quả Core Console làm bằng chứng vì `EXPORTLAYOUT` đã không chạy ổn định ở đó.

## Success Criteria

- [x] Pure tests **343/343** pass; contract tỷ lệ, viewport-region mapping, bao phủ DIM CAD, tên style không trùng và validation đường dẫn đã được cập nhật.
- [x] Build `Debug.R25` đạt **0 lỗi**.
- [x] Deploy bản mixed-scale mới vào Addins 2025 sau khi đóng Revit.
- [x] External-worker end-to-end cuối từ staging thật tạo đúng một self-contained DWG Model Space: 34/34 sheet ngay attempt 1, không còn worker/AutoCAD; 1.695/1.695 DIM và Text Style đạt contract annotative.
- [ ] Sheet order/layout/mixed-scale/dimension đạt acceptance matrix.
- [ ] Cancel/failure/timeout không sửa RVT hoặc file output cũ.
- [x] Docs nêu dependency AutoCAD đầy đủ, worker ngoài Revit, watchdog/retry, ownership lease và hợp đồng không phụ thuộc AutoCAD add-in.

Build, pure tests, full runtime smoke và `AUDIT` đã đạt. Phase 05 vẫn `in-progress` chỉ vì còn gate người dùng kiểm tra trực quan thứ tự sheet, layout và anchor trên file cuối.

## Risks

- Không thể runtime-smoke AutoCAD version không cài; phải ghi `build-only`, không ghi `supported/verified` quá mức.
- Packaging worker cạnh Revit add-in dễ thiếu runtime-specific DLL/script; kiểm tra trên máy sạch là release gate.

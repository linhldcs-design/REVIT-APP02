---
phase: 6
title: "Test, smoke, phát hành và hand-off"
status: pending
priority: P1
effort: "2 ngày"
dependencies: [5]
---

# Phase 06: Test, smoke, phát hành và hand-off

## Overview

Chốt chất lượng bằng test thuần, build hai cấu hình, smoke trong Revit 2025 và bàn giao trạng thái có thể tiếp tục khi hết quota.

## Requirements

- Không coi build xanh là hoàn thành nếu chưa smoke view/reference/layout trên model thật.
- DLL phát hành qua Add-In Manager; không ghi đè assembly đang được Revit load.
- Hand-off ghi rõ commit/worktree state, DLL path/hash, case đã test, lỗi còn lại và bước tiếp theo.

## Related Files

- Create: `RevitAPP/docs/BEAM-LONGITUDINAL-DRAWING-HANDOFF.md`
- Modify: mọi test/implementation file từ Phase 00-05 khi smoke phát hiện lỗi
- Update: `plans/260722-1326-beam-longitudinal-drawing/plan.md` và phase status

## Implementation Steps

1. Chạy toàn bộ `RevitAPP.Tests`; sửa regression trong scope.
2. Build `Debug.R25` và `Release.R25` với `DeployAddin=false`.
3. Tạo DLL smoke có assembly identity riêng nếu `RevitAPP` đã auto-load; ghi SHA-256 và timestamp.
4. Smoke bốn model cases trong Acceptance Matrix; chạy lại cùng selection để kiểm tra naming/idempotency UX.
5. Đối chiếu trực quan PDF: chụp sheet output, checklist từng nhóm và ghi deviation có chủ ý.
6. Smoke regression command mặt cắt ngang.
7. Cập nhật hand-off sau mỗi phiên smoke; chỉ đánh dấu complete khi mọi acceptance bắt buộc pass hoặc residual risk được user chấp nhận.

## Tests

- `dotnet test tests/RevitAPP.Tests/RevitAPP.Tests.csproj -c Release`
- `dotnet build RevitAPP/RevitAPP.csproj -c Debug.R25 -p:DeployAddin=false`
- `dotnet build RevitAPP/RevitAPP.csproj -c Release.R25 -p:DeployAddin=false`
- Manual Revit 2025 smoke + PDF comparison checklist.

## Success Criteria

- [ ] Unit tests và hai build R25 xanh, không thêm warning mới thuộc feature.
- [ ] Bốn smoke cases pass, bao gồm gối chung giống/khác và dầm xoay.
- [ ] Output khớp cấu trúc PDF: longitudinal, rebar/tag, upper/lower dims, spot, cross views và sheet.
- [ ] Regression mặt cắt ngang pass.
- [ ] Hand-off có DLL path/hash, trạng thái và next action rõ ràng.

## Risks

- Add-In Manager dùng nhầm assembly đã cache: kiểm tra assembly identity/hash từ process trước khi kết luận smoke.

# Project Changelog

## 2026-07-17

### AI Chat Panel + native tool control

- Expanded Chat to 47 tools: all 15 RevitAPP ribbon buttons are callable, and four background Excel tools can discover open workbooks, find, inspect, and read `.xls/.xlsx/.xlsm/.xlsb/.csv` files without blocking the Revit UI.
- Added encrypted advanced local memory: project-scoped conversations/tool outcomes, pinned global preferences, relevant-memory retrieval across sessions, 500-entry bound and deduplication, API-key redaction, direct memory-management chat commands, and tracking of Rebar ids created by the most recent draw action.
- Expanded the Chat AI registry from 7 to 28 tools by loading the 21 installed Revit MCP command objects directly in-process. Commands reuse their own `ExternalEvent`; Chat no longer depends on the MCP TCP server or port 8080. Added write/delete/C# confirmation gates and prompt routing for selection, deletion, visibility, creation, tagging, dimensions, quantities, and model analysis.

- Thêm nút ribbon mở AI Chat Panel dạng WPF modeless; hỗ trợ Anthropic Claude, OpenAI và Google Gemini. API key/cấu hình người dùng được lưu cục bộ bằng Windows DPAPI, không ghi plaintext vào repo.
- Chuẩn hóa wire protocol thuần trong `RevitAPP.Core`: message/schema, request builder và response parser không phụ thuộc Revit API, cho phép kiểm thử ngoài Revit.
- Thêm neutral tool registry gồm 7 tool: 5 tool ghi mô hình (`draw_column_rebar`, `draw_wall_rebar`, `draw_beam_rebar`, `draw_footing_rebar`, `draw_beam_drawing`) và 2 tool chỉ đọc (`get_selected_elements`, `get_current_view_info`). Registry chuyển schema trung lập sang envelope riêng của từng nhà cung cấp rồi dispatch về engine hiện hữu.
- Mọi truy cập Revit API từ panel modeless đi qua `ExternalEvent`. License được kiểm tra trước dispatch; chỉ tool cột mở transaction ở caller, các engine còn lại tự quản lý transaction để tránh transaction lồng nhau.
- Xác nhận `RevitAPP.Tests` pass 151/151 và build `Release.R22` đến `Release.R27` đều thành công.

## 2026-07-13

### Footing Section Drawing (Mặt Cắt Móng, MC 2-2)

- Thêm command native `FootingSectionDrawingCommand` vào RevitAPP — nút ribbon "Mat Cat Mong" (sau "Ban Ve Mong"), phát qua Add-In Manager. Pick 1 móng (đã có rebar sẵn) → cấu hình dialog → sinh section đứng cắt qua đế + cổ + cột → tag thép + spot cao độ → đặt lên sheet đích. Output khớp ảnh MC 2-2.
- Kiến trúc mirror BeamDrawing, reuse trực tiếp `ProjectResourceProvider`, `SheetBuilder`, `RebarTagPlacer`, `SheetConfig`, `Point3`. Orchestrator TransactionGroup (T1 view+sheet+viewport commit→regenerate → T2 annotate). Section plane cắt qua tâm móng theo cạnh dài đế; rebar tag phân vùng theo Z (đế / đai cổ / thép chờ). Preset JSON CRUD (`FootingSectionPresetStore`).
- Model thuần trong `RevitAPP.Core/Models/FootingSection/*` (record immutable, JSON round-trip) + `FootingSectionSettingFactory`/`Validator`/`PresetStore` trong Core.Services. UI: `FootingSectionViewModel` + `FootingSectionWindow.xaml` (CommunityToolkit.Mvvm, DynamicResource theme).
- Test: 7 xUnit mới (`FootingSectionPresetStoreTests`, `FootingSectionSettingValidatorTests`) — RevitAPP.Tests 135/135 pass. Build `Debug.R25` compile pass.
- Follow-up (HANDOFF): F5 smoke test trên model thật, verify hướng cắt geometry, implement `FootingDimensionPlacer` (DIM chuỗi 400/1000/350/200, 100/2600/100) — hiện annotator chỉ tag + spot. Chỉ Revit 2025 target.

## 2026-07-09 (bổ sung)

### License: gate nút ribbon + giới hạn số máy

- Fix: 4 nút ribbon vẽ thép (Cột/Dầm/Móng/Tường) trước đây KHÔNG kiểm tra license (chỉ 4 MCP tool có gate) → chưa đăng nhập vẫn vẽ được. Thêm `LicenseService.EnsureValid()` (static, sync) vào đầu `Execute` 4 command; 3 project con (BeamRebarPro, IsolatedFootingRebar, WallRebar) thêm ProjectReference RevitAPP.Licensing.
- Thêm giới hạn số máy/tài khoản (chống chia sẻ): `MachineId.cs` sinh ID ổn định (SHA-256 từ Windows MachineGuid + tên máy), gửi kèm khi verify. Sheet Licenses thêm cột `maxDevices`; tab `Devices` (email | machineId | firstSeen | lastSeen) do Apps Script ghi. Vượt số máy → error `device_limit`. Apps Script dùng LockService chống race; tab Devices phải tạo sẵn.
- Đóng gói lại: `RevitAPP-RebarTools-1.0.2.msi`.

## 2026-07-09

### License Google OAuth + Bộ cài MSI 4 công cụ vẽ thép

- Thêm project `RevitAPP.Licensing` — classlib thuần (net8.0-windows, không đụng Revit API), dùng chung bởi addin RevitAPP và 4 MCP tool. Gồm: Google OAuth PKCE loopback (`GoogleOAuthClient`), verify qua Google Sheet Apps Script (`AppsScriptClient`), cache offline 7 ngày (`LicenseCache`), logic trạng thái (`LicenseService`).
- Ribbon RevitAPP thêm nút "License" — dialog WPF MVVM đăng nhập/đăng xuất Google (`LicenseCommand`, `LicenseViewModel`, `LicenseView`).
- Gate 4 MCP tool (`draw_column_rebar`, `draw_beam_rebar`, `draw_footing_rebar`, `draw_wall_rebar`) — kiểm tra license trước khi vẽ (`LicenseGate`); chưa đăng nhập → từ chối vẽ, trả message hướng dẫn.
- Bộ cài `.msi` per-machine (C:\ProgramData): `install-rebar/InstallerRebar.cs` (WixSharp) + `build/build-rebar-msi.ps1` (gom payload, patch command.json/registry đủ 4 tool trỏ ProgramData). Output: `RevitAPP-RebarTools-1.0.0.msi` (12MB), all-in-one gồm plugin host + 4 tool + addin + LICENSE.txt.
- Test: 8 xUnit test cho logic cache/state (`tests/RevitAPP.Licensing.Tests`) — pass, không chạm mạng thật (mock verifier + inject clock).
- Tài liệu: `docs/license-google-setup.md` (setup Google), `docs/rebar-tools-install-guide.md` (cài cho máy khách).
- Chỉ Revit 2025. Cấp/thu quyền qua Google Sheet `Licenses` (email | expiry | note).

## 2026-06-30

### WallRebar (new add-in)

- Added `src/WallRebar` — add-in vẽ thép tường, tái hiện dialog "Wall Rebar": Cover Setting, Cross Section (3 hàng Ø@spacing, Hook Type trên/dưới combobox 3 lựa chọn, Top/Bottom Offset), Longitudinal Section (Horizontal Offset Start/End, Draw Additional Rebar), Configuration preset.
- Bố trí 2 lưới (mặt A & mặt B) mỗi mặt có thanh dọc + ngang; thép giằng nối 2 mặt khi bật "Draw Additional Rebar". Móc bẻ đồng phẳng (Line) theo HookType để tránh "Can't solve Rebar Shape".
- Pattern theo IsolatedFootingRebar: Host DI nhẹ, ExternalEvent (modeless không gọi Transaction trực tiếp), Orchestrator tự mở/commit Transaction, WallConfigStore preset JSON, Theme.xaml DynamicResource.
- Wired vào RevitAPP: panel "Commands" thêm nút "Ve Thep Tuong" (sau "Ve Mong Don"); ProjectReference + `WallRebar.Host.Start()`.
- Chỉ Revit R25. Build gate: `dotnet build src/WallRebar -c Debug.R25` (0 errors); RevitAPP build với `-p:DeployAddin=false` khi Revit đang mở.

## 2026-06-26

### IsolatedFootingRebar

- Completed Revit 2025 build gate: `dotnet build src/IsolatedFootingRebar -c Debug.R25 -p:DeployAddin=false -p:RunPublish=false`.
- Added xUnit pure-logic coverage for `FootingMath`, default model values, and JSON round-trip serialization.
- Fixed Phase 4/5 review findings: dowel hook direction, exact stirrup layer count, shared WPF radio groups, eccentric pedestal center handling, preset direction restore, and dispatcher-safe callbacks.
- Added tab diagrams using theme brush tokens.

Note: live Revit smoke test is still manual because the MCP bridge was not connected from this session.

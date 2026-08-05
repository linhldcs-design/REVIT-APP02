# System Architecture — Revit Add-In (Nice3point Stack)

> Project structure mặc định khi scaffold `dotnet new revit-addin` với DI mode `container`, WPF enabled, Serilog logging enabled.

## 1. High-Level Diagram

```
┌──────────────────────────────────────────────────────────────────┐
│                       Revit Process                              │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │              Add-In: MyAddIn.dll (loaded by Revit)         │  │
│  │                                                            │  │
│  │  ┌──────────────────┐                                      │  │
│  │  │  Application.cs  │  ← Kế thừa ExternalApplication       │  │
│  │  │  OnStartupAsync()│    1. Setup Serilog                  │  │
│  │  │                  │    2. Build DI container             │  │
│  │  │                  │    3. CreateRibbon()                 │  │
│  │  └────┬─────────────┘                                      │  │
│  │       │                                                    │  │
│  │       ↓ (User click button)                                │  │
│  │  ┌────────────────────────────┐                            │  │
│  │  │  StartupCommand.cs         │  ← ExternalCommand         │  │
│  │  │  Execute()                 │    [Transaction(Manual)]   │  │
│  │  │  ├─ Resolve View qua DI    │                            │  │
│  │  │  └─ view.ShowDialog()      │                            │  │
│  │  └────┬───────────────────────┘                            │  │
│  │       │                                                    │  │
│  │       ↓                                                    │  │
│  │  ┌────────────────────────────────────────────────┐        │  │
│  │  │  Views/WallReportView.xaml                     │        │  │
│  │  │  ├─ Merge Theme.xaml (Dark + Light)            │        │  │
│  │  │  ├─ DataContext = ViewModel (DI inject)        │        │  │
│  │  │  └─ Bind {DynamicResource Brush.X}             │        │  │
│  │  └────┬───────────────────────────────────────────┘        │  │
│  │       │                                                    │  │
│  │       ↓                                                    │  │
│  │  ┌────────────────────────────────────────────────┐        │  │
│  │  │  ViewModels/WallReportViewModel.cs             │        │  │
│  │  │  sealed partial class : ObservableObject       │        │  │
│  │  │  ├─ [ObservableProperty] string _searchText    │        │  │
│  │  │  ├─ [RelayCommand] async Task RunAsync(token)  │        │  │
│  │  │  └─ ctor inject ILogger<T> + IWallService      │        │  │
│  │  └────┬───────────────────────────────────────────┘        │  │
│  │       │                                                    │  │
│  │       ↓                                                    │  │
│  │  ┌────────────────────────────────────────────────┐        │  │
│  │  │  Services/WallService.cs                       │        │  │
│  │  │  ├─ Truy cập Revit API (FilteredElementCollector)│       │  │
│  │  │  └─ Wrap Transaction nếu modify document       │        │  │
│  │  └────────────────────────────────────────────────┘        │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
```

## 2. Folder Structure (Standard)

```
MyAddIn/
├── Application.cs                      ← Entry point (Revit gọi đầu tiên)
├── MyAddIn.csproj                      ← Nice3point.Revit.Sdk + config
├── MyAddIn.addin                       ← Revit manifest XML
├── launchSettings.json                 ← F5 → Revit.exe path
│
├── Commands/                           ← External Commands (button handlers)
│   ├── StartupCommand.cs
│   └── ExportReportCommand.cs
│
├── Configuration/                      ← DI + Logger setup
│   ├── HostingConfiguration.cs         ← services.Add... registration
│   └── LoggerConfiguration.cs          ← Serilog setup
│
├── ViewModels/                         ← MVVM ViewModels (CommunityToolkit.Mvvm)
│   ├── WallReportViewModel.cs
│   └── SettingsViewModel.cs
│
├── Views/                              ← WPF Views (XAML + minimal code-behind)
│   ├── WallReportView.xaml
│   ├── WallReportView.xaml.cs
│   ├── SettingsView.xaml
│   └── SettingsView.xaml.cs
│
├── Models/                             ← POCO / DTO (no Revit dep, test-friendly)
│   ├── WallInfo.cs
│   └── ReportSettings.cs
│
├── Services/                           ← Business logic
│   ├── IWallService.cs                 ← Interface (for DI + testing)
│   ├── WallService.cs                  ← Revit API access
│   └── ReportExporter.cs               ← Xuất Excel/PDF (qua document-skills)
│
├── Helpers/                            ← Multi-version compat shims
│   ├── ElementIdHelper.cs
│   └── UnitConverter.cs
│
└── Resources/
    ├── Icons/
    │   ├── RibbonIcon16.png            ← Small icon (16x16)
    │   └── RibbonIcon32.png            ← Large icon (32x32)
    └── Themes/
        ├── Theme.xaml                  ← Master ResourceDictionary
        ├── ThemeDark.xaml              ← Dark color palette
        ├── ThemeLight.xaml             ← Light color palette
        ├── Typography.xaml             ← Font tokens
        ├── Spacing.xaml                ← Thickness tokens
        ├── Buttons.xaml                ← Button styles
        ├── TextBoxes.xaml              ← TextBox styles
        └── Controls.xaml               ← Card, Separator, Badge
```

## 3. DI Container (mode `container`)

`Configuration/HostingConfiguration.cs`:

```csharp
public static class HostingConfiguration
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Provider => _provider
        ?? throw new InvalidOperationException("DI container not initialized");

    public static void Setup()
    {
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(b => b.AddSerilog());

        // Services (singleton stateless, transient stateful)
        services.AddSingleton<IWallService, WallService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddTransient<IReportExporter, ReportExporter>();

        // ViewModels (transient — new instance per dialog open)
        services.AddTransient<WallReportViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Views (transient — inject ViewModel via constructor)
        services.AddTransient<WallReportView>();
        services.AddTransient<SettingsView>();

        _provider = services.BuildServiceProvider();
    }
}
```

## 4. Multi-version Strategy (Revit 2022–2027)

`.csproj` configs:
```xml
<Configurations>Debug.R22;Debug.R23;Debug.R24;Debug.R25;Debug.R26;Debug.R27</Configurations>
<Configurations>$(Configurations);Release.R22;Release.R23;Release.R24;Release.R25;Release.R26;Release.R27</Configurations>
```

Target framework auto-switch:
- R22–R24 → `net48`
- R25–R27 → `net8.0-windows`

Code branching:
```csharp
#if REVIT2024_OR_GREATER
    long id = elementId.Value;
#else
    int id = elementId.IntegerValue;
#endif
```

Chi tiết: `.claude/skills/revit-addin/references/multi-version-strategy.md`.

## 5. Modal vs Modeless

| Pattern | When | Implementation |
|---|---|---|
| Modal | Dialog ngắn (< 30s), block Revit UI | `view.ShowDialog()` + set `Owner = UiApplication.MainWindowHandle` |
| Modeless | Panel/picker, user vẫn tương tác Revit | `view.Show()` + `ExternalEvent.Create(handler)` cho Revit API call |

## 6. Theme Switch Runtime

`Services/ThemeService.cs` swap MergedDictionary:
```csharp
var uri = theme == AppTheme.Dark
    ? new Uri("pabs://application:,,,/MyAddIn;component/Resources/Themes/ThemeDark.xaml")
    : new Uri("pabs://application:,,,/MyAddIn;component/Resources/Themes/ThemeLight.xaml");
// Replace dict trong Application.Current.Resources.MergedDictionaries
```

Mọi binding `{DynamicResource Brush.X}` tự refresh khi swap.

## 7. Deploy Pipeline

| Stage | Tool | Output |
|---|---|---|
| Build Debug | `dotnet build -c Debug.R<XX>` (F5) | DLL auto-deploy vào `%ProgramData%\Autodesk\Revit\Addins\<version>\` |
| Build Release | `dotnet build -c Release.R<XX>` | DLL trong `bin/Release.R<XX>/` |
| ILRepack | Auto khi `<IsRepackable>true</IsRepackable>` | Single merged DLL |
| Installer | `revit-solution` template (WixSharp) | `.msi` |
| Autodesk Store | `revit-solution` template | Bundle folder + PackageContents.xml |

## 8. Logging Flow

```
Revit event / User click button
        ↓
Command.Execute() → Log.Information("...")
        ↓
ViewModel logic → _logger.LogDebug(...)
        ↓
Service Revit API → _logger.LogInformation(...)
        ↓
Serilog File sink
        ↓
%LocalAppData%\MyAddIn\logs\addin-YYYY-MM-DD.log
```

Setup: `Configuration/LoggerConfiguration.cs` (Nice3point template sinh sẵn).

## 9. Stack Reference

| Component | Version | Source |
|---|---|---|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com |
| Nice3point.Revit.Sdk | latest | NuGet |
| Nice3point.Revit.Toolkit | `$(RevitVersion).*` | NuGet |
| CommunityToolkit.Mvvm | 8.4+ | NuGet |
| Serilog | 4.3+ | NuGet |
| Microsoft.Extensions.DependencyInjection | latest stable | NuGet |
| TUnit (test) | latest | NuGet (Nice3point `revit-test` template) |
| xUnit (pure logic) | latest | NuGet |

## 10. Skill / Tool Map

| Khi cần | Skill |
|---|---|
| Scaffold mới | `/bs:revit-addin` |
| Sửa ViewModel/View | `/bs:revit-wpf-mvvm` |
| Sửa XAML style | `/bs:revit-xaml-styles` |
| Debug F5 / runtime issue | `/bs:revit-debug` |
| Setup / chạy test | `/bs:revit-test` |
| Plan feature mới | `/bs:plan` (Stack-Aware 6-phase) |
| Implement plan | `/bs:cook` (build verify gate) |

## 11. IsolatedFootingRebar Flow

Active target: Revit 2025 (`Debug.R25`).

```
Ribbon button
  -> StartupCommand picks one Structural Foundation
  -> FootingGeometryReader extracts base + optional pedestal
  -> modeless FootingRebarView opens with preset bar and six tabs
  -> ViewModel raises ExternalEvent for document writes
  -> FootingRebarHandler calls FootingRebarOrchestrator
  -> Transaction creates bottom/top/mid mesh, vertical dowels, and horizontal stirrups
```

Pure logic lives in `Models/*` and `Services/FootingMath.cs`, which are linked into `tests/IsolatedFootingRebar.Tests` for out-of-process xUnit tests. Revit API code remains verified by build plus manual Revit smoke testing.

## 12. AI Chat Panel

The ribbon opens a modeless WPF chat panel that can call Anthropic Claude, OpenAI, or Google Gemini. Provider settings and API keys are stored per user with Windows DPAPI. User messages can include up to three resized images from Clipboard, file picker, or drag/drop; provider adapters map the same neutral image block to OpenAI data URLs, Anthropic base64 image sources, and Gemini inline data. Image bytes stay in session history and are not persisted to advanced memory.

```text
Ribbon -> modeless ChatWindow -> provider client
                               -> neutral schema adapter
                               -> ChatToolRegistry (55 tools)
                               -> ExternalEvent -> Revit API / existing engines
```

The provider-independent wire layer (messages, schemas, request builders, and response parsers) lives in `RevitAPP.Core` and has no Revit API dependency. The registry exposes 55 tools: 19 native Revit/Excel automation tools, 21 optional tools backed directly by an installed Revit MCP command assembly, and 15 adapters covering every RevitAPP ribbon button. The native `draw_footing_drawing` and `draw_footing_section` tools take the selected Structural Foundation element ids plus an exact saved preset name, then execute the existing footing plan/section engines directly without opening their configuration dialogs or guessing settings. Their results include the source footing plus created sheet, view, and viewport ids. `arrange_footing_sheet` consumes explicit plan/section pairs, validates matching source footing ids, unique viewport ids, view roles, and target sheet, measures both viewport boxes and labels, and arranges plans above sections from left to right while reserving the title-block region. `draw_and_arrange_footing_sheet` performs both drawing operations and layout in one C# orchestration path, retaining real viewport ids without LLM copying. Native category selection collects and selects the complete requested category in one Revit API context, avoiding bounded MCP filter results. `NativeMcpCommandHost` constructs MCP command objects in a valid Revit API context; each reuses its own `ExternalEvent`, so Chat does not require the MCP TCP server, localhost port 8080, or any external MCP connection. If the optional MCP command assembly is absent, Chat still starts and its other tools remain available. Excel discovery/inspection/table reads run on the Chat worker thread and support `.xls`, `.xlsx`, `.xlsm`, `.xlsb`, and `.csv` with bounded rows, columns, and file size. Model-changing commands require an explicit confirmation dialog; delete/arbitrary-C# commands display a stronger warning.

### Batch beam-longitudinal drawing

`find_beam_longitudinal_presets` is a read-only Chat tool that reads the local saved presets without opening a Revit transaction or changing the model. An empty query returns every named preset in storage order; named searches are case-insensitive, with exact matches before partial-name matches. Chat uses this lookup before `draw_beam_longitudinal_drawing` when the user refers to a saved preset.

`draw_beam_longitudinal_drawing` creates longitudinal drawings for multiple beams and places them on existing sheets. Its inputs are:

- `beamIds`: ordered Structural Framing element ids; when omitted, Chat uses the beams selected in Revit.
- `beamsPerSheet`: maximum number of beams assigned to each target sheet.
- `sheetNumbers`: ordered numbers of existing target sheets, with enough capacity for the batch.
- `presetName`: required exact name of a saved Mặt Cắt Dọc Dầm preset.
- `reverseDirection`: optional batch-wide direction reversal; defaults to `false`.

The tool never creates sheets. Every target must be an existing, non-placeholder sheet with a valid title block and no content other than that title block. Before execution, Chat shows the resolved inputs and requires explicit user confirmation. Beam assignments follow `sheetNumbers` order and `beamsPerSheet`; a failure for one beam does not roll back successful beams on other independent assignments. The result reports success, failure, created view ids, and warnings per beam; a sheet-layout failure marks the affected beam results failed and attempts to remove their generated views.

Layout resolves the drawable sheet region through the shared read-only resolver. It honors title-block parameters `RevitAPP Drawing Left Inset` and `RevitAPP Drawing Right Inset`, otherwise reserving a fallback region for the title-block panel. The resolver never calls `Document.Regenerate` outside a transaction; dependent splitting only regenerates after its transaction has started. If a longitudinal viewport fits that region, the single primary view is placed unchanged. Only when it is too wide, the original viewport is replaced by two `Duplicate As Dependent` views named `ĐOẠN 1/2` and `ĐOẠN 2/2`.

The split station is grid-driven, not the crop midpoint: intersect each `Grid.Curve` with the beam-segment centerline, discard intersections within 1 mm of either endpoint, then choose the station nearest `TotalLength / 2` (a tie selects the station toward the chain start). The selected model point is mapped into crop coordinates with `crop.Transform.Inverse` before setting the dependent crop boxes. Each dependent crop extends up to 525 mm beyond the split grid; a configured line-based Detail Item marks the cut at the preferred 500 mm offset and leaves 25 mm of crop beyond it. Per-view visibility overrides ensure each dependent segment displays only its own cut marker.

Sheet layout preserves the configured view scales. Cross sections use one row when they fit and otherwise two balanced rows. Vertical margins shrink symmetrically from 2 mm to 0.5 mm when that is enough to fit the content. If the current scales still exceed the drawable width or height, layout remains best-effort: it places every generated viewport on the requested sheet, allows overflow, and leaves final manual adjustment to the user instead of rolling back for capacity alone. If no suitable internal grid exists for an oversized longitudinal view, the unsplit primary viewport is retained and placed for manual adjustment.

Example Vietnamese prompt: `Vẽ mặt cắt dọc cho các dầm đang chọn, 3 dầm mỗi sheet, đưa lần lượt vào các sheet S-101, S-102, dùng preset DẦM TẦNG 2 và đảo hướng.`

`ChatMemoryStore` provides bounded advanced local memory. It persists up to 500 versioned entries in `%APPDATA%/RevitAPP/chat-memory.dat`, encrypted with Windows DPAPI for the current user. Memories are scoped by Revit document title unless explicitly saved as a global pinned preference. Successful conversations, tool inputs/results, corrections, and recent created Rebar ids can be reused in later sessions; relevant entries are selected deterministically instead of sending the full archive. API-key-like strings are redacted before persistence. Users manage memory directly through `XEM TRÍ NHỚ`, `GHIM ...`, `QUÊN ...`, and `XÓA TOÀN BỘ TRÍ NHỚ`.

Threading and execution invariants:

- Every functional ribbon command validates the shared license before opening UI, picking elements, or changing Revit state. The License command is the sole activation-safe exception; Chat ribbon adapters apply the same gate.
- Modeless UI code never calls the Revit API directly; all model/view access is marshalled through `ExternalEvent` onto Revit's API context.
- License validation happens before tool dispatch.
- Transaction ownership is explicit: the column tool requires a caller-owned transaction; beam, wall, footing, and beam-drawing engines own their transactions. Read tools do not open transactions.
- The registry must not wrap engine-owned transactions, preventing nested Revit transactions.

Current verification baseline: `RevitAPP.Tests` passes 159/159 tests, and `Release.R22` through `Release.R27` builds succeed.

## 13. Beam Longitudinal Drawing

The beam longitudinal drawing feature is being added to the existing `RevitAPP` assembly as a separate ribbon command. Its Phase 00-02 boundary is:

```text
BeamLongitudinalDrawingCommand
  -> read selected beams and their existing hosted Rebar
  -> reject any selected beam without existing Rebar
  -> modal BeamLongitudinalDrawingWindow/ViewModel
  -> mandatory themed preview and explicit confirmation gate
  -> resource/settings validation and isolated JSON presets

RevitAPP.Core (no Revit API dependency)
  -> BeamChainBuilder
  -> SectionStationPlanner
  -> RebarFingerprintComparer
  -> LongitudinalDimensionPlanner
```

The Core layer owns deterministic chain validation, station deduplication, normalized reinforcement fingerprints,
upper/lower dimension witness plans, drawing settings validation, preset serialization, and preview projection. The
`RevitAPP` layer owns read-only Revit element/resource discovery and WPF presentation in Phase 02; transactions,
view/annotation creation, and sheet placement remain later-phase responsibilities.

Before any Revit transaction starts, Phase 02 projects `BeamChainModel + SectionStation` into a pure
`BeamChainPreviewModel` rendered by a themed WPF preview control. The preview is a required confirmation boundary:
invalid topology, a changed selection/direction/tolerance, or an unconfirmed snapshot keeps Generate disabled. The
workflow is intentionally read-only and never creates, modifies, or deletes Rebar; it only documents beams whose
reinforcement already exists in the active model.

Current status: Phase 00-02 complete; 196/196 `RevitAPP.Tests` pass and `Debug.R25` builds successfully. The command
provides selection validation, settings, presets, and mandatory preview confirmation, but still does not create any
Revit view or modify the model. View generation, annotation, sheet output, runtime smoke testing, deployment, and
release hand-off remain pending in Phase 03-06; the feature is not released.

## 14. Model From CAD (v1.8.0 Release Candidate)

`ModelFromCadCommand` opens one modal options window before any AutoCAD selection begins. The user chooses
`Create Grid` or `Create Column`, then starts acquisition with the `Select From CAD` button inside that tab.
Both tabs write the selection result to the same in-window Data state, so the user can switch tabs and review the
same scan without repeating it. Either tab can also reselect CAD data while keeping the window open.

```text
Ribbon: Model From CAD
  -> empty ModelFromCadWindow (options first)
  -> choose Create Grid or Create Column
  -> Select From CAD in the active tab
  -> AutoCAD COM selection + source anchor
  -> shared CadStructureAnalysis Data
       |-> Grid table + 2D preview
       `-> Column table + 2D/3D preview
  -> Create
  -> target anchor in Revit
  -> Grid or Structural Column transaction
```

Responsibilities are separated as follows:

- `AutoCadModelSelectionService` reads supported AutoCAD entities and nested block geometry without modifying the DWG.
- `CadStructureAnalyzer` in `RevitAPP.Core` performs unit normalization and deterministic Grid/rectangle analysis without a Revit API dependency.
- `ModelFromCadViewModel` owns the shared Data state, active tab, settings, validation, selection state and preview projection.
- `ModelFromCadWindow` renders Grid/Column review surfaces. The 2D controls occupy a dedicated toolbar row; the Column 3D viewport supports wheel zoom and left-drag orbit, including drag gestures started over the blank host area.
- `CadGridDirectLineBuilder` and `CadColumnCreationService` own the Revit creation paths after the user picks the target anchor.

Verification baseline for this release candidate: `RevitAPP.Tests` passes **357/357**. `Release.R22` through
`Release.R27` build successfully with deployment, Revit launch and publish disabled. Local runtime iteration has
verified the options-first window, responsive CAD scanning, the dedicated 2D toolbar layout, and 3D wheel zoom and
left-drag orbit fixes. A documented end-to-end smoke of element creation, duplicate handling and Undo is still
required before declaring the feature production-ready.

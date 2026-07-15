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

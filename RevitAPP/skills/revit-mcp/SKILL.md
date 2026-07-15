---
name: revit-mcp
description: Automate Autodesk Revit through the local mcp-server-for-revit socket. Use when the user asks Codex to inspect or modify the active Revit model/view, replace TextNote contents, renumber pile labels such as P01-P30, find untagged Structural Foundation piles, create IndependentTag annotations, or execute Revit API C# code through MCP.
---

# Revit MCP

## Overview

Use the installed Revit MCP bridge on `localhost:8080` to send JSON-RPC commands into the running Revit session. Prefer direct MCP socket calls for live model operations because the Revit MCP tools may not be loaded into the current Codex tool list until a new session starts.

## Quick Start

Check Revit MCP is reachable before changing the model:

```powershell
powershell -ExecutionPolicy Bypass -File .\RevitAPP\skills\revit-mcp\scripts\invoke-revit-mcp.ps1 -Method get_current_view_info
```

Execute C# code with `send_code_to_revit` using the helper script and `-CodeFile`. Keep temporary code files in the workspace and delete them after use.

Important runtime details:

- Use `Document`, not `doc`, inside `send_code_to_revit`.
- Do not put `using ...;` directives at the top of the submitted code. Use fully qualified names such as `Autodesk.Revit.DB.TextNote`.
- Do not start a new `Transaction` unless testing shows this command host requires it. In this setup, `send_code_to_revit` already runs in a writable context; creating a transaction caused `Starting a new transaction is not permitted`.
- Revit 2025 plugin is installed under `C:\ProgramData\Autodesk\Revit\Addins\2025\revit_mcp_plugin`.
- The socket service log is under `...\revit_mcp_plugin\Logs\mcp_YYYYMMDD.log`.

## Common Tasks

### Replace Text In Current View

Use `FilteredElementCollector(Document, Document.ActiveView.Id)` and `TextNote.Text`.

```csharp
var activeView = Document.ActiveView;
var textNotes = new Autodesk.Revit.DB.FilteredElementCollector(Document, activeView.Id)
    .OfClass(typeof(Autodesk.Revit.DB.TextNote))
    .Cast<Autodesk.Revit.DB.TextNote>()
    .ToList();

var changed = 0;
foreach (var note in textNotes)
{
    var original = note.Text;
    if (string.IsNullOrEmpty(original) || !original.Contains("200")) continue;
    note.Text = original.Replace("200", "250");
    changed++;
}

return $"Updated {changed} TextNote(s) in view '{activeView.Name}'.";
```

### Renumber Pile Labels

For pile plans, `Pxx` may be stored on `Structural Foundations` parameters, not as text notes. Query:

- Category: `Autodesk.Revit.DB.BuiltInCategory.OST_StructuralFoundation`
- Typical writable parameters: `Mark`, sometimes `STT`
- Location: `LocationPoint.Point`

Order piles by rows using Y descending, then X ascending. A tolerance around `0.8` internal feet worked for grouping rows on the pile-location plan. For the observed layout, the desired sequence was:

- Top row: `P01` to `P12`
- Middle row: `P13` to `P18`
- Bottom row: `P19` to `P30`

Set every string parameter whose value matches `^P\s*\d+$` to the new label, including both `Mark` and `STT` when present.

### Find And Create Missing Pile Tags

Differentiate two cases:

- Missing parameter label: a pile has no `Mark`/`STT` matching `Pxx`.
- Missing annotation tag: the pile has `Mark`, but no `IndependentTag` in the active view points to it.

To inspect tags:

```csharp
var tags = new Autodesk.Revit.DB.FilteredElementCollector(Document, Document.ActiveView.Id)
    .OfClass(typeof(Autodesk.Revit.DB.IndependentTag))
    .Cast<Autodesk.Revit.DB.IndependentTag>()
    .ToList();

foreach (var tag in tags)
{
    var ids = tag.GetTaggedLocalElementIds();
}
```

To create missing structural foundation tags:

```csharp
var tag = Autodesk.Revit.DB.IndependentTag.Create(
    Document,
    activeView.Id,
    new Autodesk.Revit.DB.Reference(pileElement),
    false,
    Autodesk.Revit.DB.TagMode.TM_ADDBY_CATEGORY,
    Autodesk.Revit.DB.TagOrientation.Horizontal,
    tagHeadPoint);
```

Use an existing `Structural Foundation Tags` tag type from the view when available, then call `tag.ChangeTypeId(existingTagTypeId)` if needed. Place the tag head slightly below the pile point for the current drafting style; `pile.Point + new XYZ(0, -1.15, 0)` worked acceptably on the observed plan.

### Copy Dimension Pattern From Left View To Right View

For sheets with two similar foundation detail viewports, such as:

- Left: `CHI TIẾT MÓNG M1 LỚP DƯỚI`
- Right: `CHI TIẾT MÓNG M1 LỚP TRÊN`

Use the sheet viewports to identify left/right by `Viewport.GetBoxCenter().X`. Inspect dimensions in the left view:

```csharp
var dims = new Autodesk.Revit.DB.FilteredElementCollector(Document, leftView.Id)
    .OfClass(typeof(Autodesk.Revit.DB.Dimension))
    .Cast<Autodesk.Revit.DB.Dimension>()
    .OrderBy(d => d.Id.IntegerValue)
    .ToList();
```

`ElementTransformUtils.CopyElements(leftView, dimIds, rightView, Transform.Identity, options)` can report success but still leave zero dimensions in the destination view. For this setup, create new dimensions in the right view from the left dimension references instead.

Important details:

- Reuse the source `Reference` directly for model elements that are visible in both views.
- For view-specific `Detail Items`, replace the left element `UniqueId` in the stable reference with the matching right-view detail item `UniqueId`.
- Match detail items by name and bounding box center. In the observed M1 sheet, `2D-Top` and the left `1-25` marker were referenced by the left dimensions.
- Create the new dimension line from the source dimension line origin and direction:

```csharp
var srcLine = sourceDim.Curve as Autodesk.Revit.DB.Line;
var origin = srcLine.Origin;
var dir = srcLine.Direction.Normalize();
var line = Autodesk.Revit.DB.Line.CreateBound(origin, origin + dir.Multiply(50.0));
var newDim = Document.Create.NewDimension(rightView, line, refArray, sourceDim.DimensionType);
```

On layer/detail views, Revit may recalculate some chain values differently after reference remapping, especially around circular pile/edge references. If the user wants the right view to match the left view annotation pattern exactly, set `DimensionSegment.ValueOverride` or `Dimension.ValueOverride` from the left dimension text after creating the dimensions.

Observed M1 pattern to mirror:

- `100|250|300|550|400|550|300|250|100`
- `2600`
- `100|250|300|250|300`
- `1100`
- `100|1100|400|1100|100`
- `2600`

## Helper Script

Use `scripts/invoke-revit-mcp.ps1` for repeatable JSON-RPC calls. It supports `-Method`, optional `-ParamsJson`, and optional `-CodeFile` for `send_code_to_revit`.

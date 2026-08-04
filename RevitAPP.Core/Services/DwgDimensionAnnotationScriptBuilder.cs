using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RevitAPP.Core.Services;

public sealed record DwgDimensionAnnotationTarget(
    string Handle,
    string SourceStyleKey,
    double LinearScaleFactor);

public sealed record DwgSheetDimensionAnnotationPlan(
    string SheetNumber,
    int ReferenceScale,
    IReadOnlyList<DwgDimensionAnnotationTarget> Dimensions);

/// <summary>
/// Builds one native AutoCAD script for all dimension annotation updates. The script uses
/// only built-in commands and AutoLISP functions; it does not load or install a CAD plug-in.
/// </summary>
public static class DwgDimensionAnnotationScriptBuilder
{
    private static readonly Regex HandleRegex = new(
        "^[0-9A-F]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Build(
        IEnumerable<DwgSheetDimensionAnnotationPlan> plans,
        string completionMarker,
        double dimensionSizeScaleFactor)
    {
        if (string.IsNullOrWhiteSpace(completionMarker))
            throw new ArgumentException("A completion marker is required.", nameof(completionMarker));
        if (completionMarker.Any(character => character is '"' or '\r' or '\n'))
            throw new ArgumentException("Completion marker contains an unsafe character.", nameof(completionMarker));
        if (double.IsNaN(dimensionSizeScaleFactor)
            || double.IsInfinity(dimensionSizeScaleFactor)
            || dimensionSizeScaleFactor <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(dimensionSizeScaleFactor),
                "Dimension size scale factor must be finite and positive.");

        var sheets = plans.ToArray();
        if (sheets.Any(sheet => sheet.ReferenceScale <= 0))
            throw new ArgumentOutOfRangeException(nameof(plans), "Annotation scale must be positive.");

        var dimensions = sheets.SelectMany(sheet => sheet.Dimensions).ToArray();
        foreach (var dimension in dimensions)
        {
            if (!HandleRegex.IsMatch(dimension.Handle))
                throw new ArgumentException($"Invalid AutoCAD handle '{dimension.Handle}'.", nameof(plans));
            if (string.IsNullOrWhiteSpace(dimension.SourceStyleKey))
                throw new ArgumentException("A dimension style key is required.", nameof(plans));
            if (double.IsNaN(dimension.LinearScaleFactor) || double.IsInfinity(dimension.LinearScaleFactor))
                throw new ArgumentOutOfRangeException(nameof(plans), "DIMLFAC must be finite.");
        }

        var duplicateHandle = dimensions
            .GroupBy(dimension => dimension.Handle, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateHandle is not null)
            throw new ArgumentException($"Duplicate AutoCAD handle '{duplicateHandle.Key}'.", nameof(plans));

        var styles = dimensions
            .GroupBy(dimension => dimension.SourceStyleKey, StringComparer.Ordinal)
            .Select((group, index) => new
            {
                SourceStyleKey = group.Key,
                RepresentativeHandle = group.First().Handle,
                DimStyleVariable = $"ra_ds_{index:0000}",
                TextStyleVariable = $"ra_ts_{index:0000}",
                AnnotativeStyleBaseName = $"RA_ANNO_{index:0000}",
                AnnotativeTextStyleBaseName = $"RA_DIMTXT_{index:0000}"
            })
            .ToArray();
        var styleNames = styles.ToDictionary(
            item => item.SourceStyleKey,
            item => item.DimStyleVariable,
            StringComparer.Ordinal);

        var script = new StringBuilder();
        script.AppendLine("(setvar \"FILEDIA\" 0)");
        script.AppendLine("(setvar \"CMDECHO\" 0)");
        script.AppendLine("(setvar \"ANNOALLVISIBLE\" 1)");
        script.AppendLine("(setvar \"USERS5\" \"\")");
        script.AppendLine("(setvar \"USERR1\" 0.0)");
        script.AppendLine("(setvar \"USERR2\" 0.0)");
        script.AppendLine(
            $"(setq ra_dim_size_factor {dimensionSizeScaleFactor.ToString("R", CultureInfo.InvariantCulture)})");
        script.AppendLine("(defun ra_set_scale (n / s r) (setq s (strcat \"1:\" (itoa n))) (setq r (vl-catch-all-apply 'setvar (list \"CANNOSCALE\" s))) (if (vl-catch-all-error-p r) (progn (command \"_.-SCALELISTEDIT\" \"_Add\" s s \"_Exit\") (setvar \"CANNOSCALE\" s))) s)");
        script.AppendLine("(defun ra_unique_name (table base / candidate suffix) (setq candidate base suffix 0) (while (tblsearch table candidate) (setq suffix (1+ suffix) candidate (strcat base \"_\" (itoa suffix)))) candidate)");
        script.AppendLine("(defun ra_make_text_style (n) (command \"_.-STYLE\" n \"Arial Narrow\" \"_Annotative\" \"_Yes\" \"_No\" \"2.5\" \"0.8\" \"0\" \"_No\" \"_No\") n)");
        script.AppendLine("(defun ra_app_name (a / n) (setq n (car a)) (strcase (if (= (type n) 'STR) n (vl-symbol-name n))))");
        script.AppendLine("(defun ra_scale_dim_items (items factor / out pending item) (setq out '() pending nil) (foreach item items (if (and pending (listp item) (= (car item) 1040)) (progn (setq item (cons 1040 (* (cdr item) factor))) (setq pending nil))) (setq out (cons item out)) (if (and (listp item) (= (car item) 1070) (= (cdr item) 41)) (setq pending T))) (reverse out))");
        script.AppendLine("(defun ra_scale_dim_apps (apps factor) (mapcar '(lambda (a) (if (= (ra_app_name a) \"ACAD\") (cons (car a) (ra_scale_dim_items (cdr a) factor)) a)) apps))");
        script.AppendLine("(defun ra_scale_dim_data (d factor / x) (setq x (assoc -3 d)) (if x (subst (cons -3 (ra_scale_dim_apps (cdr x) factor)) x d) d))");
        script.AppendLine("(defun ra_scale_dim_size (e factor) (if (and e (/= factor 1.0)) (entmod (ra_scale_dim_data (entget e '(\"ACAD\")) factor))) T)");
        // entupd forces an immediate graphical regeneration for every dimension and becomes
        // prohibitively slow on large print sets. entmod updates the database record; the
        // batched ANNOUPDATE below performs the required graphical/context refresh once per sheet.
        script.AppendLine("(defun ra_set_style (e s / d p) (if e (progn (setq d (entget e)) (setq p (assoc 3 d)) (if p (progn (entmod (subst (cons 3 s) p d)) T)))))");
        script.AppendLine("(setq ra_selected 0 ra_annotative 0)");

        // Restore the source style by its name read inside AutoCAD, then clone it as an
        // annotative style. Source names never enter the script, so Unicode names are safe.
        foreach (var style in styles)
        {
            script.AppendLine($"(setq {style.TextStyleVariable} (ra_unique_name \"STYLE\" \"{style.AnnotativeTextStyleBaseName}\"))");
            script.AppendLine($"(setq {style.DimStyleVariable} (ra_unique_name \"DIMSTYLE\" \"{style.AnnotativeStyleBaseName}\"))");
            script.AppendLine($"(setq ra_e (handent \"{style.RepresentativeHandle}\"))");
            script.AppendLine($"(if ra_e (progn (setq ra_source_style (cdr (assoc 3 (entget ra_e)))) (setq ra_text_style (ra_make_text_style {style.TextStyleVariable})) (command \"_.-DIMSTYLE\" \"_Restore\" ra_source_style) (setvar \"DIMTXSTY\" ra_text_style) (command \"_.-DIMSTYLE\" \"_Annotative\" \"_Yes\" {style.DimStyleVariable}) (command)))");
        }

        // ANNOUPDATE can regenerate a large drawing. Group sheets sharing the same reference
        // denominator so the expensive native commands run once per scale, not once per sheet.
        foreach (var scaleGroup in sheets
                     .Where(sheet => sheet.Dimensions.Count > 0)
                     .GroupBy(sheet => sheet.ReferenceScale)
                     .OrderBy(group => group.Key))
        {
            script.AppendLine($"(setq ra_scale (ra_set_scale {scaleGroup.Key.ToString(CultureInfo.InvariantCulture)}))");
            script.AppendLine("(setq ra_ss (ssadd))");
            foreach (var dimension in scaleGroup.SelectMany(sheet => sheet.Dimensions))
            {
                var styleName = styleNames[dimension.SourceStyleKey];
                script.AppendLine($"(setq ra_e (handent \"{dimension.Handle}\"))");
                script.AppendLine($"(if (and ra_e (ra_set_style ra_e {styleName})) (progn (ssadd ra_e ra_ss) (setq ra_selected (1+ ra_selected))))");
            }
            script.AppendLine("(if (> (sslength ra_ss) 0) (progn (command \"_.ANNOUPDATE\" ra_ss \"\") (command \"_.-OBJECTSCALE\" ra_ss \"\" \"_Add\" ra_scale \"\") (command) (setq ra_i 0) (repeat (sslength ra_ss) (setq ra_e (ssname ra_ss ra_i)) (if (assoc -3 (entget ra_e '(\"AcadAnnotative\"))) (setq ra_annotative (1+ ra_annotative))) (setq ra_i (1+ ra_i))))))");
        }

        // Revit writes per-dimension DIMASZ overrides in inches. Convert those overrides only
        // after ANNOUPDATE; changing arrow geometry beforehand makes AutoCAD recompute the full
        // 1,500+ dimension set during annotation conversion and can exceed the worker timeout.
        foreach (var dimension in dimensions)
        {
            script.AppendLine($"(setq ra_e (handent \"{dimension.Handle}\"))");
            script.AppendLine("(ra_scale_dim_size ra_e ra_dim_size_factor)");
        }

        script.AppendLine("(setvar \"USERR1\" ra_selected)");
        script.AppendLine("(setvar \"USERR2\" ra_annotative)");
        script.AppendLine($"(setvar \"USERS5\" \"{completionMarker}\")");
        script.AppendLine("(princ)");
        return script.ToString();
    }

}

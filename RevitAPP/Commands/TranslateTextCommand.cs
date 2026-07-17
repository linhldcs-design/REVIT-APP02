using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Nice3point.Revit.Toolkit.External;
using RevitAPP.Services;
using RevitAPP.Views;

namespace RevitAPP.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class TranslateTextCommand : ExternalCommand
    {
        public override void Execute()
        {
            if (!LicenseCommandGate.Ensure("Dịch Text")) return;
            var uiDocument = Application.ActiveUIDocument;
            var document = uiDocument.Document;

            var selectedTextNotes = GetSelectedOrPickedTextNotes(uiDocument, document);
            if (selectedTextNotes == null)
            {
                return;
            }

            if (selectedTextNotes.Count == 0)
            {
                TaskDialog.Show("RevitAI", "Khong co TextNote hop le de dich.");
                return;
            }

            var optionsWindow = new TranslationOptionsWindow(Application.MainWindowHandle, selectedTextNotes.Count,
                TranslationSessionSettings.CreateOptions());
            if (optionsWindow.ShowDialog() != true)
            {
                return;
            }

            TranslationSessionSettings.Save(optionsWindow.Options);

            var service = new GeminiTranslationService();
            var translatedTexts = new Dictionary<ElementId, string>();

            try
            {
                var originalTexts = selectedTextNotes.Select(textNote => textNote.Text.Trim()).ToList();
                var batchTranslations = service.TranslateBatchAsync(originalTexts, optionsWindow.Options).GetAwaiter().GetResult();

                for (var index = 0; index < selectedTextNotes.Count; index++)
                {
                    var textNote = selectedTextNotes[index];
                    var originalText = originalTexts[index];
                    var translatedText = batchTranslations[index];
                    translatedText = GeminiTranslationService.ApplyCase(translatedText, optionsWindow.Options.CaseMode);

                    translatedTexts[textNote.Id] = optionsWindow.Options.AppendToOriginal
                        ? $"{originalText}/{translatedText}"
                        : translatedText;
                }
            }
            catch (Exception exception)
            {
                TaskDialog.Show("RevitAI", $"Khong the dich text bang Gemini AI:\n{exception.Message}");
                return;
            }

            using var transaction = new Transaction(document, "Translate text notes");
            transaction.Start();

            foreach (var translatedText in translatedTexts)
            {
                if (document.GetElement(translatedText.Key) is TextNote textNote)
                {
                    textNote.Text = translatedText.Value;
                }
            }

            transaction.Commit();

            TaskDialog.Show("RevitAI", $"Da dich {translatedTexts.Count} TextNote.");
        }

        private static List<TextNote>? GetSelectedOrPickedTextNotes(UIDocument uiDocument, Document document)
        {
            var selectedTextNotes = uiDocument.Selection.GetElementIds()
                .Select(id => document.GetElement(id))
                .OfType<TextNote>()
                .Where(textNote => !string.IsNullOrWhiteSpace(textNote.Text))
                .ToList();

            if (selectedTextNotes.Count > 0)
            {
                return selectedTextNotes;
            }

            try
            {
                var references = uiDocument.Selection.PickObjects(ObjectType.Element, new TextNoteSelectionFilter(),
                    "Chon mot hoac nhieu TextNote can dich, bam Finish de tiep tuc.");

                return references
                    .Select(reference => document.GetElement(reference.ElementId))
                    .OfType<TextNote>()
                    .Where(textNote => !string.IsNullOrWhiteSpace(textNote.Text))
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private class TextNoteSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                return element is TextNote textNote && !string.IsNullOrWhiteSpace(textNote.Text);
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RevitAPP.Models;
using Control = System.Windows.Controls.Control;
using Grid = System.Windows.Controls.Grid;

namespace RevitAPP.Views
{
    public class TranslationOptionsWindow : Window
    {
        private readonly PasswordBox _apiKeyBox;
        private readonly TextBox _sourceLanguageBox;
        private readonly TextBox _targetLanguageBox;
        private readonly TextBox _modelBox;
        private readonly RadioButton _preserveCaseButton;
        private readonly RadioButton _upperCaseButton;
        private readonly RadioButton _lowerCaseButton;
        private readonly CheckBox _appendToOriginalBox;

        public TranslationOptionsWindow(IntPtr ownerHandle, int selectedTextCount, TranslationOptions initialOptions)
        {
            Title = "Dich text bang Gemini AI";
            Width = 430;
            MinWidth = 430;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (ownerHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ownerHandle;
            }

            var root = new Grid
            {
                Margin = new Thickness(18)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddText(root, $"Dang chon {selectedTextCount} TextNote.", 0, FontWeights.SemiBold);

            _apiKeyBox = new PasswordBox
            {
                Password = initialOptions.ApiKey
            };
            AddLabeledControl(root, "Gemini API key", _apiKeyBox, 1);

            _sourceLanguageBox = new TextBox
            {
                Text = initialOptions.SourceLanguage
            };
            AddLabeledControl(root, "Ngon ngu goc", _sourceLanguageBox, 2);

            _targetLanguageBox = new TextBox
            {
                Text = initialOptions.TargetLanguage
            };
            AddLabeledControl(root, "Ngon ngu dich", _targetLanguageBox, 3);

            _modelBox = new TextBox
            {
                Text = initialOptions.Model
            };
            AddLabeledControl(root, "Gemini model", _modelBox, 4);

            var casePanel = new StackPanel
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            casePanel.Children.Add(new TextBlock
            {
                Text = "Kieu chu ban dich",
                Margin = new Thickness(0, 0, 0, 6)
            });

            _upperCaseButton = new RadioButton
            {
                Content = "CHU HOA",
                IsChecked = initialOptions.CaseMode == TranslationCase.Upper,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _lowerCaseButton = new RadioButton
            {
                Content = "chu thuong",
                IsChecked = initialOptions.CaseMode == TranslationCase.Lower,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _preserveCaseButton = new RadioButton
            {
                Content = "Giu nguyen ket qua AI",
                IsChecked = initialOptions.CaseMode == TranslationCase.Preserve,
                Margin = new Thickness(0, 2, 0, 2)
            };
            casePanel.Children.Add(_upperCaseButton);
            casePanel.Children.Add(_lowerCaseButton);
            casePanel.Children.Add(_preserveCaseButton);
            Grid.SetRow(casePanel, 5);
            root.Children.Add(casePanel);

            _appendToOriginalBox = new CheckBox
            {
                Content = "Ghep dang: text goc / ban dich",
                IsChecked = initialOptions.AppendToOriginal,
                Margin = new Thickness(0, 14, 0, 4)
            };
            Grid.SetRow(_appendToOriginalBox, 6);
            root.Children.Add(_appendToOriginalBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var okButton = new Button
            {
                Content = "Dich",
                Width = 92,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (_, _) => Accept();

            var cancelButton = new Button
            {
                Content = "Huy",
                Width = 92,
                Height = 30,
                IsCancel = true
            };

            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            Grid.SetRow(buttons, 7);
            root.Children.Add(buttons);

            Content = root;
        }

        public TranslationOptions Options { get; private set; } = new();

        private static void AddText(Grid root, string text, int row, FontWeight fontWeight)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontWeight = fontWeight,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(textBlock, row);
            root.Children.Add(textBlock);
        }

        private static void AddLabeledControl(Grid root, string label, Control control, int row)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0)
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4)
            });

            control.Height = 28;
            panel.Children.Add(control);
            Grid.SetRow(panel, row);
            root.Children.Add(panel);
        }

        private void Accept()
        {
            var apiKey = _apiKeyBox.Password.Trim();
            var sourceLanguage = _sourceLanguageBox.Text.Trim();
            var targetLanguage = _targetLanguageBox.Text.Trim();
            var model = _modelBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show(this, "Vui long nhap Gemini API key hoac dat bien moi truong GEMINI_API_KEY.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(targetLanguage))
            {
                MessageBox.Show(this, "Vui long nhap ngon ngu dich.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Options = new TranslationOptions
            {
                ApiKey = apiKey,
                SourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage) ? "Auto detect" : sourceLanguage,
                TargetLanguage = targetLanguage,
                Model = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model,
                CaseMode = GetCaseMode(),
                AppendToOriginal = _appendToOriginalBox.IsChecked == true
            };

            DialogResult = true;
        }

        private TranslationCase GetCaseMode()
        {
            if (_upperCaseButton.IsChecked == true)
            {
                return TranslationCase.Upper;
            }

            if (_lowerCaseButton.IsChecked == true)
            {
                return TranslationCase.Lower;
            }

            return TranslationCase.Preserve;
        }
    }
}

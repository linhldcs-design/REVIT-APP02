using System.Windows;
using RevitAPP.Chat.ViewModels;

namespace RevitAPP.Chat.Views;

/// <summary>
///     Dialog Settings. PasswordBox không hỗ trợ binding an toàn nên key được đẩy qua/lấy ra qua code-behind
///     (giá trị nằm trong ViewModel, PasswordBox chỉ là ô nhập tạm).
/// </summary>
public partial class ChatSettingsWindow : Window
{
    private readonly ChatSettingsViewModel _viewModel;

    public ChatSettingsWindow(ChatSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        AnthropicKeyBox.Password = viewModel.AnthropicKey;
        OpenAiKeyBox.Password = viewModel.OpenAiKey;
        GeminiKeyBox.Password = viewModel.GeminiKey;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        PushKeysToViewModel();
        _viewModel.SaveCommand.Execute(null);

        if (_viewModel.Saved)
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnTestClick(object sender, RoutedEventArgs e)
    {
        PushKeysToViewModel();
        _viewModel.TestConnectionCommand.Execute(null);
    }

    private void PushKeysToViewModel()
    {
        _viewModel.AnthropicKey = AnthropicKeyBox.Password;
        _viewModel.OpenAiKey = OpenAiKeyBox.Password;
        _viewModel.GeminiKey = GeminiKeyBox.Password;
    }
}

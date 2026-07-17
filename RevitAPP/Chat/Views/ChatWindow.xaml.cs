using System.Windows;
using RevitAPP.Chat.ViewModels;

namespace RevitAPP.Chat.Views;

public partial class ChatWindow : Window
{
    public ChatWindow(ChatViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

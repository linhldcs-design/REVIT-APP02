using System.Windows;
using System.Windows.Input;

namespace RevitAPP.Chat.Views;

public static class ImageDropBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(ImageDropBehavior), new PropertyMetadata(null, OnCommandChanged));

    public static void SetCommand(DependencyObject element, ICommand value) => element.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    private static void OnCommandChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not UIElement element) return;
        element.AllowDrop = args.NewValue is ICommand;
        element.PreviewDragOver -= OnPreviewDragOver;
        element.Drop -= OnDrop;
        if (args.NewValue is ICommand)
        {
            element.PreviewDragOver += OnPreviewDragOver;
            element.Drop += OnDrop;
        }
    }

    private static void OnPreviewDragOver(object sender, DragEventArgs args)
    {
        args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs args)
    {
        if (sender is not DependencyObject element || GetCommand(element) is not { } command) return;
        var files = args.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        if (command.CanExecute(files)) command.Execute(files);
        args.Handled = true;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RevitAPP.Chat.Models;

namespace RevitAPP.Chat.Services;

public sealed class ChatImageService
{
    public const int MaxImages = 3;
    private const int MaxDimension = 1600;
    private const int MaxEncodedBytes = 3 * 1024 * 1024;

    public IReadOnlyList<string> PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh gửi cho Chat AI",
            Filter = "Ảnh (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            Multiselect = true
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public ChatImageAttachment FromClipboard()
    {
        if (!Clipboard.ContainsImage()) throw new InvalidOperationException("Clipboard không có ảnh.");
        var source = Clipboard.GetImage() ?? throw new InvalidOperationException("Không đọc được ảnh từ Clipboard.");
        return Encode(source, $"clipboard-{DateTime.Now:HHmmss}.png");
    }

    public ChatImageAttachment FromFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp")
            throw new InvalidOperationException($"Định dạng ảnh không hỗ trợ: {extension}");

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return Encode(decoder.Frames[0], Path.GetFileName(path));
    }

    private static ChatImageAttachment Encode(BitmapSource original, string fileName)
    {
        BitmapSource source = original;
        var scale = Math.Min(1d, Math.Min((double)MaxDimension / original.PixelWidth,
            (double)MaxDimension / original.PixelHeight));
        if (scale < 1d)
            source = new TransformedBitmap(original, new ScaleTransform(scale, scale));

        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        if (output.Length > MaxEncodedBytes)
            throw new InvalidOperationException("Ảnh sau khi thu nhỏ vẫn vượt quá 3 MB.");

        var preview = BitmapFrame.Create(source);
        preview.Freeze();
        return new ChatImageAttachment(fileName, "image/png", Convert.ToBase64String(output.ToArray()),
            preview, source.PixelWidth, source.PixelHeight);
    }
}

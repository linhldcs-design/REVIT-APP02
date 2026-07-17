using System.Windows.Media;

namespace RevitAPP.Chat.Models;

public sealed record ChatImageAttachment(
    string FileName,
    string MimeType,
    string Base64Data,
    ImageSource Preview,
    int PixelWidth,
    int PixelHeight);

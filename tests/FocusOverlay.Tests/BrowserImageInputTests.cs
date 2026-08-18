using System.IO;
using System.Windows;
using FocusOverlay.App;

namespace FocusOverlay.Tests;

public class BrowserImageInputTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    [Fact]
    public async Task HtmlImageDataUriIsImportedAsValidatedLocalImage()
    {
        var data = new DataObject();
        data.SetData(
            DataFormats.Html,
            $"<html><body><img src=\"data:image/png;base64,{OnePixelPng}\"></body></html>");

        Assert.True(ImageInput.CanImportDrop(data));

        var path = await ImageInput.ImportDropAsync(data);
        try
        {
            Assert.True(File.Exists(path));
            Assert.EndsWith(".png", path, StringComparison.OrdinalIgnoreCase);
            Assert.True(ImageInput.TryDecode(path, out var image));
            Assert.NotNull(image);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void PlainWebPageUrlIsNotPresentedAsImageDrop()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "https://example.com/posts/42");

        Assert.False(ImageInput.CanImportDrop(data));
    }

    [Fact]
    public void BrowserHtmlImageSourceIsRecognizedWithoutDownloadingDuringDragOver()
    {
        var data = new DataObject();
        data.SetData(
            DataFormats.Html,
            "Version:1.0\r\nSourceURL:https://example.com/post\r\n<html><img src='/media/cat.jpg'></html>");

        Assert.True(ImageInput.CanImportDrop(data));
    }
}

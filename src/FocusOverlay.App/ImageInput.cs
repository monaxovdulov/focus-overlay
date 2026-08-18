using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FocusOverlay.App;

internal static class ImageInput
{
    private const int MaxImageBytes = 25 * 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".webp"
    };

    private static readonly Regex ImageSourcePattern = new(
        "<img\\b[^>]*\\b(?:src|data-src)\\s*=\\s*(?:\"(?<double>[^\"]+)\"|'(?<single>[^']+)'|(?<bare>[^\\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HtmlSourceUrlPattern = new(
        "(?:^|\\r?\\n)SourceURL:(?<url>[^\\r\\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HttpClient RemoteClient = CreateRemoteClient();

    public static bool IsSupportedFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        SupportedExtensions.Contains(Path.GetExtension(path));

    public static bool TryGetSingleSupportedFile(IEnumerable<string> files, out string path)
    {
        path = string.Empty;
        var candidates = files.Take(2).ToArray();
        if (candidates.Length != 1 || !IsSupportedFile(candidates[0]))
        {
            return false;
        }

        path = Path.GetFullPath(candidates[0]);
        return true;
    }

    public static bool TryGetSingleSupportedFile(System.Windows.IDataObject data, out string path)
    {
        path = string.Empty;
        return data.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
               data.GetData(System.Windows.DataFormats.FileDrop) is string[] files &&
               TryGetSingleSupportedFile(files, out path);
    }

    public static bool CanImportDrop(System.Windows.IDataObject data)
    {
        try
        {
            return TryGetSingleSupportedFile(data, out _) ||
                   data.GetDataPresent(System.Windows.DataFormats.Bitmap) ||
                   TryGetBrowserImageUri(data, out _);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> ImportDropAsync(
        System.Windows.IDataObject data,
        CancellationToken cancellationToken = default)
    {
        if (TryGetSingleSupportedFile(data, out var localPath))
        {
            return localPath;
        }

        if (TrySaveBitmapPayload(data, out var bitmapPath))
        {
            return bitmapPath;
        }

        if (!TryGetBrowserImageUri(data, out var imageUri))
        {
            throw new InvalidDataException("Перетащите изображение, а не страницу");
        }

        return await ImportUriAsync(imageUri, cancellationToken);
    }

    public static string SaveClipboardBitmap(BitmapSource image)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusOverlay",
            "images");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"clipboard-{Guid.NewGuid():N}.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        return path;
    }

    public static bool TryDecode(string path, out BitmapImage? image)
    {
        image = null;
        if (!IsSupportedFile(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySaveBitmapPayload(System.Windows.IDataObject data, out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(System.Windows.DataFormats.Bitmap))
        {
            return false;
        }

        try
        {
            var payload = data.GetData(System.Windows.DataFormats.Bitmap);
            if (payload is BitmapSource bitmapSource)
            {
                path = SaveClipboardBitmap(bitmapSource);
                return true;
            }

            if (payload is System.Drawing.Image drawingImage)
            {
                path = CreateManagedImagePath("browser", ".png");
                drawingImage.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return true;
            }
        }
        catch
        {
            // Fall through to URL/HTML formats exposed by the same drag payload.
        }

        return false;
    }

    private static bool TryGetBrowserImageUri(System.Windows.IDataObject data, out Uri uri)
    {
        uri = null!;

        if ((TryReadString(data, System.Windows.DataFormats.Html, out var html) ||
             TryReadString(data, "text/html", out html)) &&
            TryExtractHtmlImageUri(html, out uri))
        {
            return true;
        }

        var urlFormats = new[]
        {
            "text/uri-list",
            "text/x-moz-url-data",
            "text/x-moz-url",
            "UniformResourceLocatorW",
            "UniformResourceLocator"
        };

        foreach (var format in urlFormats)
        {
            if (TryReadString(data, format, out var value) &&
                TryParseFirstUri(value, requireImageLikePath: true, out uri))
            {
                return true;
            }
        }

        return (TryReadString(data, System.Windows.DataFormats.UnicodeText, out var text) ||
                TryReadString(data, System.Windows.DataFormats.Text, out text)) &&
               TryParseFirstUri(text, requireImageLikePath: true, out uri);
    }

    private static bool TryExtractHtmlImageUri(string html, out Uri uri)
    {
        uri = null!;
        var match = ImageSourcePattern.Match(html);
        if (!match.Success)
        {
            return false;
        }

        var source = match.Groups["double"].Success
            ? match.Groups["double"].Value
            : match.Groups["single"].Success
                ? match.Groups["single"].Value
                : match.Groups["bare"].Value;
        source = WebUtility.HtmlDecode(source).Trim();

        if (Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri) &&
            absoluteUri is not null &&
            IsImportableScheme(absoluteUri))
        {
            uri = absoluteUri;
            return true;
        }

        var sourceUrlMatch = HtmlSourceUrlPattern.Match(html);
        if (!sourceUrlMatch.Success ||
            !Uri.TryCreate(sourceUrlMatch.Groups["url"].Value.Trim(), UriKind.Absolute, out var baseUri) ||
            baseUri is null ||
            !Uri.TryCreate(baseUri, source, out var relativeUri) ||
            relativeUri is null ||
            !IsImportableScheme(relativeUri))
        {
            return false;
        }

        uri = relativeUri;
        return true;
    }

    private static bool TryParseFirstUri(string value, bool requireImageLikePath, out Uri uri)
    {
        uri = null!;
        var candidate = value
            .Trim('\0', ' ', '\t', '\r', '\n')
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !line.StartsWith('#'));

        if (candidate is null ||
            !Uri.TryCreate(WebUtility.HtmlDecode(candidate), UriKind.Absolute, out var parsedUri) ||
            parsedUri is null ||
            !IsImportableScheme(parsedUri))
        {
            return false;
        }

        uri = parsedUri;
        return !requireImageLikePath || IsLikelyImageUri(uri);
    }

    private static bool TryReadString(System.Windows.IDataObject data, string format, out string value)
    {
        value = string.Empty;
        try
        {
            if (!data.GetDataPresent(format))
            {
                return false;
            }

            var payload = data.GetData(format);
            switch (payload)
            {
                case string text:
                    value = text;
                    return !string.IsNullOrWhiteSpace(value);
                case byte[] bytes:
                    value = DecodeText(bytes, format);
                    return !string.IsNullOrWhiteSpace(value);
                case Stream stream:
                    var originalPosition = stream.CanSeek ? stream.Position : 0;
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        value = DecodeText(memory.ToArray(), format);
                    }

                    if (stream.CanSeek)
                    {
                        stream.Position = originalPosition;
                    }

                    return !string.IsNullOrWhiteSpace(value);
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string DecodeText(byte[] bytes, string format)
    {
        var isUnicode = format.EndsWith('W') ||
                        (bytes.Length > 2 && bytes.Take(Math.Min(bytes.Length, 32)).Where((_, index) => index % 2 == 1).Count(value => value == 0) > 4);
        var encoding = isUnicode ? Encoding.Unicode : Encoding.UTF8;
        return encoding.GetString(bytes).Trim('\0', '\uFEFF');
    }

    private static bool IsImportableScheme(Uri uri) =>
        uri.Scheme is "http" or "https" or "file" or "data" &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsLikelyImageUri(Uri uri)
    {
        if (uri.Scheme == "data")
        {
            return uri.OriginalString.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
        }

        if (uri.IsFile)
        {
            return IsSupportedFile(uri.LocalPath);
        }

        return SupportedExtensions.Contains(Path.GetExtension(uri.AbsolutePath)) ||
               Regex.IsMatch(uri.Query, "(?:^|[?&])format=(?:png|jpe?g|gif|bmp|webp)(?:&|$)", RegexOptions.IgnoreCase);
    }

    private static async Task<string> ImportUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.IsFile)
        {
            if (IsSupportedFile(uri.LocalPath))
            {
                return Path.GetFullPath(uri.LocalPath);
            }

            throw new InvalidDataException("Файл не является изображением");
        }

        if (uri.Scheme == "data")
        {
            return await ImportDataUriAsync(uri, cancellationToken);
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException("Этот источник изображения не поддерживается");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            using var response = await RemoteClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();

            var extension = GetExtension(response.Content.Headers.ContentType);
            if (extension is null)
            {
                throw new InvalidDataException("Ссылка вернула не изображение");
            }

            if (response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                throw new InvalidDataException("Изображение больше 25 МБ");
            }

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                if (memory.Length + read > MaxImageBytes)
                {
                    throw new InvalidDataException("Изображение больше 25 МБ");
                }

                await memory.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }

            return await SaveValidatedBytesAsync(memory.ToArray(), extension, "browser", timeout.Token);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new InvalidDataException("Загрузка изображения заняла слишком долго");
        }
        catch (HttpRequestException)
        {
            throw new InvalidDataException("Не удалось загрузить изображение");
        }
    }

    private static async Task<string> ImportDataUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        var source = uri.OriginalString;
        var comma = source.IndexOf(',');
        if (comma < 0 || !source[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Формат встроенного изображения не поддерживается");
        }

        var mediaType = source[5..comma].Split(';', 2)[0];
        var extension = GetExtension(new MediaTypeHeaderValue(mediaType));
        if (extension is null)
        {
            throw new InvalidDataException("Формат изображения не поддерживается");
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(source[(comma + 1)..]);
        }
        catch (FormatException)
        {
            throw new InvalidDataException("Повреждённые данные изображения");
        }

        if (bytes.Length > MaxImageBytes)
        {
            throw new InvalidDataException("Изображение больше 25 МБ");
        }

        return await SaveValidatedBytesAsync(bytes, extension, "browser", cancellationToken);
    }

    private static async Task<string> SaveValidatedBytesAsync(
        byte[] bytes,
        string extension,
        string prefix,
        CancellationToken cancellationToken)
    {
        var path = CreateManagedImagePath(prefix, extension);
        try
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            if (!TryDecode(path, out _))
            {
                throw new InvalidDataException("Не удалось распознать изображение");
            }

            return path;
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }
    }

    private static string CreateManagedImagePath(string prefix, string extension)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FocusOverlay",
            "images");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{prefix}-{Guid.NewGuid():N}{extension}");
    }

    private static string? GetExtension(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" or "image/x-ms-bmp" => ".bmp",
            "image/webp" => ".webp",
            _ => null
        };

    private static HttpClient CreateRemoteClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FocusOverlay/0.1");
        return client;
    }
}

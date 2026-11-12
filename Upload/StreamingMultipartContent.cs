using System.Diagnostics;
using System.Net;
using System.Text;

namespace MyFiles.Upload;

/// <summary>
/// Потоковое тело multipart/form-data: сначала поле dir, затем один файл (files[]).
/// Файл читается с диска порциями по 128 КБ и сразу пишется в сеть — целиком в память не попадает.
/// Content-Length задаётся ЯВНО, чтобы исключить Transfer-Encoding: chunked
/// (chunked-тело запроса PHP не разбирает, и $_FILES оказывается пустым).
/// Boundary — простой буквенно-цифровой токен; Content-Type выставляется «сырым» значением,
/// чтобы HttpClient не переформатировал заголовок. Имена файлов — UTF-8 «как есть».
/// </summary>
public sealed class StreamingMultipartContent : HttpContent
{
    private const int BufferSize = 128 * 1024;

    private readonly string _filePath;
    private readonly long _fileLength;
    private readonly byte[] _preamble;
    private readonly byte[] _epilogue;
    private readonly IProgress<long>? _progress;

    /// <summary>Точные заголовки и обрамление тела — для панели диагностики.</summary>
    public string RequestPreview { get; }

    public StreamingMultipartContent(string filePath, string fieldName, string dir, IProgress<long>? progress)
    {
        _filePath = filePath;
        _progress = progress;
        _fileLength = new FileInfo(filePath).Length; // FileNotFoundException, если файла нет

        string boundary = "MyFilesForm" + Guid.NewGuid().ToString("N");
        string field = EscapeQuotes(fieldName);
        string fileName = EscapeQuotes(Path.GetFileName(filePath));

        var sb = new StringBuilder();

        // Часть 1: dir (пустое значение = корневая папка).
        sb.Append("--").Append(boundary).Append("\r\n");
        sb.Append("Content-Disposition: form-data; name=\"dir\"\r\n\r\n");
        sb.Append(dir).Append("\r\n");

        // Часть 2: файл.
        sb.Append("--").Append(boundary).Append("\r\n");
        sb.Append("Content-Disposition: form-data; name=\"").Append(field)
          .Append("\"; filename=\"").Append(fileName).Append("\"\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n\r\n");

        _preamble = Encoding.UTF8.GetBytes(sb.ToString());
        _epilogue = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");

        long total = _preamble.Length + _fileLength + _epilogue.Length;

        string contentType = "multipart/form-data; boundary=" + boundary;
        Headers.TryAddWithoutValidation("Content-Type", contentType);
        Headers.ContentLength = total; // никакого chunked

        RequestPreview =
            contentType + "\r\n" +
            "Content-Length: " + total + "\r\n" +
            "\r\n--- НАЧАЛО ТЕЛА (перед байтами файла) ---\r\n" + Visible(_preamble) +
            "\r\n--- КОНЕЦ ТЕЛА (после байтов файла) ---\r\n" + Visible(_epilogue);
    }

    private static string EscapeQuotes(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");
    }

    /// <summary>Показывает CRLF явно, чтобы в диагностике было видно реальное обрамление.</summary>
    private static string Visible(byte[] data)
    {
        return Encoding.UTF8.GetString(data).Replace("\r\n", "\\r\\n\r\n");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _preamble.Length + _fileLength + _epilogue.Length;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(_preamble, cancellationToken).ConfigureAwait(false);

        using (var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                   BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (file.Length != _fileLength)
            {
                throw new IOException("Размер файла изменился после добавления в список: " + Path.GetFileName(_filePath));
            }

            byte[] buffer = new byte[BufferSize];
            long sent = 0;
            var watch = Stopwatch.StartNew();
            while (true)
            {
                int read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                sent += read;
                if (watch.ElapsedMilliseconds >= 100 || sent == _fileLength)
                {
                    _progress?.Report(sent);
                    watch.Restart();
                }
            }
            if (sent != _fileLength)
            {
                throw new IOException("Файл изменился во время загрузки: " + Path.GetFileName(_filePath));
            }
            _progress?.Report(sent);
        }

        await stream.WriteAsync(_epilogue, cancellationToken).ConfigureAwait(false);
    }
}

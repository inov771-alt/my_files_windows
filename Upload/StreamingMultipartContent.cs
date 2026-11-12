using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MyFiles.Upload;

/// <summary>
/// Потоковое тело multipart/form-data для одного файла.
/// Файл читается с диска порциями по 128 КБ и сразу пишется в сетевой поток:
/// весь файл НИКОГДА не загружается в оперативную память целиком.
/// Заголовки части (включая имя файла) формируются вручную в UTF-8 —
/// кириллические имена передаются «как есть», точно так же, как их отправляют
/// браузеры и curl, и корректно понимаются PHP на сервере.
/// Content-Length известен заранее, поэтому HttpClient не буферизует тело.
/// </summary>
public sealed class StreamingMultipartContent : HttpContent
{
    private const int BufferSize = 128 * 1024;

    private readonly string _filePath;
    private readonly long _fileLength;
    private readonly byte[] _preamble;
    private readonly byte[] _epilogue;
    private readonly IProgress<long>? _progress;

    public StreamingMultipartContent(string filePath, string fieldName, string dir, IProgress<long>? progress)
    {
        _filePath = filePath;
        _progress = progress;
        _fileLength = new FileInfo(filePath).Length; // FileNotFoundException, если файла нет

        string boundary = "----MyFilesBoundary" + Guid.NewGuid().ToString("N");
        string fileName = EscapeQuotes(Path.GetFileName(filePath));
        string field = EscapeQuotes(fieldName);

        var sb = new StringBuilder();
        sb.Append("--").Append(boundary).Append("\r\n");
        sb.Append("Content-Disposition: form-data; name=\"").Append(field)
          .Append("; filename=\"").Append(fileName).Append("\"\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n\r\n");
        _preamble = Encoding.UTF8.GetBytes(sb.ToString());

        sb.Clear();
        sb.Append("\r\n--").Append(boundary).Append("\r\n");
        sb.Append("Content-Disposition: form-data; name=\"dir\"\r\n\r\n");
        sb.Append(dir).Append("\r\n");
        sb.Append("--").Append(boundary).Append("--\r\n");
        _epilogue = Encoding.UTF8.GetBytes(sb.ToString());

        Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
        Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));
    }

    private static string EscapeQuotes(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");
    }

    // ВНИМАНИЕ: здесь именно protected override, НЕ protected internal override.
    // HttpContent.TryComputeLength — protected internal в сборке System.Net.Http.dll.
    // При переопределении из другой сборки модификатор обязан быть protected (иначе CS0507).
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

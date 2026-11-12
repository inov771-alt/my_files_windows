using System.Net;

namespace MyFiles.Upload;

/// <summary>Загрузка файлов на сервер. Каждый файл — отдельный POST-запрос multipart/form-data.</summary>
public sealed class UploadClient : IDisposable
{
    public const string ApiUrl = "https://files.spkmd.ru/index.php?api=upload";

    private readonly HttpClient _http;

    public UploadClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = false, // редирект POST превращает в GET и теряет тело — лучше показать ошибку
            AutomaticDecompression = DecompressionMethods.All
        };
        _http = new HttpClient(handler)
        {
            // Большие файлы могут загружаться часами: общий таймаут отключён,
            // отмена выполняется кнопкой «Отмена» через CancellationToken.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MyFilesWindows/1.0");
    }

    public Task<UploadResult> UploadAsync(string filePath, string dir, IProgress<long>? progress, CancellationToken ct)
    {
        return UploadAsync(ApiUrl, filePath, dir, progress, ct);
    }

    public async Task<UploadResult> UploadAsync(string url, string filePath, string dir, IProgress<long>? progress, CancellationToken ct)
    {
        try
        {
            using var content = new StreamingMultipartContent(filePath, "files[]", dir, progress);
            using HttpResponseMessage response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ServerResponse.Parse((int)response.StatusCode, body);
        }
        catch (OperationCanceledException)
        {
            throw; // отмена пользователем — обрабатывается вызывающим кодом
        }
        catch (HttpRequestException ex)
        {
            string detail = ex.InnerException?.Message ?? ex.Message;
            return new UploadResult(false, "Сетевая ошибка: " + detail);
        }
        catch (IOException ex)
        {
            return new UploadResult(false, "Ошибка чтения файла: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new UploadResult(false, "Нет доступа к файлу: " + ex.Message);
        }
        catch (Exception ex)
        {
            return new UploadResult(false, "Ошибка: " + ex.Message);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

using System.Net;

namespace MyFiles.Upload;

/// <summary>Загрузка файлов на сервер. Каждый файл — отдельный POST multipart/form-data.</summary>
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
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All
        };
        _http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MyFilesWindows/1.0");
    }

    public Task<UploadResult> UploadAsync(string filePath, string dir, IProgress<long>? progress, CancellationToken ct, IProgress<string>? stage = null)
    {
        return UploadAsync(ApiUrl, filePath, dir, progress, ct, stage);
    }

    public async Task<UploadResult> UploadAsync(string url, string filePath, string dir, IProgress<long>? progress, CancellationToken ct, IProgress<string>? stage = null)
    {
        string currentStage = "Подготовка";
        string requestInfo = "";
        int? statusCode = null;
        string body = "";
        try
        {
            currentStage = "Подготовка multipart";
            stage?.Report(currentStage);

            var content = new StreamingMultipartContent(filePath, "files[]", dir, progress);
            requestInfo = "POST " + url + "\r\n" + content.RequestPreview;

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            currentStage = "Отправка запроса";
            stage?.Report(currentStage);

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            statusCode = (int)response.StatusCode;
            currentStage = "Чтение тела ответа (HTTP " + statusCode + ")";
            stage?.Report(currentStage);
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            currentStage = "Разбор ответа";
            stage?.Report(currentStage);
            UploadResult result = ServerResponse.Parse(statusCode.Value, body);
            return result with
            {
                Stage = result.Success ? "Готово" : "Разбор ответа: отказ",
                RequestInfo = requestInfo
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UploadResult(false, DescribeError(ex, statusCode))
            {
                StatusCode = statusCode,
                ResponseBody = body,
                Stage = currentStage,
                ExceptionText = ex.ToString(),
                RequestInfo = requestInfo
            };
        }
    }

    private static string DescribeError(Exception ex, int? statusCode)
    {
        string kind = ex switch
        {
            HttpRequestException => "Сетевая ошибка",
            IOException => "Ошибка ввода-вывода (файл/поток)",
            UnauthorizedAccessException => "Нет доступа к файлу",
            _ => "Ошибка (" + ex.GetType().Name + ")"
        };
        string text = kind + ": " + ex.Message;
        if (ex.InnerException is not null)
        {
            text += " → " + ex.InnerException.Message;
        }
        if (statusCode.HasValue)
        {
            text += " [HTTP " + statusCode.Value + "]";
        }
        return text;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}

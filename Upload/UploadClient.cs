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
            // Как Android/OkHttp: следуем редиректам (307/308 сохраняют метод и тело).
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All
        };
        _http = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan // отмена — кнопкой «Отмена», не таймаутом
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
        int? statusCode = null;
        string body = "";
        try
        {
            currentStage = "Подготовка multipart";
            stage?.Report(currentStage);

            var content = new StreamingMultipartContent(filePath, "files[]", dir, progress);
            // HttpRequestMessage владеет контентом и освободит его в своём Dispose().
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            currentStage = "Отправка запроса";
            stage?.Report(currentStage);

            // PostAsync не поддерживает HttpCompletionOption — используем SendAsync.
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
            return result with { Stage = result.Success ? "Готово" : "Разбор ответа: отказ" };
        }
        catch (OperationCanceledException)
        {
            throw; // отмена пользователем — обрабатывается вызывающим кодом
        }
        catch (Exception ex)
        {
            // Ничего не скрываем: тип, Message, inner-exception, HTTP-код, этап, тело ответа.
            return new UploadResult(false, DescribeError(ex, statusCode))
            {
                StatusCode = statusCode,
                ResponseBody = body,
                Stage = currentStage,
                ExceptionText = ex.ToString()
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

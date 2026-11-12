namespace MyFiles.Upload;

/// <summary>
/// Итог загрузки одного файла + полная диагностика (HTTP-код, тело ответа, исключение, этап).
/// Conflict — на сервере уже есть файл с таким именем.
/// </summary>
public sealed record UploadResult(bool Success, string Message, bool Conflict = false)
{
    public int? StatusCode { get; init; }
    public string ResponseBody { get; init; } = "";
    public string Stage { get; init; } = "";
    public string ExceptionText { get; init; } = "";
}

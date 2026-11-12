namespace MyFiles.Upload;

/// <summary>Итог загрузки одного файла. Conflict — на сервере уже есть файл с таким именем.</summary>
public sealed record UploadResult(bool Success, string Message, bool Conflict = false);

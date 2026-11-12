namespace MyFiles.Upload;

public enum UploadState
{
    Waiting,
    Uploading,
    Done,
    Error,
    Cancelled
}

/// <summary>Один файл в списке загрузки (без привязки к интерфейсу).</summary>
public sealed class UploadItem
{
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public long Size { get; set; }
    public long Sent { get; set; }
    public UploadState State { get; set; } = UploadState.Waiting;
    public string? Error { get; set; }

    public int Percent
    {
        get
        {
            if (State == UploadState.Done) return 100;
            if (Size <= 0) return 0;
            return (int)Math.Min(100, Sent * 100 / Size);
        }
    }
}

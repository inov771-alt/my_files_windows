using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MyFiles;

/// <summary>Одно окно приложения + передача аргументов повторных запусков в первое окно.</summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\MyFiles.SpkmD.SingleInstance.v1";
    private const string PipeName = "MyFiles.SpkmD.Activate.v1";

    public static Mutex? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            return mutex;
        }
        mutex.Dispose();
        return null;
    }

    public static bool TrySendArgs(string[] args)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(args);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(700);
                pipe.Write(BitConverter.GetBytes(payload.Length), 0, 4);
                pipe.Write(payload, 0, payload.Length);
                pipe.Flush();
                return true;
            }
            catch
            {
                Thread.Sleep(250); // главное окно могло ещё не успеть запустить сервер
            }
        }
        return false;
    }

    public static void StartServer(Action<string[]> onArgs)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);

                    byte[] lenBuf = new byte[4];
                    await server.ReadExactlyAsync(lenBuf).ConfigureAwait(false);
                    int len = BitConverter.ToInt32(lenBuf, 0);
                    if (len <= 0 || len > 32 * 1024 * 1024)
                    {
                        continue;
                    }
                    byte[] buf = new byte[len];
                    await server.ReadExactlyAsync(buf).ConfigureAwait(false);
                    string[] args = JsonSerializer.Deserialize<string[]>(buf) ?? Array.Empty<string>();
                    onArgs(args);
                }
                catch
                {
                    // Ошибки канала не должны ронять приложение.
                }
            }
        });
    }
}

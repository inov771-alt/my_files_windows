namespace MyFiles;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        // Одно окно на пользователя: повторные запуски (например, Проводник передал
        // слишком длинный список файлов несколькими запусками) передают свои файлы
        // уже открытому окну через именованный канал.
        Mutex? mutex = SingleInstance.TryAcquire();
        if (mutex is null && SingleInstance.TrySendArgs(args))
        {
            return;
        }

        using (var form = new MainForm())
        {
            _ = form.Handle; // создаём дескриптор окна до запуска IPC-сервера (нужен для BeginInvoke)
            if (mutex is not null)
            {
                SingleInstance.StartServer(a => form.BeginInvoke(new Action(() => form.OnExternalArgs(a))));
            }
            if (form.AddPaths(args) > 0)
            {
                form.EnableAutoStart(); // запуск из Проводника: начинаем загрузку сразу
            }
            Application.Run(form);
        }

        if (mutex is not null)
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}

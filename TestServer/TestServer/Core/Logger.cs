namespace TestServer.Core;

public static class Logger
{
    private static readonly object _lock = new object();

    public static void Log(string module, string message, ConsoleColor color = ConsoleColor.White)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss.fff}] ");

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"[{module,-12}] ");

            Console.ForegroundColor = color;
            Console.WriteLine(message);

            Console.ResetColor();
        }
    }
}
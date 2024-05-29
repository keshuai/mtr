namespace mtr;

public static class ConsoleLineEx
{
    public static bool CanWrite(int consoleLine)
    {
        return consoleLine >= 0 && consoleLine < Console.BufferHeight;
    }

    public static void ClearLine(int consoleLine)
    {
        if (CanWrite(consoleLine))
        {
            Console.SetCursorPosition(0, consoleLine);
            Console.Write(new string(' ', Console.WindowWidth));
        }
    }

    public static void Write(int consoleLine, string str) => Write(consoleLine, 0, str);

    public static void Write(int consoleLine, int left, string str)
    {
        // 高度越界
        if (!CanWrite(consoleLine))
        {
            return;
        }

        // 清理当前行
        {
            Console.SetCursorPosition(0, consoleLine);
            Console.Write(new string(' ', Console.WindowWidth));
        }

        // 宽度越界
        if (left >= Console.WindowWidth)
        {
            return;
        }

        Console.SetCursorPosition(left, consoleLine);
        Console.Write(str);
    }
}
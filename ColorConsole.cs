// File: ColorConsole.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

namespace DotnetHtop;

public static class ColorConsole
{
    private static readonly object _lock = new();

    public static void Write(string text, ConsoleColor fg, ConsoleColor? bg = null)
    {
        lock (_lock)
        {
            if (bg.HasValue) Console.BackgroundColor = bg.Value;
            Console.ForegroundColor = fg;
            Console.Write(text);
            Console.ResetColor();
        }
    }

    public static void WriteLine(string text, ConsoleColor fg, ConsoleColor? bg = null)
    {
        Write(text + Environment.NewLine, fg, bg);
    }

    public static void WriteSuccess(string text) => WriteLine(text, ConsoleColor.Green);
    public static void WriteWarning(string text) => WriteLine(text, ConsoleColor.Yellow);
    public static void WriteError(string text)   => WriteLine(text, ConsoleColor.Red);
    public static void WriteInfo(string text)    => WriteLine(text, ConsoleColor.Cyan);
    public static void WriteDim(string text)     => WriteLine(text, ConsoleColor.DarkGray);

    public static void WriteKV(string label, string value, ConsoleColor valueColor = ConsoleColor.White)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(label);
            Console.ForegroundColor = valueColor;
            Console.Write(value);
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Render a horizontal bar. Full block for filled (bright), light shade for empty (dim).
    /// </summary>
    public static void WriteBar(double pct, int width = 25, ConsoleColor? color = null)
    {
        var filled = (int)Math.Round(Math.Clamp(pct / 100.0, 0, 1) * width);
        var col = color ?? PercentColor(pct);
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write('[');
            Console.ForegroundColor = col;
            // U+2588 FULL BLOCK, U+2591 LIGHT SHADE
            Console.Write(new string('\u2588', filled));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(new string('\u2591', width - filled));
            Console.Write(']');
            Console.ResetColor();
        }
    }

    public static ConsoleColor PercentColor(double pct) => pct switch
    {
        >= 80 => ConsoleColor.Red,
        >= 50 => ConsoleColor.Yellow,
        >= 20 => ConsoleColor.Cyan,
        _     => ConsoleColor.Green,
    };

    public static ConsoleColor HexToConsoleColor(string hex) => hex.ToUpperInvariant() switch
    {
        "#FF0000" => ConsoleColor.Red,
        "#00FF00" => ConsoleColor.Green,
        "#0000FF" => ConsoleColor.Blue,
        "#FFFF00" => ConsoleColor.Yellow,
        "#FF8800" or "#FFA500" => ConsoleColor.DarkYellow,
        "#FF00FF" => ConsoleColor.Magenta,
        "#00FFFF" => ConsoleColor.Cyan,
        "#FFFFFF" => ConsoleColor.White,
        "#000000" => ConsoleColor.Black,
        "#808080" => ConsoleColor.Gray,
        "#808000" => ConsoleColor.DarkYellow,
        "#800000" => ConsoleColor.DarkRed,
        "#008000" => ConsoleColor.DarkGreen,
        "#000080" => ConsoleColor.DarkBlue,
        "#800080" => ConsoleColor.DarkMagenta,
        "#008080" => ConsoleColor.DarkCyan,
        _         => ConsoleColor.White,
    };

    public static ConsoleColor ColorForUsage(double pct, List<ColorMapping> thresholds)
    {
        var match = thresholds
            .OrderByDescending(t => t.Threshold)
            .FirstOrDefault(t => pct >= t.Threshold);
        return match is not null
            ? HexToConsoleColor(match.Color)
            : ConsoleColor.White;
    }
}

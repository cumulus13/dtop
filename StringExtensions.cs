// File: StringExtensions.cs
// Author: Hadi Cahyadi <cumulus13@gmail.com>
// Date: 2026-04-18
// Description: 
// License: MIT

namespace DotnetHtop;

public static class StringExtensions
{
    public static string Truncate(this string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

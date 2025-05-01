using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MediaServer
{
    public static class Extensions
    {
        public static string ChopOffBefore(this string s, string before)
        {
            int end = s.ToUpper().IndexOf(before.ToUpper());
            return end > -1 ? s.Substring(end + before.Length) : s;
        }

        public static string ChopOffAfter(this string s, string after)
        {
            int end = s.ToUpper().IndexOf(after.ToUpper());
            return end > -1 ? s.Substring(0, end) : s;
        }

        public static string ReplaceIgnoreCase(this string source, string pattern, string replacement)
        {
            return Regex.IsMatch(source, pattern, RegexOptions.IgnoreCase)
                ? Regex.Replace(source, pattern, replacement, RegexOptions.IgnoreCase)
                : source;
        }
    }
}

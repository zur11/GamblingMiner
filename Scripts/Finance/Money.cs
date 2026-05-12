using System;
using System.Globalization;

namespace Scripts.Finance
{
    public static class Money
    {
        public const int Precision = 8;
        public const decimal SmallestUnit = 0.00000001m;

        public static decimal Normalize(decimal value)
        {
            return Math.Round(value, Precision, MidpointRounding.ToZero);
        }

        public static string FormatSignedAdaptive(decimal value, int maxDecimals = 16)
        {
            if (value == 0m)
                return "0.00000000";

            if (Math.Abs(value) >= SmallestUnit)
                return value.ToString("+0.00000000;-0.00000000", CultureInfo.InvariantCulture);

            string sign = value > 0m ? "+" : "-";
            string magnitude = Math.Abs(value).ToString($"0.{new string('#', maxDecimals)}", CultureInfo.InvariantCulture);
            return sign + magnitude;
        }
    }
}

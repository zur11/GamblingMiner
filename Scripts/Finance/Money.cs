using System;

namespace Scripts.Finance
{
    public static class Money
    {
        public const int Precision = 8;

        public static decimal Normalize(decimal value)
        {
            return Math.Round(value, Precision, MidpointRounding.ToZero);
        }
    }
}
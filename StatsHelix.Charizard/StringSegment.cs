using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    public struct StringSegment
    {
        public string UnderlyingString { get; set; }
        public int Index { get; set; }
        public int Length { get; set; }

        public bool Empty
        {
            get { return Length == 0; }
        }

        public StringSegment(string underlying)
        {
            UnderlyingString = underlying;
            Index = 0;
            Length = UnderlyingString?.Length ?? 0;
        }

        public char this[int index]
        {
            get
            {
                if (index >= Length)
                    return '\0';
                return UnderlyingString[Index + index];
            }
        }

        public int IndexOf(char c, int searchStart = 0)
        {
            for (int i = searchStart; i < Length; i++)
                if (this[i] == c)
                    return i;
            return -1;
        }

        public StringSegment Substring(int index)
        {
            return Substring(index, Length - index);
        }

        public bool StartsWith(string str) => (str.Length <= Length) && UnderlyingString.IndexOf(str, Index, str.Length) == Index;

        public StringSegment Substring(int index, int length)
        {
            if (index < 0 || index > Length)
                throw new ArgumentOutOfRangeException("index");
            if (length < 0 || length > (Length - index))
                length = Length - index;

            return new StringSegment
            {
                UnderlyingString = UnderlyingString,
                Index = Index + index,
                Length = length,
            };
        }

        public static bool operator ==(StringSegment a, string b)
        {
            return a == new StringSegment(b);
        }

        public static bool operator !=(StringSegment a, string b)
        {
            return !(a == b);
        }

        public static bool operator ==(string a, StringSegment b)
        {
            return b == a;
        }

        public static bool operator !=(string a, StringSegment b)
        {
            return b != a;
        }

        public static bool operator ==(StringSegment a, StringSegment b)
        {
            return a.Length == b.Length && String.CompareOrdinal(a.UnderlyingString,
                a.Index, b.UnderlyingString, b.Index, a.Length) == 0;
        }

        public static bool operator !=(StringSegment a, StringSegment b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var other = obj as StringSegment?;
            if (!other.HasValue)
                return Empty;
            return this == other.Value;
        }

        public override string ToString()
        {
            return UnderlyingString?.Substring(Index, Length) ?? String.Empty;
        }
    }
}

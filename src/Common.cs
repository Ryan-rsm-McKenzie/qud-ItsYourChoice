#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace ItsYourChoice
{
	internal static class IEnumerableExt
	{
		public static IEnumerable<(int Enumerator, T Item)> Enumerate<T>(this IEnumerable<T> self)
		{
			int i = 0;
			foreach (var elem in self) {
				yield return (i++, elem);
			}
		}

		public static IEnumerable<T> Filter<T>(this IEnumerable<T> self, Func<T, bool> predicate)
		{
			return self.Where(predicate);
		}

		public static IEnumerable<U> Map<T, U>(this IEnumerable<T> self, Func<T, U> f)
		{
			return self.Select(f);
		}

		public static ArraySegment<T> Slice<T>(this T[] self, int offset, int count)
		{
			return new ArraySegment<T>(self, offset, count);
		}
	}

	internal static class MathExt
	{
		public static T Clamp<T>(T v, T lo, T hi)
			where T : IComparable<T>
		{
			return v.CompareTo(lo) < 0 ? lo : v.CompareTo(hi) > 0 ? hi : v;
		}
	}
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Qud.UI;
using XRL.UI;

namespace ItsYourChoice
{
	[HarmonyPatch(typeof(OptionsScreen))]
	public class OptionsScreen_Exit
	{
		private static Configuration? s_config;

		[HarmonyPatch(nameof(OptionsScreen.Hide))]
		[HarmonyPatch(new Type[] { })]
		public static void Postfix()
		{
			var latest = new Configuration();
			if (s_config != latest) {
				Popup.Show(
					Message: "Some settings require the installed mod configuration to be saved and reloaded to apply.",
					LogMessage: false);
			}
		}

		[HarmonyPatch(nameof(OptionsScreen.Show))]
		[HarmonyPatch(new Type[] { })]
		public static void Prefix()
		{
			s_config = new();
		}
	}

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

	internal record Configuration
	{
		public readonly bool Leveling;

		public readonly bool Injector;

		public Configuration()
		{
			this.Leveling = Options
				.GetOption(ID: "Option_RSM_ItsYourChoice_Leveling", Default: "Yes")
				.EqualsNoCase("Yes");
			this.Injector = Options
				.GetOption(ID: "Option_RSM_ItsYourChoice_Injector", Default: "Yes")
				.EqualsNoCase("Yes");
		}
	}
}

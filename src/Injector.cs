#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;

namespace ItsYourChoice
{
	[HarmonyPatch(typeof(Nectar_Tonic_Applicator))]
	[HarmonyPatch(nameof(Nectar_Tonic_Applicator.FireEvent))]
	[HarmonyPatch(new Type[] { typeof(Event) })]
	public class Nectar_Tonic_Applicator_FireEvent
	{
		public static void Detour(GameObject subject, int dosage)
		{
			if (subject.HasStat("MP")) {
				string plural = dosage > 1 ? "s" : "";
				string[] options = new string[] {
					$"+{dosage} Attribute Point{plural}",
					$"+{dosage} Mutation Point{plural}",
				};

				while (true) {
					int choice = Popup.ShowOptionList(
						Title: "Choose your bonus:",
						Options: options);
					if (choice == 0) {
						subject.GetStat("AP").BaseValue += dosage;
						break;
					} else if (choice == 1) {
						subject.GainMP(dosage);
						break;
					}
				}
			} else {
				subject.GetStat("AP").BaseValue += dosage;
			}
		}

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			int start;
			int stop;
			var cached = instructions.ToList();

			{
				var matcher = new CodeMatcher(cached)
					.MatchStartForward(new CodeMatch[] {
						new(OpCodes.Ldc_I4_S, (sbyte)50),
						new(OpCodes.Call, AccessTools.Method(
							type: typeof(Extensions),
							name: nameof(Extensions.in100),
							parameters: new Type[]{ typeof(int) })),
						new(OpCodes.Brfalse),
					});
				if (matcher.IsValid) {
					start = matcher.Pos;
				} else {
					Logger.buildLog.Error("Failed to locate mutant branch start for nectar injector!");
					return instructions;
				}
			}

			{
				var matcher = new CodeMatcher(cached)
					.MatchStartForward(new CodeMatch[] {
						new(OpCodes.Ldloc_1),
						new(OpCodes.Callvirt, AccessTools.Method(
							type: typeof(GameObject),
							name: nameof(GameObject.IsPlayer),
							parameters: new Type[] { }
						)),
						new(OpCodes.Brfalse_S),
					});
				if (matcher.IsValid) {
					stop = matcher.Pos;
				} else {
					Logger.buildLog.Error("Failed to mutant branch end for nectar injector!");
					return instructions;
				}
			}

			var result = new CodeMatcher(cached)
				.Advance(start + 1)
				.RemoveInstructions(stop - start)
				.Insert(new CodeInstruction[] {
					new(OpCodes.Ldloc_1),
					new(OpCodes.Ldloc_0),
					new(OpCodes.Call, AccessTools.Method(
						type: typeof(Nectar_Tonic_Applicator_FireEvent),
						name: nameof(Detour),
						parameters: new Type[] { typeof(GameObject), typeof(int) }
					)),
				})
				.Instructions();
			result[start].labels = cached[start].labels;

			return result;
		}
	}
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using ConsoleLib.Console;
using HarmonyLib;
using XRL.Language;
using XRL.UI;
using XRL.World;
using XRL.World.Anatomy;
using XRL.World.Parts;

namespace ItsYourChoice.Patches
{
	internal readonly struct Limb
	{
		public readonly BodyPart Part;

		private readonly string _display;

		public Limb(BodyPart part)
		{
			string display = Grammar.MakeTitleCase(part.GetCardinalName());
			if (Options.IndentBodyParts) {
				int depth = part.ParentBody?.GetPartDepth(part) ?? 0;
				display = display.PadLeft(display.Length + depth, ' ');
			}

			this._display = display;
			this.Part = part;
		}

		public readonly override string ToString() => this._display;
	}

	[UIView(ID: "RSM_LimbScreen", ForceFullscreen: true, NavCategory: "Menu")]
	public class LimbScreen : IWantsTextConsoleInit
	{
		private const int VIEWPORT_SIZE = YMAX - 3 + 1;

		private const int XMAX = 79;

		private const int YMAX = 24;

		private static readonly string[] s_hotkeyLayers = new string[] { "Menus", "UINav" };

		private static ScreenBuffer? s_screenBuffer = null;

		private static TextConsole? s_textConsole = null;

		public static void Show(Mutations subject)
		{
			var attachables = Anatomies.GetBodyPartTypeSelector(UseChimeraWeight: true)
				.Filter(x => x.Value > 0)
				.Map(x => (x.Key, x.Key.Name))
				.ToSortedList();
			if (attachables.Count == 0) {
				MetricsManager.LogWarning($"could not find any viable chimera limbs for subject: {subject.DebugName}");
				return;
			}

			var limbs = ParseLimbs(subject.ParentObject);
			if (limbs.Length == 0) {
				MetricsManager.LogWarning($"could not find any viable attach points for subject: {subject.DebugName}");
				return;
			}

			using var _ = new GameViewContext(s_textConsole!, "RSM_LimbScreen");
			var screen = s_screenBuffer!;
			Func<int, ArraySegment<Limb>> reslice = offset =>
				limbs.Slice(offset, Math.Min(VIEWPORT_SIZE, limbs.Length - offset));
			var viewport = reslice(0);
			int selection = 0;

			while (true) {
				screen.Clear();
				screen.SingleBox(0, 0, XMAX, YMAX, ColorUtility.MakeColor(TextColor.Grey, TextColor.Black));

				string header = "[ {{W|Choose where to grow your new limb:}} ]";
				screen.Goto((XMAX - ColorUtility.StripFormatting(header).Length) / 2, 0);
				screen.Write(header);

				if (selection < viewport.Offset)
					viewport = reslice(selection);
				else if (selection >= viewport.Offset + viewport.Count)
					viewport = reslice(MathExt.Clamp(selection - (VIEWPORT_SIZE - 1), 0, limbs.Length - 1));

				var spread = HotkeySpread.get(s_hotkeyLayers);
				foreach (var (i, limb) in viewport.Enumerate()) {
					string selected = selection == i + viewport.Offset ? "{{Y|>}}{{W|" : " {{w|";
					char hotkey = spread.charAtOr(i, ' ');
					screen.Goto(1, i + 2);
					screen.Write($"{selected}{hotkey}}}}}) {limb}");
				}

				if (viewport.Offset > 0) {
					screen.Goto(2, 0);
					screen.Write("<more...>");
				}
				if (viewport.Offset + viewport.Count < limbs.Length) {
					screen.Goto(2, YMAX);
					screen.Write("<more...>");
				}

				s_textConsole!.DrawBuffer(screen);
				var input = Keyboard.getvk(Options.MapDirectionsToKeypad);
				ScreenBuffer.ClearImposterSuppression();
				if (input == Keys.NumPad8) {
					selection = MathExt.Clamp(selection - 1, 0, limbs.Length - 1);
				} else if (input == Keys.NumPad2) {
					selection = MathExt.Clamp(selection + 1, 0, limbs.Length - 1);
				} else if (Keyboard.IsCommandKey("Page Down")) {
					int last = viewport.Offset + viewport.Count - 1;
					selection = selection == last ?
						MathExt.Clamp(last + (VIEWPORT_SIZE - 1), 0, limbs.Length - 1) :
						last;
				} else if (Keyboard.IsCommandKey("Page Up")) {
					int first = viewport.Offset;
					selection = selection == first ?
						MathExt.Clamp(first - (VIEWPORT_SIZE - 1), 0, limbs.Length - 1) :
						first;
				} else if (input == Keys.Space || input == Keys.Enter) {
					bool chosen = MakePartSelection(
						subject: subject,
						attachPoint: limbs[selection].Part,
						attachables: attachables);
					if (chosen) {
						break;
					}
				}
			}
		}

		public void Init(TextConsole console, ScreenBuffer buffer)
		{
			s_textConsole = console;
			s_screenBuffer = buffer;
		}

		private static bool MakePartSelection(
			Mutations subject,
			BodyPart attachPoint,
			SortedList<BodyPartType> attachables)
		{
			int selection = Popup.ShowOptionList(
				Title: "",
				Options: attachables.Names,
				AllowEscape: true);
			if (selection >= 0) {
				var partTemplate = attachables.Values[selection];
				var newPart = new BodyPart(
					Base: partTemplate,
					ParentBody: attachPoint.ParentBody,
					Manager: "Chimera",
					Dynamic: true);

				var children = Anatomies.FindUsualChildBodyPartTypes(partTemplate) ?? Enumerable.Empty<BodyPartType>();
				foreach (var child in children) {
					newPart.AddPart(new BodyPart(
						Base: child,
						ParentBody: newPart.ParentBody,
						Manager: "Chimera"));
				}

				var parent = subject.ParentObject!;
				string what =
					newPart.Mass ? $"Some {newPart.Name}" :
					newPart.Plural ? $"A set of {newPart.Name}" :
					Grammar.A(newPart.Name, Capitalize: true);
				string whose = parent.IsPlayer() ?
					"your" :
					Grammar.MakePossessive(parent.the + parent.ShortDisplayName);
				subject.EmitMessage(
					Msg: $"{what} grows out of {whose} {attachPoint.GetOrdinalName()}!",
					FromDialog: true);

				if (newPart.Laterality == 0) {
					if (!partTemplate.UsuallyOn.IsNullOrEmpty() && partTemplate.UsuallyOn != attachPoint.Type) {
						var model = attachPoint.VariantTypeModel();
						newPart.ModifyNameAndDescriptionRecursively(
							NameMod: model.Name.Replace(" ", "-"),
							DescMod: model.Description.Replace(" ", "-"));
					}
					if (attachPoint.Laterality != 0) {
						newPart.ChangeLaterality(
							NewLaterality: attachPoint.Laterality | newPart.Laterality,
							Recursive: true);
					}
				}

				attachPoint.AddPart(
					NewPart: newPart,
					InsertAfter: newPart.Type,
					OrInsertBefore: new string[] { "Thrown Weapon", "Floating Nearby" });
				return true;
			} else {
				return false;
			}
		}

		private static Limb[] ParseLimbs(GameObject subject)
		{
			return subject
				.Body
				.GetParts()
				.Filter(x => !x.Abstract && x.Contact && !x.Extrinsic && x.DependsOn.IsNullOrEmpty() && x.RequiresType.IsNullOrEmpty() && BodyPartCategory.IsLiveCategory(x.Category))
				.Map(x => new Limb(x))
				.ToArray();
		}
	}

	[HarmonyPatch(typeof(StatusScreen))]
	[HarmonyPatch(nameof(StatusScreen.BuyRandomMutation))]
	[HarmonyPatch(new Type[] { typeof(GameObject) })]
	public class StatusScreen_BuyRandomMutation
	{
		public static void Detour(Mutations subject) => LimbScreen.Show(subject);

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var previous = new CodeMatch[] {
				new(OpCodes.Ldloc_1),
				new(OpCodes.Ldc_I4_0),
				new(OpCodes.Ldstr, "Chimera"),
				new(OpCodes.Ldnull),
				new(OpCodes.Callvirt, AccessTools.Method(
					type: typeof(Mutations),
					name: nameof(Mutations.AddChimericBodyPart),
					parameters: new Type[]{ typeof(bool), typeof(string), typeof(BodyPart)})),
				new(OpCodes.Pop),
			};

			var matcher = new CodeMatcher(instructions);
			matcher.MatchStartForward(previous);
			if (matcher.IsValid) {
				matcher.RemoveInstructions(previous.Length);
				matcher.Insert(new CodeInstruction[] {
					new(OpCodes.Ldloc_1),
					new(OpCodes.Call, AccessTools.Method(
						type: typeof(StatusScreen_BuyRandomMutation),
						name: nameof(Detour),
						parameters: new Type[]{ typeof(Mutations) })),
				});
				instructions = matcher.Instructions();
			} else {
				Logger.buildLog.Error("Failed to install chimera part selection detour!");
			}

			return instructions;
		}
	}

	internal static class Extensions
	{
#pragma warning disable IDE1006 // Naming Styles
		public static char charAtOr(this HotkeySpread self, int n, char defaulted)
		{
			char c = self.charAt(n);
			return c != '\0' ? c : defaulted;
		}

#pragma warning restore IDE1006 // Naming Styles

		public static SortedList<T> ToSortedList<T>(this IEnumerable<(T, string)> self)
		{
			return new SortedList<T>(self);
		}
	}

	internal sealed class GameViewContext : IDisposable
	{
		private readonly ScreenBuffer _buffer;

		private readonly TextConsole _console;

		public GameViewContext(TextConsole console, string view)
		{
			this._console = console;
			this._buffer = ScreenBuffer.GetScrapBuffer1(bLoadFromCurrent: true);
			GameManager.Instance.PushGameView(view);
		}

		public void Dispose()
		{
			GameManager.Instance.PopGameView();
			this._console.DrawBuffer(this._buffer);
		}
	}

	internal sealed class SortedList<T>
	{
		private readonly List<string> _names = new();

		private readonly List<T> _values = new();

		public SortedList(IEnumerable<(T, string)> list)
		{
			var iter = list
				.Map(x => (Value: x.Item1, Name: Grammar.InitialCap(x.Item2)))
				.OrderBy(x => x.Name);
			foreach (var (value, name) in iter) {
				this._values.Add(value);
				this._names.Add(name);
			}
		}

		public int Count => this._values.Count;

		public IList<string> Names => this._names;

		public IList<T> Values => this._values;

		public T this[int key] { get => this._values[key]; }
	}
}

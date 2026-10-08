using Godot;
using Scripts.Hardware;
using System.Collections.Generic;

namespace UI.DevPlayerCreditsSelector
{
	// DEV/TEST ONLY (mini-plan 20 Part 0, developer's request 2026-10-08) — sets the PLAYER node's hardware credits
	// directly from DiceGame, so a test that moves the credit count (1 → 10 → 99) does not need a round-trip
	// through Mining Pools & Hardware for every step. The shipping path is, and stays, that scene: this control is
	// a test convenience that writes through the SAME repository calls the shop's DEV buttons use
	// (AddCredits lands in the individual pool, RemoveCredits takes from the casino pool first and never goes
	// below 1), so it cannot produce a hardware state the shop could not.
	//
	// Player only, by decision: bots' credits are not test variables yet. Built programmatically, like
	// DevTimeScaleSelector, so DiceGame's .tscn is untouched.
	public partial class DevPlayerCreditsSelector : HBoxContainer
	{
		// The shop's own node id for the player (the same literal SimulationService and NetworkRoot hold privately).
		private const string PlayerNodeId = "player";

		// A ladder rather than a SpinBox: one pick per test phase, and no write per arrow click. Fine at the low end,
		// where each credit changes the bet spacing a lot (100 / credits), coarse above. The top is the rate cap.
		private static readonly int[] Ladder = { 1, 2, 3, 4, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, SimulationService.MaxAutoBetBaseAps };

		private OptionButton _selector;
		private readonly List<int> _items = new();
		private bool _rebuilding;

		public override void _Ready()
		{
			AddThemeConstantOverride("separation", 6);
			TooltipText = "DEV: the player node's hardware credits (1 credit = 1 bet/s at 100X). " +
				"The shipping control is Mining Pools & Hardware.";

			var label = new Label { Text = "DEV:" };
			label.AddThemeFontSizeOverride("font_size", 18);
			AddChild(label);

			_selector = new OptionButton();
			_selector.ItemSelected += OnItemSelected;
			AddChild(_selector);

			Rebuild();
			// A credit change made anywhere else (the shop, a future caller) must show here too.
			HardwareAllocationRepository.HardwareChanged += OnHardwareChanged;
		}

		public override void _ExitTree()
		{
			// A static event: an unsubscribed scene would be kept alive and called after it is freed.
			HardwareAllocationRepository.HardwareChanged -= OnHardwareChanged;
		}

		private void OnHardwareChanged(string nodeId)
		{
			if (nodeId == PlayerNodeId)
			{
				Rebuild();
			}
		}

		// The items are the ladder plus the current total if it is off the ladder (set in the shop one credit at a
		// time, say), so the control always shows the truth rather than the nearest rung.
		private void Rebuild()
		{
			int current = HardwareAllocationRepository.GetNode(PlayerNodeId).TotalCredits;

			_items.Clear();
			_items.AddRange(Ladder);
			if (!_items.Contains(current))
			{
				_items.Add(current);
				_items.Sort();
			}

			_rebuilding = true;
			_selector.Clear();
			foreach (int credits in _items)
			{
				_selector.AddItem(credits.ToString(System.Globalization.CultureInfo.InvariantCulture) + " cr");
			}

			_selector.Select(_items.IndexOf(current));
			_rebuilding = false;
		}

		private void OnItemSelected(long index)
		{
			if (_rebuilding)
			{
				return;
			}

			int target = _items[(int)index];
			int current = HardwareAllocationRepository.GetNode(PlayerNodeId).TotalCredits;
			if (target > current)
			{
				HardwareAllocationRepository.AddCredits(PlayerNodeId, target - current);
			}
			else if (target < current)
			{
				HardwareAllocationRepository.RemoveCredits(PlayerNodeId, current - target);
			}
			// HardwareChanged then rebuilds this control and re-locks DiceGame's APS selector, exactly as a shop
			// purchase does, and SimulationService reads the new rate on its next frame (HardwareRate is read fresh).
		}
	}
}

using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GodotBlockchainPort.Simulation;
#nullable enable

namespace UI.CompanyRows
{
	// Mini-plan 29 unit 1 — ONE source for a company row's player signals: the stake border (what do I own here?), the
	// pending-work state (what must I do here?), the button that state names, and the terminal row of a dissolved
	// company. Extracted unchanged from BlockExplorer's Enroll Mode so that the explorer and the Player Holdings hub
	// draw the same row from the same function (Standing Convention 6: a signal shares its source with what it
	// advertises). Before this, the colours alone existed in three copies (BlockExplorer, CompanyDetails,
	// AuctioningCompanyDetails); those two scenes now read them from here too.
	public static class CompanyRowSignals
	{
		// ND.9b (extended 2026-07-22) — the holding-keyed border colours: gold when the player holds NST, silver PST,
		// black neither. ND.10f (2026-07-23) — the identical three colours also frame each STILL-OPEN auction pool's
		// title, keyed on the PROJECTED stake (what would be minted if the pool closed at the current block).
		public static readonly Color HoldingGold = new(0.85f, 0.65f, 0.13f);
		public static readonly Color HoldingSilver = new(0.75f, 0.75f, 0.78f);
		public static readonly Color HoldingBlack = new(0.05f, 0.05f, 0.05f);

		// ND.10h (2026-07-23, D-ND10h.1) — the founded row's PENDING-WORK colours. A different axis from the
		// stake borders above: those say "what do I own here?", these say "what must I do here?" — a gold
		// border beside a green Claim → reads "you hold NST and have a dividend waiting". They share only the
		// convention that BLACK MEANS NOTHING, which is why the idle button is black-bordered rather than bare.
		public static readonly Color WorkRed = new(1f, 0.3f, 0.3f);      // #FF4D4D — board vote pending (unchanged)
		public static readonly Color WorkGreen = new(0.3f, 0.85f, 0.48f); // #4DD97A — dividends waiting
		// MOCHA (both). NOT an RGB average of the two above — that gives #A6C364, a muddy yellow-green, the
		// opposite of the requested "mixed like pigments". Pigment mixing is SUBTRACTIVE: in CMY,
		// #FF4D4D → (0, .70, .70) and #4DD97A → (.70, .15, .52); summed and clamped → (.70, .85, 1.0) → #4D2600,
		// a deep brown. That is the correct HUE but unreadable as a font colour on a dark theme, so the shipped
		// constant keeps the hue and lifts the value for legibility. Do not "correct" this toward yellow.
		public static readonly Color WorkMocha = new(0.75f, 0.52f, 0.32f); // #C08552

		public static readonly Color ClosedCompanyGrey = new Color(0.55f, 0.55f, 0.55f);

		// The ⚠ prefix stays the dedicated, unambiguous channel for "your ballot is pausing the game" — colour is
		// never the only signal (§22.15).
		public const string VotePendingPrefix = "⚠ BOARD VOTE PENDING — ";

		// ND.10f — the projected-stake → border-colour mapping, shared by the open-auction rows and (by the same
		// three constants) the founded rows: gold NST, silver PST, black nothing.
		public static Color StakeBorderColor(PlayerAuctionStake stake) => stake switch
		{
			PlayerAuctionStake.Nst => HoldingGold,
			PlayerAuctionStake.Pst => HoldingSilver,
			_ => HoldingBlack
		};

		// The player's holding in a founded company, or null when the player holds none (or there is no founding).
		public static CompanyShareHolding? PlayerHolding(CompanyFounding? founding) =>
			founding?.Holdings.FirstOrDefault(h => h.HolderId == "player");

		// ND.9b (2026-07-22) — a holding-keyed coloured border around a founded company's TITLE (same
		// gold/silver/black scheme as CompanyDetails). Independent of the vote-pending red text — both signals
		// coexist (red = "your ballot is blocking play", border = "your stake here").
		public static Color HoldingBorderColor(CompanyFounding? founding)
		{
			CompanyShareHolding? playerHolding = PlayerHolding(founding);
			return playerHolding is { Nst: > 0m } ? HoldingGold
				: playerHolding is { Pst: > 0m } ? HoldingSilver
				: HoldingBlack;
		}

		// ND.10h — the four-state pending-work colour. A pure function of the CURRENT state: voting clears the
		// vote flag and claiming clears the claimable, so nothing about what the player did last visit needs to
		// be remembered (no history record, no persisted state, no event — it is re-evaluated each refresh).
		public static Color PendingWorkColor(bool votePending, bool canClaim) =>
			votePending && canClaim ? WorkMocha
			: votePending ? WorkRed
			: canClaim ? WorkGreen
			: HoldingBlack;

		// D-ND10h.5 — the text carries the state too: a blocking vote outranks a non-blocking claim.
		public static string ActionButtonText(bool votePending, bool canClaim) =>
			votePending ? "Vote →" : canClaim ? "Claim →" : "Details →";

		public static string FoundedRowTitle(string title, bool votePending) =>
			votePending ? VotePendingPrefix + title : title;

		// The whole work state on one row: the button's text, and (D-ND10h.4) label and button sharing the state
		// colour, so the row is scannable without the eye having to land on the button. The idle state keeps the
		// theme's font colour and a black button border.
		public static void ApplyWorkState(Label rowLabel, Button actionButton, bool votePending, bool canClaim)
		{
			actionButton.Text = ActionButtonText(votePending, canClaim);
			Color workColor = PendingWorkColor(votePending, canClaim);
			if (votePending || canClaim)
			{
				rowLabel.AddThemeColorOverride("font_color", workColor);
				actionButton.AddThemeColorOverride("font_color", workColor);
			}

			ApplyButtonBorder(actionButton, workColor);
		}

		// ND.10h (D-ND10h.2) — the state colour as a border on the button itself. A Button has FOUR stylebox
		// states; overriding only "normal" makes the border vanish on hover, which reads as a rendering bug —
		// so all four get their own duplicate. (BuildTitlePanel is the neighbouring precedent but is a
		// PanelContainer wrapper, which a Button cannot reuse without swallowing its own click styling.)
		public static void ApplyButtonBorder(Button button, Color borderColor)
		{
			foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
			{
				StyleBox? existing = button.GetThemeStylebox(state);
				var style = existing?.Duplicate() as StyleBoxFlat ?? new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
				style.BorderColor = borderColor;
				style.BorderWidthLeft = 2;
				style.BorderWidthTop = 2;
				style.BorderWidthRight = 2;
				style.BorderWidthBottom = 2;
				button.AddThemeStyleboxOverride(state, style);
			}
		}

		// ND.10f — so every row kind builds an identical bordered title: a transparent-centre StyleBoxFlat around
		// the label, mouse-transparent so the wheel still reaches the enclosing scroll (Ch. 29).
		public static PanelContainer BuildTitlePanel(Label titleLabel, Color borderColor)
		{
			var titleStyle = new StyleBoxFlat
			{
				BgColor = new Color(0, 0, 0, 0),
				BorderColor = borderColor,
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,
				CornerRadiusTopLeft = 4,
				CornerRadiusTopRight = 4,
				CornerRadiusBottomLeft = 4,
				CornerRadiusBottomRight = 4,
				ContentMarginLeft = 8,
				ContentMarginRight = 8,
				ContentMarginTop = 3,
				ContentMarginBottom = 3
			};
			var titlePanel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
			titlePanel.AddThemeStyleboxOverride("panel", titleStyle);
			titlePanel.AddChild(titleLabel);
			return titlePanel;
		}

		// P15.6d — the FBI warning line's colour: amber while the file grows, red once flagged. The same "colour is never
		// the only signal" rule as §22.15: the warning's text says which state it is.
		public static Color FbiWarningColor(decimal investigationScore) =>
			investigationScore >= NetworkRoot.InvestigationFlagThreshold
				? new Color(1f, 0.4f, 0.4f)
				: new Color(1f, 0.75f, 0.3f);

		// Mini-plan 29 — what the Player Holdings hub's header and the StatusBar badge both count, from ONE pass so
		// the two can never disagree. Held = live companies where the player holds NST or PST (a dissolved company
		// holds nothing left to act on). Waiting = companies with a vote awaiting the player OR a dividend a claim
		// would actually pay (HasPlayerClaimableDividends, the predicate the Claim panel itself uses), each counted
		// once. Cheap enough for every block: no ledger walk, one dictionary pass over the foundings.
		public readonly record struct HoldingsTally(int Held, int VotesWaiting, int ClaimsWaiting, int Waiting);

		public static HoldingsTally TallyPlayerHoldings()
		{
			var awaitingVote = new HashSet<string>(NetworkRoot.GetCompaniesAwaitingPlayerVote().Select(c => c.nonMinerNodeId));
			int held = 0, votes = 0, claims = 0, waiting = 0;
			foreach (CompanyFounding founding in NetworkRoot.GetCompanyFoundings())
			{
				if (PlayerHolding(founding) is not { } holding || (holding.Nst <= 0m && holding.Pst <= 0m))
				{
					continue;
				}

				held++;
				bool votePending = awaitingVote.Contains(founding.NonMinerNodeId);
				bool canClaim = NetworkRoot.HasPlayerClaimableDividendsStatic(founding.NonMinerNodeId);
				if (votePending) votes++;
				if (canClaim) claims++;
				if (votePending || canClaim) waiting++;
			}

			return new HoldingsTally(held, votes, claims, waiting);
		}

		// Step 15 P15.5d (D-15.15) — the terminal row for a dissolved company: WHEN it closed, WHY, and — the
		// part that matters to the player — what they lost with it. Deliberately has NO action button: the
		// tokens are destroyed and there is nothing left to vote on or claim (§39.12).
		public static Control BuildClosedCompanyRow(string companyDescription, CompanyClosure? closure)
		{
			string when = closure == null
				? string.Empty
				: $" on {DateTimeOffset.FromUnixTimeMilliseconds(closure.ClosedAtUnixMs).LocalDateTime:yyyy-MM-dd}";
			string why = closure?.Reason == NetworkRoot.ClosureReasonFbiSeizure ? "seized by the FBI" : "closed — debt default";

			string lost = string.Empty;
			if (closure is { PlayerNstAtClosure: > 0m } or { PlayerPstAtClosure: > 0m })
			{
				string kind = closure.PlayerNstAtClosure > 0m ? "NST" : "PST";
				decimal amount = closure.PlayerNstAtClosure > 0m ? closure.PlayerNstAtClosure : closure.PlayerPstAtClosure;
				lost = string.Create(CultureInfo.InvariantCulture, $"   |   you lost {amount:N0} {kind}");
				if (closure.PlayerUnclaimedBtcAtClosure > 0m || closure.PlayerUnclaimedScAtClosure > 0m)
				{
					lost += string.Create(CultureInfo.InvariantCulture,
						$" + {closure.PlayerUnclaimedBtcAtClosure:N8} BTC / {closure.PlayerUnclaimedScAtClosure:N2} SC unclaimed");
				}
			}

			var label = new Label
			{
				Text = $"✗ {companyDescription}  | {why}{when}{lost}",
				MouseFilter = Control.MouseFilterEnum.Pass
			};
			label.AddThemeColorOverride("font_color", ClosedCompanyGrey);
			// Black border: a dissolved company is a stake you no longer hold (§22.15's vocabulary).
			return BuildTitlePanel(label, HoldingBlack);
		}
	}
}

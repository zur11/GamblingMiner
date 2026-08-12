using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Scripts.Betting
{
	public sealed class SavedBettingStrategyRepository
	{
		private readonly string _statePath;
		private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
		private readonly List<SavedBettingStrategy> _strategies = new();

		public SavedBettingStrategyRepository(string statePath)
		{
			_statePath = statePath;
			Load();
		}

		public bool HasAny => _strategies.Count > 0;

		public bool HasAnyForGame(string gameId)
		{
			return _strategies.Any(s =>
				string.Equals(s.GameId, gameId, StringComparison.OrdinalIgnoreCase));
		}

		public void Save(SavedBettingStrategy strategy)
		{
			if (strategy == null || string.IsNullOrWhiteSpace(strategy.Name))
			{
				return;
			}

			string normalizedName = strategy.Name.Trim();
			SavedBettingStrategy stored = Clone(strategy);
			stored.Name = normalizedName;
			stored.SavedAtUtc = DateTime.UtcNow;

			int existingIndex = _strategies.FindIndex(s =>
				string.Equals(s.Name, normalizedName, StringComparison.OrdinalIgnoreCase) &&
				string.Equals(s.GameId, stored.GameId, StringComparison.OrdinalIgnoreCase));

			if (existingIndex >= 0)
			{
				_strategies[existingIndex] = stored;
			}
			else
			{
				_strategies.Add(stored);
			}

			Persist();
		}

		public bool TryGet(string gameId, string name, out SavedBettingStrategy strategy)
		{
			strategy = null;
			if (string.IsNullOrWhiteSpace(gameId))
			{
				return false;
			}

			IEnumerable<SavedBettingStrategy> matches = _strategies
				.Where(s => string.Equals(s.GameId, gameId, StringComparison.OrdinalIgnoreCase));

			if (!string.IsNullOrWhiteSpace(name))
			{
				string normalizedName = name.Trim();
				strategy = matches.FirstOrDefault(s =>
					string.Equals(s.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
			}
			else
			{
				strategy = matches
					.OrderByDescending(s => s.SavedAtUtc)
					.FirstOrDefault();
			}

			if (strategy == null)
			{
				return false;
			}

			strategy = Clone(strategy);
			return true;
		}

		private void Load()
		{
			if (!FileAccess.FileExists(_statePath))
			{
				return;
			}

			try
			{
				using FileAccess file = FileAccess.Open(_statePath, FileAccess.ModeFlags.Read);
				string json = file.GetAsText();
				List<SavedBettingStrategy> loaded = JsonSerializer.Deserialize<List<SavedBettingStrategy>>(json, _jsonOptions);
				if (loaded == null)
				{
					return;
				}

				_strategies.Clear();
				foreach (SavedBettingStrategy strategy in loaded)
				{
					if (strategy == null ||
						string.IsNullOrWhiteSpace(strategy.Name) ||
						string.IsNullOrWhiteSpace(strategy.GameId) ||
						strategy.Config == null)
					{
						continue;
					}

					_strategies.Add(Clone(strategy));
				}
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[SavedBettingStrategyRepository] Load failed: {ex.Message}");
			}
		}

		private void Persist()
		{
			try
			{
				using FileAccess file = FileAccess.Open(_statePath, FileAccess.ModeFlags.Write);
				file.StoreString(JsonSerializer.Serialize(_strategies, _jsonOptions));
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[SavedBettingStrategyRepository] Save failed: {ex.Message}");
			}
		}

		private static SavedBettingStrategy Clone(SavedBettingStrategy strategy)
		{
			return new SavedBettingStrategy
			{
				Name = strategy.Name ?? string.Empty,
				GameId = strategy.GameId ?? string.Empty,
				SavedAtUtc = DateTime.SpecifyKind(strategy.SavedAtUtc, DateTimeKind.Utc),
				Config = new BettingStrategyConfig
				{
					BaseBet = strategy.Config?.BaseBet ?? 0m,
					// Strategies saved before the progression percents were split carry the retired
					// IncreasePercent/IncreaseOnLoss/IncreaseOnWin trio, which deserializes as absent ⇒ both
					// percents 0 ⇒ flat betting. Re-save such a strategy to restore its progression (mini-plan
					// 01, D-M1.5: no migration — this is a personal file, not world state).
					IncreaseOnLossPercent = strategy.Config?.IncreaseOnLossPercent ?? 0m,
					IncreaseOnWinPercent = strategy.Config?.IncreaseOnWinPercent ?? 0m,
					StopOnProfit = strategy.Config?.StopOnProfit,
					StopOnLoss = strategy.Config?.StopOnLoss,
					StopOnBlockMined = strategy.Config?.StopOnBlockMined ?? false,
					// Strategies saved before the stops were split carry neither flag, so they load with both
					// Insist switches OFF. No migration by design (mini-plan 01, D-M1.5): this is a personal
					// file, not world state.
					InsistAfterStopOnProfit = strategy.Config?.InsistAfterStopOnProfit ?? false,
					InsistAfterStopOnLoss = strategy.Config?.InsistAfterStopOnLoss ?? false
				},
				NumberOfBets = Math.Max(0, strategy.NumberOfBets),
				WinningChance = Math.Clamp(strategy.WinningChance, 1, 95),
				BetHigh = strategy.BetHigh,
				BetsPerSecond = Math.Max(1, strategy.BetsPerSecond)
			};
		}
	}
}

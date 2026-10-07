using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
#nullable enable

namespace GodotBlockchainPort.Blockchain;

public sealed class BlockchainService
{
	public const string CoinbaseSender = "00";

	// Mini-plan 13 A — DIAGNOSTIC ONLY: which node owns this service, so the block-cost trace can report how
	// many DISTINCT nodes replayed the chain after a block. Set by NodeAgent; never read by game logic. The
	// answer is what separates T4.1 (make the rebuild incremental) from T4.2 (stop keeping ~62 copies of it).
	public string OwnerNodeIdForDiagnostics { get; set; } = string.Empty;

	// ── Difficulty (Difficulty Regulator, D.1) ────────────────────────────────────────────────────
	// Difficulty is a CONTINUOUS value = expected nonce attempts per block (probability 1/Difficulty that
	// a block-header hash meets the target). A hash meets target when, read as a 256-bit integer H,
	// H ≤ 2²⁵⁶ / Difficulty. The difficulty in effect for each block is stored on the block (Block.Difficulty)
	// so the chain validates without replaying retargets from genesis (D.1 is representation-only; the LWMA
	// retarget arrives in D.2).
	//
	// InitialDifficulty = the legacy effective difficulty (the old "00" prefix + next-hex ≤ '6' rule had
	// success probability (1/16²)·(7/16) = 7/4096, i.e. 4096/7 ≈ 585.14 expected attempts) — seeding this
	// keeps the block pace unchanged until the regulator runs. Target block pace: 58,500 in-game sec/block.
	public const double InitialDifficulty = 4096d / 7d; // ≈ 585.14

	// ── LWMA retarget (D.2) ───────────────────────────────────────────────────────────────────────
	// The difficulty is nudged every block so the average *in-game* time between blocks stays near
	// TargetBlockSeconds. We use an LWMA (Linear Weighted Moving Average) of the last LwmaWindow block
	// solvetimes — recent blocks weighted more — then nextDifficulty = current × (Target / lwmaSolvetime),
	// clamped per step. Because in-game time is bet-driven and a block needs ≈Difficulty attempts, more
	// total mining (more bots / faster hardware) makes blocks arrive in fewer in-game seconds → solvetime
	// dips below target → difficulty rises (and vice-versa). Block time is the only signal (no hashrate term).
	public const double TargetBlockSeconds = 58_500d; // matches the 100X bootstrap pace (≈16h40m/block)
	public const int LwmaWindow = 20;
	public const double MaxStepUp = 2.0d;             // difficulty can at most double per block…
	// R2-D: superseded as the trim floor by MinFeedbackTrim (0.25) below — kept as the documented historical
	// value and because it is the symmetric partner of MaxStepUp in the anti-oscillation vocabulary. Nothing
	// reads it now; do not re-wire it into the clamp without re-reading §R2.7's cede-fast/rise-slow argument.
	public const double MaxStepDown = 0.5d;
	public const double MinDifficulty = 1.0d;         // floor (1 expected attempt = every hash passes)
	// Easing (D.2, Option A): fraction of the gap to the target closed each block, so a power change ramps
	// in over a few blocks instead of snapping in one. 1.0 = instant; 0.7 ≈ ~97% closed in 3 blocks (tuned by test).
	public const double DifficultyEaseAlpha = 0.7d;

	// R2-D (2026-07-27, btc-pools-hardware-plan.md §R2.7) — ASYMMETRIC response. F2 proposed this in
	// 2026-06 and it was dropped because a 10→1 power step already ceded in ~3 blocks; the case that
	// revives it is a 30× OVERHANG (difficulty 217,833 against an anchor of 51,946 after a founder power
	// spike collapsed), where the symmetric response needed 4-5 blocks — each of them itself 2-7× slow.
	//
	// The two directions are NOT equally dangerous, so they should not share constants:
	//   • difficulty too HIGH  → blocks run slow. Annoying, visible, self-correcting.
	//   • difficulty too LOW   → blocks FLOOD: pacing breaks and coinbase is minted early, which is
	//                            economically irreversible.
	// So: cede fast, take on slowly. MinFeedbackTrim 0.5 → 0.25 lets the LWMA ask for a 4× cut in one
	// block (the ceiling stays 2× — nothing may double difficulty faster than before), and the downward
	// easing closes 90% of the gap instead of 70%. Verified on the measured post-spike state: the unwind
	// becomes 217,833 → 33,471 → 15,034, i.e. ≤2 blocks.
	public const double MinFeedbackTrim = 0.25d;
	public const double DifficultyEaseAlphaDown = 0.9d;

	// 2²⁵⁶ — the space of a 256-bit (64-hex) double-SHA256 hash. Acceptance threshold = MaxHash256 / Difficulty.
	private static readonly BigInteger MaxHash256 = BigInteger.Pow(2, 256);
	// Historical reference only — Satoshi's real base58 genesis address. NOT used for payouts.
	// It is the initial placeholder recipient on the genesis/bootstrap coinbase; NetworkRoot
	// rewrites that recipient to Satoshi's derived gm1q… address once the founder wallet exists.
	public const string SatoshiAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
	public const string GenesisHeadline = "The Times 03/Jan/2009 Chancellor on brink of second bailout for banks.";
	public const string BootstrapSecondBlockTxId = "bootstrap-satoshi-second-block-50btc";
	public static readonly long GenesisTimestampUnixMs =
		TimelineConfig.Shift(new DateTimeOffset(2009, 1, 3, 18, 15, 5, TimeSpan.Zero)).ToUnixTimeMilliseconds();

	public List<Block> Chain { get; } = new();
	public List<Transaction> PendingTransactions { get; } = new();

	// ── UTXO set (Step 8 full model, plan Appendix A) ──────────────────────────────────────────────
	// The set of unspent transaction outputs, rebuilt by replaying the chain (because "a block is the only
	// commit to disk"). Cached and invalidated whenever the chain mutates (_chainVersion). Key = "txid:vout".
	private sealed class UtxoEntry
	{
		public required string TxId;
		public required int Vout;
		public required TxOutput Output;
		public required int BlockIndex;
		public required bool IsCoinbase;
		public required bool IsSpendable;
	}
	private Dictionary<string, UtxoEntry>? _utxoCache;

	// Mini-plan 17 B2 (T4.3) — address → the outpoint keys it owns. Maintained in lockstep with _utxoCache and
	// sharing its version, so the two can never disagree about which version of the chain they describe.
	// GetSpendableUtxos used to walk the WHOLE set and filter by owned address, making every wallet panel, bot
	// affordability check and treasury read O(all UTXOs) no matter how little that node owns. This is the
	// follow-on §38.7's R3 fix pointed at: that took AggregateSpendable from O(addresses × utxos) to O(utxos),
	// and this takes it to O(owned).
	private Dictionary<string, HashSet<string>>? _addressIndex;
	private int _utxoCacheVersion = -1;
	private int _chainVersion;
	private static string OutPointKey(string txId, int vout) => $"{txId}:{vout}";

	private static Dictionary<string, HashSet<string>> BuildAddressIndex(Dictionary<string, UtxoEntry> utxos)
	{
		var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, UtxoEntry> pair in utxos)
		{
			string address = pair.Value.Output.Address ?? string.Empty;
			if (!index.TryGetValue(address, out HashSet<string>? keys))
			{
				keys = new HashSet<string>(StringComparer.Ordinal);
				index[address] = keys;
			}

			keys.Add(pair.Key);
		}

		return index;
	}

	public BlockchainService()
	{
		Block genesis = CreateNewBlock(100, "0", "0", GenesisTimestampUnixMs, "0");
		genesis.Transactions = new List<Transaction> { CreateGenesisCoinbase() };
		genesis.MerkleRoot = MerkleTree.ComputeRoot(genesis.Transactions);
		genesis.Difficulty = InitialDifficulty; // genesis is exempt from PoW validation; set for consistency
	}

	public static string TextToHex(string text)
	{
		return Convert.ToHexString(Encoding.ASCII.GetBytes(text)).ToLowerInvariant();
	}

	public static Transaction CreateGenesisCoinbase()
	{
		return new Transaction
		{
			// Input-less coinbase: one 50-BTC output to Satoshi (rewritten to his derived gm1q… address by
			// NetworkRoot.NormalizeGenesisAcrossNodes). IsSpendable = false → unspendable forever.
			Inputs = new List<TxInput>(),
			Outputs = new List<TxOutput> { new() { Address = SatoshiAddress, Amount = 50m } },
			TransactionId = "genesis-coinbase",
			InputDataText = GenesisHeadline,
			InputDataHex = TextToHex(GenesisHeadline),
			IsSpendable = false
		};
	}

	// Builds an input-less coinbase output (reward + Σfees) to the miner. BIP34-style height in the Salt
	// keeps each coinbase txid unique even at equal reward (Step 4b.3).
	public static Transaction CreateCoinbase(string minerAddress, decimal amount, int blockIndex)
	{
		var coinbase = new Transaction
		{
			Inputs = new List<TxInput>(),
			Outputs = new List<TxOutput> { new() { Address = minerAddress, Amount = amount } },
			Salt = $"coinbase:{blockIndex}",
			IsSpendable = true
		};
		coinbase.TransactionId = ComputeTransactionId(coinbase);
		return coinbase;
	}

	public Block CreateNewBlock(long nonce, string previousBlockHash, string hash, long timestamp, string merkleRoot)
	{
		Block newBlock = new()
		{
			Index = Chain.Count + 1,
			Timestamp = timestamp,
			Transactions = PendingTransactions.ToList(),
			Nonce = nonce,
			Hash = hash,
			PreviousBlockHash = previousBlockHash,
			MerkleRoot = merkleRoot
		};

		PendingTransactions.Clear();
		Chain.Add(newBlock);
		_chainVersion++;
		AdvanceUtxoCacheWithAppendedBlock(newBlock); // mini-plan 17 B1
		return newBlock;
	}

	// Step 4b: commit a mined block built from a BlockTemplate. Unlike CreateNewBlock (used only for
	// genesis), the coinbase is already inside template.BlockTransactions, and only the transactions
	// actually included are removed from the mempool (unselected ones remain pending).
	public Block CommitBlock(long nonce, string previousBlockHash, string hash, long timestamp, BlockTemplate template, double difficulty)
	{
		Block newBlock = new()
		{
			Index = Chain.Count + 1,
			Timestamp = timestamp,
			Transactions = template.BlockTransactions,
			Nonce = nonce,
			Hash = hash,
			PreviousBlockHash = previousBlockHash,
			MerkleRoot = template.MerkleRoot,
			Difficulty = difficulty
		};

		foreach (Transaction included in template.SelectedMempoolTxs)
		{
			PendingTransactions.Remove(included);
		}

		Chain.Add(newBlock);
		_chainVersion++;
		AdvanceUtxoCacheWithAppendedBlock(newBlock); // mini-plan 17 B1
		return newBlock;
	}

	public Block GetLastBlock() => Chain[^1];

	// Builds an unsigned transaction from explicit inputs + outputs (Step 8 full UTXO model). The caller
	// (NodeAgent) fills in each input's signature/keys and sets the txid. A random Salt keeps the content
	// hash unique; pass a deterministic salt for scripted historical events.
	public static Transaction CreateUnsignedTransaction(List<TxInput> inputs, List<TxOutput> outputs, decimal fee, string? salt = null)
	{
		return new Transaction
		{
			Inputs = inputs,
			Outputs = outputs,
			Fee = fee,
			Salt = salt ?? Guid.NewGuid().ToString("N"),
			TransactionId = string.Empty
		};
	}

	// Step 4b.3 (OQ-C6) / Step 8 (A.6): a transaction's id is the double-SHA256 of its canonical content —
	// the input outpoints, the output (address, amount) pairs, the fee, input data, spendability and the
	// uniqueness Salt. Excludes the signatures, so it is a true fingerprint of *what* the tx does. Also the
	// Merkle leaf, so any reshuffle of inputs/outputs changes the root and invalidates the block.
	public static string ComputeTransactionId(Transaction tx)
	{
		string inputs = string.Join(",", tx.Inputs.Select(i => $"{i.Source.PrevTxId}:{i.Source.Vout}"));
		string outputs = string.Join(",", tx.Outputs.Select(o => $"{o.Address}:{o.Amount.ToString(CultureInfo.InvariantCulture)}"));
		string content = string.Join("|", new[]
		{
			"in:" + inputs,
			"out:" + outputs,
			tx.Fee.ToString(CultureInfo.InvariantCulture),
			tx.InputDataHex,
			tx.IsSpendable ? "1" : "0",
			tx.Salt
		});
		return CryptoUtils.Sha256Hex(CryptoUtils.Sha256Hex(content));
	}

	// The per-input signed message (sighash). The txid already commits to every input/output/fee/salt
	// (ComputeTransactionId), so signing it makes the whole tx tamper-evident and binds each input to this
	// exact set of inputs/outputs — they cannot be reshuffled without breaking every signature.
	public static string BuildTransactionPayload(Transaction tx) => tx.TransactionId;

	// Step 8 (A.4) — per-input validation: txid integrity + for EVERY input an ownership proof (the input's
	// secp256k1 key derives the address recorded on it) and a P-256 signature over the tx sighash. A coinbase
	// (no inputs) is signature-valid by definition. Enables inputs across several owned addresses.
	public bool ValidateTransactionSignature(Transaction tx)
	{
		if (tx.IsCoinbase)
		{
			return true;
		}

		// Integrity: the txid must be the content hash of the transaction (Step 4b.3 / A.6).
		if (!string.Equals(tx.TransactionId, ComputeTransactionId(tx), StringComparison.Ordinal))
		{
			return false;
		}

		string payload = BuildTransactionPayload(tx);
		foreach (TxInput input in tx.Inputs)
		{
			if (string.IsNullOrWhiteSpace(input.PublicKeyBase64) ||
				string.IsNullOrWhiteSpace(input.SignatureBase64) ||
				string.IsNullOrWhiteSpace(input.Secp256k1PublicKeyBase64))
			{
				return false;
			}

			// Ownership: secp256k1 public key → Hash160 → Bech32 must match the input's recorded address.
			if (!string.Equals(input.Address, CryptoUtils.DeriveAddressFromPublicKey(input.Secp256k1PublicKeyBase64), StringComparison.Ordinal))
			{
				return false;
			}

			// Signature: P-256 signing key (game-internal, not visible on the address) over the sighash.
			if (!CryptoUtils.Verify(payload, input.SignatureBase64, input.PublicKeyBase64))
			{
				return false;
			}
		}

		return true;
	}

	// Step 8 (A.4) — admit a spend to the mempool under UTXO rules: every input references a confirmed,
	// unspent, mature (if coinbase), non-double-spent output it owns; Σinputs ≥ Σoutputs; and the declared
	// Fee equals Σinputs − Σoutputs. Coinbases never enter here (they are created inside the block template).
	public bool AddTransactionToPendingTransactions(Transaction transaction)
	{
		if (transaction.IsCoinbase || transaction.Inputs.Count == 0)
		{
			return false;
		}
		if (transaction.Outputs.Count == 0 || transaction.Outputs.Any(o => o.Amount <= 0m))
		{
			return false;
		}
		if (ContainsTransactionId(transaction.TransactionId))
		{
			return false;
		}
		if (!ValidateTransactionSignature(transaction))
		{
			return false;
		}

		Dictionary<string, UtxoEntry> utxos = GetUtxoSet();
		HashSet<string> spentByPending = CollectPendingSpentOutpoints();
		int tipIndex = Chain.Count > 0 ? Chain[^1].Index : 0;

		decimal inputSum = 0m;
		var seenInThisTx = new HashSet<string>();
		foreach (TxInput input in transaction.Inputs)
		{
			string key = OutPointKey(input.Source.PrevTxId, input.Source.Vout);
			if (!seenInThisTx.Add(key)) return false;                       // duplicate input inside the tx
			if (!utxos.TryGetValue(key, out UtxoEntry? utxo)) return false; // unknown or already-spent output
			if (!utxo.IsSpendable) return false;                           // genesis (unspendable)
			if (utxo.IsCoinbase && (tipIndex - utxo.BlockIndex) < CoinbaseMaturity) return false; // immature
			if (spentByPending.Contains(key)) return false;                // double-spend vs the mempool
			if (!string.Equals(utxo.Output.Address, input.Address, StringComparison.Ordinal)) return false;
			inputSum += utxo.Output.Amount;
		}

		decimal outputSum = transaction.TotalOutput;
		if (inputSum < outputSum) return false;
		if (transaction.Fee != inputSum - outputSum) return false; // fee must equal the in/out delta exactly

		PendingTransactions.Add(transaction);
		return true;
	}

	// Step 13 (SW.1 hardening) — membership probe: is this output still unspent on the CONFIRMED chain?
	// (Maturity/pending state is ignored — this asks only "does the coin still exist".)
	public bool IsUnspentOutput(string txId, int vout) =>
		GetUtxoSet().ContainsKey(OutPointKey(txId, vout));

	// Every outpoint consumed by a pending mempool transaction (for the double-spend guard).
	private HashSet<string> CollectPendingSpentOutpoints()
	{
		var spent = new HashSet<string>();
		foreach (Transaction pending in PendingTransactions)
			foreach (TxInput input in pending.Inputs)
				spent.Add(OutPointKey(input.Source.PrevTxId, input.Source.Vout));
		return spent;
	}

	// The confirmed UTXO set, rebuilt by replaying the chain oldest→newest and cached until the chain
	// mutates (Step 8 / A.3). Key = "txid:vout". Coinbase maturity and spendability are read from each entry.
	private Dictionary<string, UtxoEntry> GetUtxoSet()
	{
		if (_utxoCache != null && _utxoCacheVersion == _chainVersion)
		{
			return _utxoCache;
		}

		// Mini-plan 13 A — this replay is the plan's leading suspect for per-block cost: it is O(all
		// transactions ever), and a block invalidates the cache of every node that holds one of these.
		long rebuildStart = System.Diagnostics.Stopwatch.GetTimestamp();

		var utxos = new Dictionary<string, UtxoEntry>();
		foreach (Block block in Chain)
		{
			foreach (Transaction tx in block.Transactions)
			{
				foreach (TxInput input in tx.Inputs)
					utxos.Remove(OutPointKey(input.Source.PrevTxId, input.Source.Vout));

				for (int v = 0; v < tx.Outputs.Count; v++)
				{
					string key = OutPointKey(tx.TransactionId, v);
					utxos[key] = new UtxoEntry
					{
						TxId = tx.TransactionId,
						Vout = v,
						Output = tx.Outputs[v],
						BlockIndex = block.Index,
						IsCoinbase = tx.IsCoinbase,
						IsSpendable = tx.IsSpendable
					};
				}
			}
		}

		_utxoCache = utxos;
		// Mini-plan 17 B2 — derived from the FINISHED set in one extra pass rather than maintained inside the
		// replay loop above. The loop removes spent outputs as it goes, so keeping the index in step with it
		// would duplicate the remove-from-index logic in the one place that does not need it; a single pass
		// over the final set cannot disagree with the set it was built from.
		_addressIndex = BuildAddressIndex(utxos);
		_utxoCacheVersion = _chainVersion;
		Scripts.Diagnostics.BlockCostProfiler.NoteUtxoRebuild(
			OwnerNodeIdForDiagnostics,
			(System.Diagnostics.Stopwatch.GetTimestamp() - rebuildStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
		return utxos;
	}

	/// <summary>
	/// Mini-plan 17 B1 (T4.1) — advance the cached UTXO set by ONE appended block instead of replaying the
	/// whole chain. Called immediately after every <c>Chain.Add</c> + <c>_chainVersion++</c> pair.
	///
	/// <para><b>Fail-safe by construction.</b> It advances the cache only when the cache was <i>exactly</i>
	/// current as of the version before this append (<c>_utxoCacheVersion == _chainVersion - 1</c>). In every
	/// other state — no cache yet, or a cache already stale for some other reason — it does nothing, and
	/// <see cref="GetUtxoSet"/>'s version check then rebuilds by replay, which is always correct. So the only
	/// way to end up with a wrong set is an arithmetic bug in the loop below, which is precisely what
	/// <see cref="DescribeUtxoCacheMismatch"/> exists to catch.</para>
	///
	/// <para><b>The order inside matters and mirrors the replay exactly:</b> per transaction, inputs are
	/// removed before outputs are added, and transactions are processed in list order. That is what makes a
	/// transaction spending an output created earlier in the SAME block come out right.</para>
	///
	/// <para><c>TryReplaceChain</c> deliberately does NOT call this: a wholesale swap is not an append, and
	/// its version bump leaves the cache stale so the next read replays. The replay stays for that case and
	/// for the oracle.</para>
	/// </summary>
	private void AdvanceUtxoCacheWithAppendedBlock(Block appended)
	{
		if (_utxoCache == null || _utxoCacheVersion != _chainVersion - 1)
		{
			return;
		}

		foreach (Transaction tx in appended.Transactions)
		{
			foreach (TxInput input in tx.Inputs)
			{
				string spentKey = OutPointKey(input.Source.PrevTxId, input.Source.Vout);

				// Mini-plan 17 B2 — the address must be read BEFORE the entry is removed, or there is nothing
				// left to say which address's index entry to clean up. An index that keeps a key whose UTXO is
				// gone reports money that has been spent.
				if (_addressIndex != null && _utxoCache.TryGetValue(spentKey, out UtxoEntry? spent) && spent != null)
				{
					string spentAddress = spent.Output.Address ?? string.Empty;
					if (_addressIndex.TryGetValue(spentAddress, out HashSet<string>? spentKeys))
					{
						spentKeys.Remove(spentKey);
						if (spentKeys.Count == 0) _addressIndex.Remove(spentAddress);
					}
				}

				_utxoCache.Remove(spentKey);
			}

			for (int v = 0; v < tx.Outputs.Count; v++)
			{
				string key = OutPointKey(tx.TransactionId, v);
				_utxoCache[key] = new UtxoEntry
				{
					TxId = tx.TransactionId,
					Vout = v,
					Output = tx.Outputs[v],
					BlockIndex = appended.Index,
					IsCoinbase = tx.IsCoinbase,
					IsSpendable = tx.IsSpendable
				};

				if (_addressIndex != null)
				{
					string address = tx.Outputs[v].Address ?? string.Empty;
					if (!_addressIndex.TryGetValue(address, out HashSet<string>? keys))
					{
						keys = new HashSet<string>(StringComparer.Ordinal);
						_addressIndex[address] = keys;
					}

					keys.Add(key);
				}
			}
		}

		_utxoCacheVersion = _chainVersion;
	}

	/// <summary>
	/// Mini-plan 17 A3 — THE ORACLE. Replays the whole chain into a fresh set and compares it, entry by entry,
	/// against whatever <see cref="GetUtxoSet"/> currently holds. Returns null when they agree, or a description
	/// of the first disagreements when they do not.
	///
	/// <para><b>Written BEFORE the incremental update (B1) exists, deliberately.</b> Today the cache is itself
	/// produced by a replay, so this passes trivially — and that is the point: the comparison is in place and
	/// proven to run before there is any result it could be shaped to fit. Once B1 maintains the set
	/// incrementally, this is the only thing that can prove the fast path right.</para>
	///
	/// <para><b>The full replay is never deleted</b> (plan §4). It is not dead code once B1 lands — it is what
	/// <c>TryReplaceChain</c> needs and what this method needs. A wrong UTXO set is a wrong balance, which this
	/// project treats as unrecoverable (INC-001/INC-004).</para>
	/// </summary>
	public string? DescribeUtxoCacheMismatch()
	{
		if (_utxoCache == null)
		{
			return null; // nothing cached yet — nothing to disagree with.
		}

		Dictionary<string, UtxoEntry> cached = _utxoCache;
		var replayed = new Dictionary<string, UtxoEntry>();

		foreach (Block block in Chain)
		{
			foreach (Transaction tx in block.Transactions)
			{
				foreach (TxInput input in tx.Inputs)
					replayed.Remove(OutPointKey(input.Source.PrevTxId, input.Source.Vout));

				for (int v = 0; v < tx.Outputs.Count; v++)
				{
					replayed[OutPointKey(tx.TransactionId, v)] = new UtxoEntry
					{
						TxId = tx.TransactionId,
						Vout = v,
						Output = tx.Outputs[v],
						BlockIndex = block.Index,
						IsCoinbase = tx.IsCoinbase,
						IsSpendable = tx.IsSpendable
					};
				}
			}
		}

		if (cached.Count == replayed.Count)
		{
			bool identical = true;
			foreach (KeyValuePair<string, UtxoEntry> pair in replayed)
			{
				if (!cached.TryGetValue(pair.Key, out UtxoEntry? mine) || mine == null || !SameUtxo(mine, pair.Value))
				{
					identical = false;
					break;
				}
			}

			if (identical)
			{
				// Mini-plan 17 B2 — the index is checked too, and only once the SET agrees, so a reported
				// index fault is never just an echo of a set fault. Derived from the cached set rather than
				// from the replay: the question is whether the index describes the set it is paired with.
				return DescribeAddressIndexMismatch(cached);
			}
		}

		// Report the SHAPE of the disagreement, not just that there is one: which side holds what, and a few
		// example keys. "The sets differ" is an alarm; this is a diagnosis.
		var missing = new List<string>();
		var extra = new List<string>();
		var differing = new List<string>();

		foreach (KeyValuePair<string, UtxoEntry> pair in replayed)
		{
			if (!cached.TryGetValue(pair.Key, out UtxoEntry? mine) || mine == null)
			{
				if (missing.Count < 3) missing.Add(pair.Key);
			}
			else if (!SameUtxo(mine, pair.Value) && differing.Count < 3)
			{
				differing.Add(pair.Key);
			}
		}

		foreach (string key in cached.Keys)
		{
			if (!replayed.ContainsKey(key) && extra.Count < 3) extra.Add(key);
		}

		return $"cached={cached.Count} replayed={replayed.Count}"
			+ $" · missing-from-cache={missing.Count switch { 0 => "none", _ => string.Join(",", missing) }}"
			+ $" · extra-in-cache={extra.Count switch { 0 => "none", _ => string.Join(",", extra) }}"
			+ $" · differing={differing.Count switch { 0 => "none", _ => string.Join(",", differing) }}";
	}

	/// <summary>
	/// Mini-plan 17 B2's half of the oracle: does <see cref="_addressIndex"/> describe exactly the set it is
	/// paired with? Returns null when it does. The two failure directions are reported separately because they
	/// mean opposite things: a **missing** key hides money the node owns, a **stale** key reports money it has
	/// already spent — and only the second one can make a bot bid coins it does not have.
	/// </summary>
	private string? DescribeAddressIndexMismatch(Dictionary<string, UtxoEntry> set)
	{
		if (_addressIndex == null)
		{
			return set.Count == 0 ? null : $"address index absent while the set holds {set.Count} entry(ies)";
		}

		Dictionary<string, HashSet<string>> expected = BuildAddressIndex(set);

		int missingKeys = 0;
		int staleKeys = 0;
		string firstMissing = string.Empty;
		string firstStale = string.Empty;

		foreach (KeyValuePair<string, HashSet<string>> pair in expected)
		{
			_addressIndex.TryGetValue(pair.Key, out HashSet<string>? actual);
			foreach (string key in pair.Value)
			{
				if (actual == null || !actual.Contains(key))
				{
					if (missingKeys++ == 0) firstMissing = $"{pair.Key}→{key}";
				}
			}
		}

		foreach (KeyValuePair<string, HashSet<string>> pair in _addressIndex)
		{
			expected.TryGetValue(pair.Key, out HashSet<string>? want);
			foreach (string key in pair.Value)
			{
				if (want == null || !want.Contains(key))
				{
					if (staleKeys++ == 0) firstStale = $"{pair.Key}→{key}";
				}
			}
		}

		if (missingKeys == 0 && staleKeys == 0)
		{
			return null;
		}

		return $"address index disagrees with its own set: missing={missingKeys}"
			+ (missingKeys > 0 ? $" (first {firstMissing})" : string.Empty)
			+ $" · stale={staleKeys}"
			+ (staleKeys > 0 ? $" (first {firstStale})" : string.Empty)
			+ $" · addresses indexed={_addressIndex.Count} expected={expected.Count}";
	}

	private static bool SameUtxo(UtxoEntry a, UtxoEntry b) =>
		a.TxId == b.TxId
		&& a.Vout == b.Vout
		&& a.BlockIndex == b.BlockIndex
		&& a.IsCoinbase == b.IsCoinbase
		&& a.IsSpendable == b.IsSpendable
		&& a.Output.Amount == b.Output.Amount
		&& a.Output.Address == b.Output.Address;

	public bool ContainsTransactionId(string transactionId)
	{
		if (PendingTransactions.Any(t => t.TransactionId == transactionId))
		{
			return true;
		}

		return Chain.Any(b => b.Transactions.Any(t => t.TransactionId == transactionId));
	}

	// Step 4: hash a compact block header (prevHash + merkleRoot + timestamp + nonce) via
	// double-SHA256, like Bitcoin — instead of re-serialising the whole transaction list per nonce.
	// The Merkle root commits to every transaction, so this still binds the block contents.
	public string HashHeader(string previousBlockHash, string merkleRoot, long timestamp, long nonce)
	{
		string header = $"{previousBlockHash}|{merkleRoot}|{timestamp}|{nonce}";
		return CryptoUtils.Sha256Hex(CryptoUtils.Sha256Hex(header));
	}

	public long ProofOfWork(string previousBlockHash, string merkleRoot, long timestamp, double difficulty)
	{
		long nonce = 0;
		string hash = HashHeader(previousBlockHash, merkleRoot, timestamp, nonce);
		while (!IsHashAtTargetDifficulty(hash, difficulty))
		{
			nonce++;
			hash = HashHeader(previousBlockHash, merkleRoot, timestamp, nonce);
		}

		return nonce;
	}

	// Difficulty to mine the NEXT block (D.2: HYBRID regulator). Computed from the EXISTING chain only (the
	// block being mined isn't timestamped yet), so it's stable across a candidate's nonce rolls. O(LwmaWindow).
	//
	//   target = anchor × feedbackTrim ;  nextDifficulty = current + DifficultyEaseAlpha × (target − current)
	//
	// • anchor — feed-forward from the KNOWN total mining power. In-game time runs at clock-speed × real time
	//   and a block needs ≈Difficulty attempts, so equilibrium difficulty = (TargetBlockSeconds / clockSpeed) ×
	//   power = InitialDifficulty × power (baseline: 1 bet/sec ↔ InitialDifficulty). This is the *correct level*
	//   for the current power; when a miner joins/leaves or hardware changes the target updates at once. When
	//   power is unknown (0: historical bootstrap / idle), hold at the current difficulty (feedback-only).
	// • feedbackTrim = LWMA(TargetBlockSeconds / recent solvetimes) — the "real-process" block-time signal that
	//   trims calibration drift + PoW variance. CLAMPED to [MinFeedbackTrim, MaxStepUp] per block — asymmetric
	//   since R2-D (0.25× down, 2× up): an overhang is cheap to hold, an under-shoot mints coins early.
	// • easing (Option A) — instead of snapping to target, close a fraction DifficultyEaseAlpha of the gap each
	//   block, so the change ramps in over a few blocks rather than instantly.
	public double GetNextBlockDifficulty(double networkPower)
	{
		if (Chain.Count == 0)
		{
			return InitialDifficulty;
		}

		double current = EffectiveDifficulty(Chain[^1]);

		// Feed-forward anchor (instant, NOT clamped — a known power level should land in one block).
		double anchor = networkPower > 0d ? InitialDifficulty * networkPower : current;

		// Feedback: LWMA over the last up-to-W solvetimes (most recent weighted highest). Clamped.
		double feedbackTrim = 1d;
		if (Chain.Count >= 2)
		{
			int deltas = Math.Min(LwmaWindow, Chain.Count - 1);
			double weightedSum = 0d;
			double weightTotal = 0d;
			for (int k = 0; k < deltas; k++)
			{
				double solveSec = (Chain[Chain.Count - 1 - k].Timestamp - Chain[Chain.Count - 2 - k].Timestamp) / 1000d;
				if (solveSec < 1d)
				{
					solveSec = 1d;
				}

				double weight = deltas - k; // k=0 is the most recent delta → highest weight
				weightedSum += weight * solveSec;
				weightTotal += weight;
			}

			feedbackTrim = TargetBlockSeconds / (weightedSum / weightTotal);
		}
		feedbackTrim = Math.Clamp(feedbackTrim, MinFeedbackTrim, MaxStepUp);

		// Ease toward the target rather than snapping, so a power change ramps in over a few blocks.
		// R2-D: asymmetric — cede an overhang fast, take on difficulty slowly (see the constants above).
		double target = anchor * feedbackTrim;
		double alpha = target < current ? DifficultyEaseAlphaDown : DifficultyEaseAlpha;
		double next = current + alpha * (target - current);
		return Math.Max(next, MinDifficulty);
	}

	// A block with no stored difficulty (pre-D.1 save) is treated as InitialDifficulty — the value it was
	// actually mined against — so old chains still validate and retarget cleanly.
	private static double EffectiveDifficulty(Block block)
	{
		return block.Difficulty > 0d ? block.Difficulty : InitialDifficulty;
	}

	public bool ChainIsValid(IReadOnlyList<Block> blockchain)
	{
		if (blockchain.Count == 0)
		{
			return false;
		}

		bool validChain = true;
		for (int i = 1; i < blockchain.Count; i++)
		{
			Block currentBlock = blockchain[i];
			Block prevBlock = blockchain[i - 1];

			// Merkle root must match the block's transactions (tamper check)…
			if (currentBlock.MerkleRoot != MerkleTree.ComputeRoot(currentBlock.Transactions))
			{
				validChain = false;
			}

			// …and the header must hash to a value meeting the difficulty target.
			string blockHash = HashHeader(
				prevBlock.Hash,
				currentBlock.MerkleRoot,
				currentBlock.Timestamp,
				currentBlock.Nonce
			);

			if (!IsHashAtTargetDifficulty(blockHash, EffectiveDifficulty(currentBlock)))
			{
				validChain = false;
			}

			if (currentBlock.PreviousBlockHash != prevBlock.Hash)
			{
				validChain = false;
			}
		}

		Block genesis = blockchain[0];
		bool correctGenesis = genesis.Nonce == 100
			&& genesis.PreviousBlockHash == "0"
			&& genesis.Hash == "0"
			&& genesis.Transactions.Count >= 1
			&& genesis.Timestamp == GenesisTimestampUnixMs;

		return validChain && correctGenesis;
	}

	public bool TryAcceptMinedBlock(Block newBlock)
	{
		Block lastBlock = GetLastBlock();
		bool correctHash = lastBlock.Hash == newBlock.PreviousBlockHash;
		bool correctIndex = lastBlock.Index + 1 == newBlock.Index;
		if (!correctHash || !correctIndex)
		{
			return false;
		}

		if (!IsHashAtTargetDifficulty(newBlock.Hash, EffectiveDifficulty(newBlock)))
		{
			return false;
		}

		Chain.Add(newBlock);
		PendingTransactions.Clear();
		_chainVersion++;
		AdvanceUtxoCacheWithAppendedBlock(newBlock); // mini-plan 17 B1
		return true;
	}

	public Block? GetBlock(string blockHash) => Chain.FirstOrDefault(x => x.Hash == blockHash);

	public (Transaction? transaction, Block? block) GetTransaction(string transactionId)
	{
		foreach (Block block in Chain)
		{
			Transaction? tx = block.Transactions.FirstOrDefault(t => t.TransactionId == transactionId);
			if (tx is not null)
			{
				return (tx, block);
			}
		}
		return (null, null);
	}

	public Transaction? GetPendingTransaction(string transactionId)
	{
		return PendingTransactions.FirstOrDefault(t => t.TransactionId == transactionId);
	}

	// Step 4b: a coinbase output needs CoinbaseMaturity confirmations (blocks mined on top) before
	// it is spendable — the fractal equivalent of Bitcoin's 100-confirmation rule (≈16h ≈ 1 block
	// here). Immature coinbase is excluded from the balance until it matures.
	public const int CoinbaseMaturity = 1;

	// Step 8 — an address's confirmed balance = Σ of its mature, spendable UTXOs (unspendable genesis and
	// immature coinbase excluded). AddressTransactions = every confirmed tx that references the address as an
	// input owner or an output recipient (for the explorer / history readers).
	public AddressData GetAddressData(string address)
	{
		int tipIndex = Chain.Count > 0 ? Chain[^1].Index : 0;
		Dictionary<string, UtxoEntry> utxos = GetUtxoSet();

		decimal balance = 0m;
		foreach (UtxoEntry utxo in utxos.Values)
		{
			if (utxo.Output.Address != address || !utxo.IsSpendable) continue;
			if (utxo.IsCoinbase && (tipIndex - utxo.BlockIndex) < CoinbaseMaturity) continue;
			balance += utxo.Output.Amount;
		}

		List<Transaction> addressTransactions = new();
		foreach (Block block in Chain)
			foreach (Transaction tx in block.Transactions)
				if (tx.Inputs.Any(i => i.Address == address) || tx.Outputs.Any(o => o.Address == address))
					addressTransactions.Add(tx);

		return new AddressData
		{
			AddressTransactions = addressTransactions,
			AddressBalance = balance
		};
	}

	// Confirmed spendable balance MINUS the value of UTXOs already reserved by a pending outgoing tx
	// (pending outputs are not spendable until mined). The scalar form of GetSpendableUtxos.
	public decimal GetAddressSpendableBalance(string address)
	{
		return GetSpendableUtxos(new[] { address }).Sum(u => u.amount);
	}

	// Step 8 (A.3 / coin selection) — the list of confirmed, mature, spendable, NOT-pending-spent outputs
	// owned by any of `addresses`. This is the wallet's selectable "coins"; combining several funds a payment
	// no single one covers (the multi-input case the player hit). Each entry carries the outpoint to spend.
	public IReadOnlyList<(OutPoint outpoint, string address, decimal amount)> GetSpendableUtxos(IEnumerable<string> addresses)
	{
		var owned = new HashSet<string>(addresses);
		int tipIndex = Chain.Count > 0 ? Chain[^1].Index : 0;
		Dictionary<string, UtxoEntry> utxos = GetUtxoSet();
		HashSet<string> spentByPending = CollectPendingSpentOutpoints();

		// Mini-plan 17 B2 — self-healing rather than silently slow. GetUtxoSet sets both together, so this is
		// unreachable in practice; building the index here if it is somehow absent keeps the method correct
		// AND fast from the next call, instead of quietly falling back to a full walk forever. A permanent
		// slow path that still returns the right answer is the kind of regression nothing ever notices.
		_addressIndex ??= BuildAddressIndex(utxos);

		var matched = new List<(string key, UtxoEntry utxo)>();
		int walked = 0; // mini-plan 18 B — the quantity mini-plan 17's index was built to shrink
		foreach (string address in owned)
		{
			if (!_addressIndex.TryGetValue(address, out HashSet<string>? keys)) continue;

			foreach (string key in keys)
			{
				walked++;
				if (!utxos.TryGetValue(key, out UtxoEntry? utxo) || utxo == null) continue;
				if (!utxo.IsSpendable) continue;
				if (utxo.IsCoinbase && (tipIndex - utxo.BlockIndex) < CoinbaseMaturity) continue;
				if (spentByPending.Contains(key)) continue;
				matched.Add((key, utxo));
			}
		}

		// ⚠ Mini-plan 17 B2, CORRECTED after run 1 — NO SORT HERE, deliberately, and this is the second
		// version of this comment.
		//
		// The first draft sorted by outpoint key, reasoning that `NetworkRoot.SelectUtxos` is order-dependent
		// (it returns the FIRST exact-amount match, and its `OrderByDescending` is a stable sort) so the order
		// this method returns should be *defined* rather than "whatever the dictionary enumerated". The
		// reasoning about determinism was right; putting the sort HERE was wrong.
		//
		// **The dominant consumer of this method discards the order entirely.**
		// `NetworkRoot.AggregateSpendable` calls it only to SUM the amounts, and it runs for every auction
		// bidder, every bot affordability check, every company treasury read and every dead-node sweep — dozens
		// of times per block in a populated era. Sorting for them is pure waste, in the hottest reader the UTXO
		// set has.
		//
		// The determinism now lives in `SelectUtxos`, where the order actually matters and where a sort
		// **already happens**, so it costs nothing: its greedy pass tie-breaks `OrderByDescending(amount)` with
		// the outpoint key, and its exact-match pass picks the smallest key among exact matches in one O(n)
		// scan. Same guarantee, no cost on the path that does not need it.
		Scripts.Diagnostics.BlockCostProfiler.NoteSpendableRead(walked); // mini-plan 18 B

		var result = new List<(OutPoint, string, decimal)>(matched.Count);
		foreach ((string _, UtxoEntry utxo) in matched)
		{
			result.Add((new OutPoint { PrevTxId = utxo.TxId, Vout = utxo.Vout }, utxo.Output.Address, utxo.Output.Amount));
		}

		return result;
	}

	public bool TryReplaceChain(List<Block> newChain, List<Transaction> newPendingTransactions)
	{
		if (newChain.Count <= Chain.Count || !ChainIsValid(newChain))
		{
			return false;
		}

		Chain.Clear();
		Chain.AddRange(newChain);
		PendingTransactions.Clear();
		PendingTransactions.AddRange(newPendingTransactions);
		_chainVersion++;
		return true;
	}

	// A 64-hex double-SHA256 hash meets the target when, read as a 256-bit integer, it is ≤ 2²⁵⁶ / Difficulty
	// (so the chance a random hash passes is 1/Difficulty). Continuous → any positive Difficulty is valid.
	public static bool IsHashAtTargetDifficulty(string hash, double difficulty)
	{
		if (difficulty <= 0d || string.IsNullOrEmpty(hash))
		{
			return false;
		}

		BigInteger target = (BigInteger)(MaxHash256dbl / difficulty);
		return HexToBigInteger(hash) <= target;
	}

	// 2²⁵⁶ as a double (≈1.16e77). Used only to derive the acceptance threshold; double's ~15–16 significant
	// digits set the probability precisely enough (the discarded low bits are far below any meaningful target).
	private static readonly double MaxHash256dbl = Math.Pow(2d, 256d);

	private static BigInteger HexToBigInteger(string hash)
	{
		// Prefix "0" so the leading hex nibble is never read as a sign bit → always a non-negative value.
		return BigInteger.Parse("0" + hash, NumberStyles.HexNumber);
	}

	// Expected nonce attempts per block at the chain's CURRENT difficulty (the tip's stored Difficulty).
	public double GetExpectedAttemptsForCurrentDifficulty()
	{
		return Chain.Count > 0 ? EffectiveDifficulty(Chain[^1]) : InitialDifficulty;
	}
}

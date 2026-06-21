# Per-Node Candidate Block Model — Implementation Plan

**Status**: 🆕 LEAD (roadmap Step 4). All 9 decisions resolved (§4). **4a ✅ verified in-engine; 4b.1 (template + coinbase-in-block + maturity N=1) IMPLEMENTED — compiles clean, pending in-engine verification.** Next: 4b.2 (fees), then 4b.3 (content-hash txid), then 4c (BlockExplorer surfacing).
**Goal**: replace the simplified "candidate = all pending tx, hash the whole block" mining with a **real per-node candidate-block competition engine**: a public mempool, fee-based transaction selection with a block cap, a Merkle root, a proper block header that is what actually gets hashed, and a coinbase carrying block reward + collected fees. This is the engine that hardware pools, bot competition, the refit bootstrap, and (later) the founder economics all build on.

**Scope guard (per the 2026-06-20 reset):** build this **generically**. Satoshi/Hal are plain nodes here; their special economics (11,000-BTC ramp, disappearance, the 12 Jan tx) are **out of scope** and re-attach in roadmap Step 7. The historical bootstrap (3a) stays on the simplified path during this step and is refit afterward (Step 5).

This is the detailed plan for `PRIVATE_ROADMAP.md` **P4 (Block Template Builder)**, and it absorbs `scheduled-bot-transactions-plan.md` **Phase 4 (fees)**.

---

## 1. Current State (what mining does today)

- **Per-node mempool**: each `NodeAgent` owns a `BlockchainService` with `PendingTransactions`. `NetworkSimulator.BroadcastTransaction` copies a tx into every other node's pending list — so today the mempool is effectively shared-by-replication.
- **Candidate**: `NodeAgent.TryMineSingleNonceAttempt` / `MinePendingTransactions` build candidate data `{ transactions = PendingTransactions, index }` — **all** pending txs, no cap, no ordering.
- **Hashing**: `BlockchainService.HashBlock` = `SHA256( previousBlockHash + nonce + JsonSerialize({transactions, index}) )`. The whole tx list is re-serialised on **every nonce attempt** (~585×/block).
- **Difficulty**: hash must start `"00"` and next hex ≤ `'6'` (~585 attempts). Unchanged target.
- **Coinbase**: created **after** mining and pushed to pending → it lands in the **next** block (matures one block later). `CreateGenesisCoinbase` carries the headline in `InputData`.
- **No** `MerkleRoot` on `Block`; **no** `Fee` on `Transaction`; **no** 24-tx cap enforced; **no** feerate ordering.
- **Balance model**: account-based (`GetAddressData` sums per-address txs) — testing-stage; UTXO realism is roadmap Step 8.

---

## 2. Target Model

| Piece | Target |
|---|---|
| **Public mempool** | A single shared mempool all miners read when building candidates (formalises P3's "public mempool shared by mining nodes"). |
| **Tx selection** | Per candidate: take top-N by feerate (cap = 24 incl. coinbase, TBD OQ-C4), tie-break by mempool arrival age. |
| **Coinbase** | Built **into** the block being mined: amount = block reward + Σ fees of included txs, paid to the miner's address. |
| **Merkle root** | `Block.MerkleRoot` computed from included txs (Bitcoin-style double-SHA256, duplicate-last on odd count). |
| **Header hashing** | Hash a compact **header** `{prevHash, merkleRoot, timestamp, nonce}` (double-SHA256), not the whole block. Faster (no per-nonce tx re-serialisation) and realistic. |
| **Per-node candidates** | Each miner builds its own candidate → different coinbase recipient + merkle root → different header hash. "Candidate blocks differ by miner" (P4 done-criterion). |
| **Difficulty** | Unchanged target (`"00"`, ≤`'6'`) applied to the new header hash. |

The existing **1 bet = 1 nonce attempt** cadence is unchanged — each attempt now hashes the header against the node's current candidate. The weighted lottery (Step 2) and bet-driven mining still decide *who* attempts; this step decides *what each candidate contains*.

---

## 3. Phases (finalised — all OQs resolved)

**Sub-split (per the Step-3 pattern): 4a = phases 1–3 (foundation) ✅; 4b.1 = template + coinbase-in-block + maturity ✅; 4b.2 = fees end-to-end; 4b.3 = content-hash txid (OQ-C6); 4c = phase 7 (dev surfacing).**

1. **Models** ✅ DONE (4a) — `Transaction.Fee` (default 0m) + reserved `SizeVBytes`; `Block.MerkleRoot`. Clean-save break.
2. **Merkle** ✅ DONE (4a) — `MerkleTree.ComputeRoot` + `LeafHash` (double-SHA256 content hash) in `BlockchainPort/Blockchain`.
3. **Header hashing** ✅ DONE (4a) — `BlockchainService.HashHeader{prevHash, merkleRoot, timestamp, nonce}` (double-SHA256) replaces whole-block serialisation; `ProofOfWork`/`ChainIsValid` updated (+ Merkle tamper check); `CreateNewBlock` takes timestamp+merkleRoot; timestamp is now fixed **before** mining (NodeAgent computes/caches the candidate Merkle root; NetworkRoot passes the timestamp pre-mine and no longer overrides it post-mine). Difficulty target unchanged (~585 attempts). **Coinbase still lands in the next block here — coinbase-in-block is 4b.**
4. **Public mempool + selection** ✅ DONE (4b.1) — `BlockTemplateBuilder.Build(minerAddress, reward, mempool)` selects ≤23 mempool txs by fee (stable → age tie-break), prepends the coinbase, computes the merkle root. Cap = 24 incl. coinbase.
5. **Coinbase-in-block + maturity N=1** ✅ DONE (4b.1) — coinbase is now tx #0 **inside** the mined block (`BlockchainService.CommitBlock`, removes only the included mempool txs from pending); `GetAddressData` enforces `CoinbaseMaturity = 1` (immature coinbase excluded from balance until 1 confirmation). `NodeAgent` caches the candidate template across bets; the obsolete coinbase-pending broadcast was removed from `NetworkRoot.HandleMinedBlock`. Fees are 0 here.
6. **Fees end-to-end** (4b.2) — sender-chosen `Transaction.Fee` deducted from the sender (spendable check covers `Amount + Fee`); collected into the coinbase; `scheduled-bot-transactions` Phase 4 reactivates here.
7. **Dev/verify** (4c) — BlockExplorer shows merkle root + per-tx fee + coinbase fee total; confirm candidates differ by miner.

> **Deferred within Step 4 (OQ-C6 full):** replacing the GUID `Transaction.TransactionId` field with the content-hash. 4a computes the content hash as the Merkle *leaf* (`MerkleTree.LeafHash`) but leaves the txid field a GUID, to avoid destabilising the signing payload + fixed bootstrap tx ids in the same pass. Folded into 4b.

---

## 4. Resolved Decisions (2026-06-20)

**OQ-C1 — Mempool model.** ✅ **RESOLVED:** keep per-node `PendingTransactions` as the propagation mechanism (broadcast) and add a thin `BlockTemplateBuilder` that reads a node's pending list as its mempool view. Least disruptive; leaves the door open to private mempools later.

**OQ-C2 — Fee model & tx size.** ✅ **RESOLVED:** assume **uniform tx size** now (so feerate ordering = fee ordering), with a nominal `SizeVBytes` field reserved on `Transaction` for later. Fees are a **separate `Transaction.Fee`** deducted from the sender **in addition to** `Amount` (sender needs `Amount + Fee` spendable). Fee = sender-chosen (wallet-plan OQ-9: 1–10 BTC for now).

**OQ-C3 — Header hashing refactor.** ✅ **RESOLVED:** yes — hash a real header `{prevHash, merkleRoot, timestamp, nonce}` via double-SHA256, replacing the whole-block serialisation. More realistic + faster (only the nonce varies per attempt). Clean-save break (routine).

**OQ-C4 — 24-tx cap composition.** ✅ **RESOLVED:** **24 including the coinbase** (coinbase + up to 23 mempool txs, mirroring Bitcoin's tx #0).

**OQ-C5 — Coinbase placement & maturity.** ✅ **RESOLVED:** coinbase is built **into the block it rewards** (tx #0), summing that block's fees + the block reward — this is the realistic structure (the old "reward in next block" was *not* how Bitcoin works). Maturity = **N = 1 confirmation**.
> **Why N=1 and not Bitcoin's 100:** Bitcoin's 100-confirmation coinbase maturity ≈ 100 × 10 min ≈ **~16.7 hours**. In this fractal each block already spans **~16.25 in-game hours**, so the faithful equivalent of "~16 h of maturity" is **≈ 1 block**. N=100 here would mean ~68 in-game days of lockup **and** would break the dated historical events (e.g. the 12 Jan Satoshi→Hal tx spends an early coinbase that, at our compressed block heights, would not be mature under a 100-block rule). N=1 is the fractal-correct maturity, keeps the "spendable next block" feel, and preserves Step 7's historical transactions.

**OQ-C6 — Merkle leaf + txid.** ✅ **RESOLVED:** compute a real **content-hash txid** (double-SHA256 of the canonical tx serialisation) and use it as both the txid and the Merkle leaf. More realistic and makes amounts / `InputData` tamper-evident via the Merkle root. (Touches tx identity everywhere — handle carefully.)

**OQ-C7 — Ancestor feerate.** ✅ **RESOLVED:** plain feerate ordering only for now; ancestor-feerate logic deferred to the UTXO step (roadmap Step 8).

**OQ-C8 — Per-miner selection variation.** ✅ **RESOLVED:** acceptable — all miners pick the same txs (deterministic), differing only by coinbase recipient, which already makes candidates/hashes differ. Private-mempool selection variation is post-Basic.

**OQ-C9 — Empty-mempool candidates.** ✅ **RESOLVED:** yes — a coinbase-only candidate is valid (as in Bitcoin).

---

## 5. Interactions with other plans

- **`scheduled-bot-transactions-plan.md` Phase 4 (fees)** → implemented here (OQ-C2). Its circulation triggers re-align in roadmap Step 6.
- **`btc-pools-hardware-plan.md`** → builds on this engine (per-credit nonce routing mines candidates); roadmap Step 6.
- **`historical-founders-and-bootstrap-plan.md`** → Phase 5 bootstrap refit = roadmap Step 5 (founders mine real candidates); Phases 4/6/7 economics = roadmap Step 7.
- **UTXO/Patoshi (roadmap Step 8)** → unlocks real ancestor feerate (OQ-C7) and proper change outputs.

---

## 6. Definition of Done (P4 criterion)

> Candidate blocks differ by miner; transaction selection matters; blocks carry a Merkle root and a coinbase that includes collected fees; the header (not the whole block) is what's hashed; difficulty target unchanged. Historical characters are unaffected (plain nodes) and the game still starts on 21 Mar 2009.

---

*Created: 2026-06-20. Pulled forward from P4 in the direction reset; see `IMPLEMENTATION_ROADMAP.md` Step 4.*

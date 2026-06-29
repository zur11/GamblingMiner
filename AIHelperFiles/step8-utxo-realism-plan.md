# Step 8 — UTXO Realism & Satoshi Address Non-Reuse — Implementation Plan

**Status**: Planning. Implements roadmap **Step 8** (`IMPLEMENTATION_ROADMAP.md` §3): ***Satoshi-only* fresh derived address per coinbase (address non-reuse), real change outputs to fresh addresses, surfaced via the derived-address wallet — Satoshi first, then the player (whose UTXO realism comes from change outputs, not coinbase spread).** Resolves the deferred **E8 (17.49 Hearn change)** from Step 7 and the open **§6 address-reuse research** in `historical-blockchain-events-research.md`.

**Roadmap**: depends on **Step 4** (per-node candidate-block / coinbase machinery — ✅ complete) and **Step 7** (founders as regulated concurrent miners — ✅ complete). **Companion data**: `historical-blockchain-events-research.md`, `btc-wallet-system-plan.md` (OQ-2).

**Created**: 2026-06-26.

---

## 0. The headline correction — "Patoshi pattern" is the wrong name for the address mechanic

The Step 7 design (D2) and several docs (`btc-wallet-system-plan.md` OQ-2, research doc Q-X1/Q-S2) call the **"fresh address per receive"** mechanic **"the Patoshi pattern."** **That is a misnomer**, and this step corrects it. The source research (`patoshi pattern.txt`) describes **two distinct, unrelated phenomena** the docs conflated:

| Phenomenon | What it actually is | Can we replicate it? |
|---|---|---|
| **Address non-reuse** | Satoshi used **20,000+ distinct addresses**, one ~50 BTC coinbase each, **almost never reused** (only documented outgoing: the single 10 BTC → Hal). A **wallet/privacy practice**. | ✅ **Yes — this is what passphrase multi-addresses replicate.** It is the subject of this step. |
| **The Patoshi pattern** (Lerner, 2013) | A **forensic mining fingerprint**: the *ExtraNonce* sequence, the *decrementing* nonce, and the timestamp recomputed only every `0x40000` nonces — software-behavior artifacts that attribute ~22,000 blocks to one miner. **About mining, not addresses.** | ⚠️ Only as an **optional cosmetic flavor** (Phase 8.5), and clearly labelled as a stand-in — our engine has no ExtraNonce field and uses random-nonce search, not a decrementing sweep. |

**Decision D0 (locked):** Rename the address mechanic everywhere from *"Patoshi pattern"* to **"address non-reuse"** / **"one address per receive"** / **"HD-lite derived-address wallet."** Reserve the term *"Patoshi pattern"* exclusively for the (optional) mining-forensic feature in Phase 8.5. Passphrase addresses are the **address-non-reuse** mechanic — **not** the Patoshi mining fingerprint.

### 0.1 The fractal mapping (why this is achievable, not a 20,000-address nightmare)

BTC amounts are not scaled — only **block density** (~1.477 blocks/in-game-day vs real ~144/day ⇒ ~1% of the blocks) and **total supply** (210,000 = 1%) are. So:

```
Real Satoshi:    ~20,000 addresses × ~50 BTC  ≈ 1,100,000 BTC
Fractal Satoshi:  11,000 BTC ÷ 50 BTC/coinbase = ~220 coinbase addresses  (= 1% of 20,000 ✓)
```

**One fresh address per Satoshi coinbase automatically yields ~220 addresses** — the exact fractal analog of the historical 20,000+, with no extra machinery. (~111 accrue in the bootstrap to 21 Mar 2009 ≈ 5,550 BTC; ~109 more in the player era to the 11,000 floor.) This is the headline payoff and it confirms the design is *naturally* sized, not forced.

### 0.2 Decisions locked for this step

| # | Question | Decision |
|---|---|---|
| **D0** | Is the passphrase multi-address mechanic "the Patoshi pattern"? | **No.** It is **address non-reuse**. Rename throughout (§0). |
| **D1** | UTXO realism depth for v1? | **UTXO-lite: single-input + change output.** A spend consumes exactly **one** funded derived address (the chosen "UTXO"), pays the recipient, and returns change to a **fresh** derived address. This makes E8 a *real* change output and teaches change mechanics without a multi-input tx refactor. **Multi-input consolidation is deferred** (§6 OQ-8.1). |
| **D2** | Scope order? | **Founders first (Satoshi the showcase), then the player wallet.** Bots stay single-address (no seed stored; not the educational focus) — deferred (§6 OQ-8.2). |
| **D3** | How is per-node derived-wallet state persisted, given "a block is the only commit to disk"? | **It is not persisted — it is reconstructed from the chain** by deriving `addr(0), addr(1), …` and scanning for on-chain appearances (HD-wallet "gap-limit" rescan). Mirrors the chain-derived state pattern used everywhere (Step 7's chain-derived event-fired state, `EnsureSecondBlockBootstrapPendingTx`). No new save file. |
| **D4** | §6 research — was any Satoshi address reused? | **RESOLVED from two sources** (`patoshi pattern.txt` + `Respuesta ¿Hal Finney Envió BTC a Satoshi.txt`): effectively **no** — one address per block reward; the only outgoing was the single 10 BTC → Hal; **even the address Satoshi used to receive from Mike Hearn was new** (not genesis, not the Hal-related address). The genesis address is the documented exception (unspendable + later third-party tributes). ⇒ **strict one-address-per-receive holds on both the receive *and* change side**; the genesis address keeps its existing fixed treatment. |
| **D5** | Reference base58 addresses for the events ledger? | **Captured** (commented references only, per Q-X3 — payouts use derived `gm1q…`): Satoshi's E4-source = block-9 coinbase **`1HLoD9E4SDFFPDiYfNYnkBLQ85Y51J3Zb1`**; Hal's 10-BTC receiving address = **`1Q2TWHE3GMdB6BZKafqwxXtWAWgFt5Jvm3`**; Hearn's address = `1JuEjh9znXwqsy5RrnKqgzqY4Ldg7rnj5n` (already in the research doc). Resolves the open §6 "Hal's real receiving address" item. |

---

## 1. What exists today (verified in code)

- **Account/balance model** (`BlockchainService.GetAddressData(address)`, line ~422): balance = Σ confirmed txs to an address − spends from it, with `CoinbaseMaturity` gating. There is **no UTXO set** — an address is just a running balance. This is the **testing-stage** simplification Step 8 refines (OQ-2).
- **One address per node**: `NodeAgent.WalletAddress` is a single base address; the coinbase always pays it — `BlockTemplateBuilder.Build(WalletAddress, reward, …)` sets `coinbase.Recipient = minerAddress` (`BlockTemplateBuilder.cs:30`). Every block a node mines pays the **same** address ⇒ the opposite of address non-reuse.
- **HD-lite derivation already exists** (Phase 0–8 of `btc-wallet-system-plan.md`): `CryptoUtils.DeriveGmAddress(seedPhrase)`, `DeriveSigningKeypair(seedPhrase)`, `DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase)`. The **passphrase wallet** *is* exactly "one seed → many addresses": vary the trailing word(s) ⇒ a fresh `gm1q…` address + keypair. **This is the engine we reuse for index-based derivation.**
- **Founder seeds available**: `WalletInitializationService` holds `satoshi` / `hal` / `mike_hearn` wallets (seed phrases); founder nodes are registered in `NetworkRoot`. Genesis + block-2 coinbase are pinned to Satoshi's **base** derived address (`NormalizeGenesisAcrossNodes`, `EnsureSecondBlockBootstrapPendingTx`).
- **Founder confirmed BTC** (`NetworkRoot.GetFounderConfirmedSpendableBtc(founderId)`, Step 7): currently sums a **single** address. The Satoshi regulator's `satoshiConfirmedBtc` reads this each block — it already says *"summed across his address(es)"*, anticipating multi-address.
- **Spends are single-sender→single-recipient**: `Transaction { Sender, Recipient, Amount, Fee, Salt, … }` — **one** input address, **one** output, no native change output. `NodeAgent.CreateSignedTransaction` signs from `WalletAddress` only.
- **E8 omitted** (Step 7 test log): the 17.49 Hearn change was dropped because *"implicit change in the account model / self-send is rejected"* — `BlockchainService.ValidateTransaction` forbids a tx paying its own sender (`:209`). **A real change output to a *fresh* address sidesteps that rule** — that's the Step 8 fix.
- **Passphrase node registration** (`NetworkRoot.RegisterPassphraseWallet`): derives a keypair on demand, creates a session-scoped `NodeAgent` (`pass_{addr[4..12]}`), syncs the chain. The signing-from-a-derived-address capability we need is a generalization of this.
- **"Block = the only commit to disk"**: nothing between blocks persists; an app restart reverts to the last block. ⇒ **the derived-wallet must be chain-reconstructable** (D3), never a side file.

---

## 2. Target mechanism — the HD-lite derived-address wallet

### 2.1 Derivation scheme

A node that owns a seed phrase derives an unbounded, deterministic address book by index:

```
addr(i)    = CryptoUtils.DeriveGmAddress(seedPhrase + DerivationSuffix(i))
keypair(i) = CryptoUtils.DeriveSigningKeypair(seedPhrase + DerivationSuffix(i))   // + secp256k1 pubkey
DerivationSuffix(i) = " #r" + i          // reserved namespace, never collides with user passphrases
addr(0) = the existing base address (DerivationSuffix(0) == "" → unchanged, back-compatible)
```

`i = 0` **must** reproduce today's base address (empty suffix) so genesis/bootstrap pins and existing balances are untouched. `i ≥ 1` are the new fresh receive addresses.

### 2.2 Receiving — one fresh address per coinbase (address non-reuse)

> **Scope (Basic Mode v1): coinbase rotation is SATOSHI-ONLY.** One-address-per-reward is what distinguishes Satoshi (the "Patoshi" pattern); every other miner (player, Hal, bots, casino) keeps a **single** coinbase/identity address. The mechanism below is general (any node *could* rotate), but in v1 only `satoshi` does. The player still uses the same `DerivedAddressWallet` — but only for **change addresses** on send (Phase 8.4), not coinbases.

- The wallet tracks `nextReceiveIndex`. **Each rotated receive** (a Satoshi coinbase, or a founder receiving a scripted tx, or a change output) is paid to `addr(nextReceiveIndex)`, then `nextReceiveIndex++`. **No address is ever paid twice.**
- For Satoshi mining: `BlockTemplateBuilder.Build(...)` is fed `addr(nextReceiveIndex)` instead of the static `WalletAddress` as the coinbase recipient. (Non-rotating miners keep `WalletAddress`.)
- **Founder scripted-event receives also rotate** (not just coinbases). When a founder is the *recipient* of a scripted historical tx — notably **E6b (Hearn → Satoshi 32.51)** — the payment lands on a **fresh** derived address, never a reused one. This is **historically confirmed** (`Respuesta ¿Hal Finney Envió BTC a Satoshi.txt`): the address Satoshi used to receive from Mike Hearn was *new* — not the genesis address and not the address that received the 10 BTC from/sent to Hal. (For the interactive **player**, incoming external-deposit rotation is deferred — OQ-8.3 — but for the **scripted, controlled founder events** it is free and historically mandated, so we do it.)

### 2.3 Reconstructing the wallet from the chain (D3 — no persistence)

On launch (and after any revert-to-last-block), there is no saved index. Reconstruct exactly like a real HD wallet rescan:

```
i = 0
while addr(i) appears anywhere on-chain (as coinbase recipient or tx output):  i++
nextReceiveIndex = i            // first gap = next fresh address
ownedAddresses   = { addr(0) … addr(i-1) }   // the wallet's funded/used set
```

A small **gap limit** (e.g. scan a few indices past the last hit to tolerate a reverted-but-rebroadcast coinbase) keeps it robust against the between-block revert. This is consistent with how every other between-block fact is recomputed from the chain.

### 2.4 Spending — UTXO-lite single-input + change (D1)

To send `X` (+ `fee`) from a derived-address wallet:

1. **Coin-select one funded address** `addr(k)` whose confirmed balance ≥ `X + fee` (smallest-that-covers, i.e. closest fit; largest-first as fallback). For the historical events every send is covered by a single 50-BTC coinbase, so one input always suffices.
2. **Build the spend**: `Sender = addr(k)`, `Recipient = payee`, `Amount = X`, `Fee = fee`, signed with `keypair(k)`.
3. **Emit the change output**: `change = balance(addr(k)) − X − fee`. If `change > 0`, create a **second** tx `Sender = addr(k) → Recipient = addr(nextReceiveIndex++)` for `change` (a real change output to a **fresh** address — never a self-send to the same address, so it passes the "no paying your own sender" rule).
4. Both txs go to the mempool together and confirm in the same block, mirroring a real 1-input/2-output tx within our 1-in/1-out transaction model.

> **Why not real multi-input/multi-output now?** Our `Transaction` is single-sender→single-recipient. A faithful multi-output tx would require reworking `Transaction`, Merkle, validation, and the explorer. D1 keeps that deferred: a single chosen UTXO + a paired change tx delivers genuine *change-output* behavior and the educational payoff with no model rewrite. Consolidating many small UTXOs into one payment is the explicit deferral (OQ-8.1).

---

## 3. Phases

### Phase 8.1 — `DerivedAddressWallet` (the HD-lite core)

**New file**: `Scripts/BlockchainPort/Blockchain/DerivedAddressWallet.cs` (pure C#, no Godot/chain state). **Touches**: `NetworkRoot.cs` (chain-scan helpers), `CryptoUtils.cs` (reuse existing derivation).

Owns, with **no persisted state** (reconstructed from the chain — D3):

1. `DeriveAddress(int i)` / `DeriveSigningContext(int i)` → `(address, signingKeypair, secp256k1Pubkey)` via the §2.1 suffix scheme; `i = 0` ⇒ base address unchanged.
2. `Rescan(Func<string,bool> addressAppearsOnChain, int gapLimit)` → sets `NextReceiveIndex` and the `OwnedAddresses` set (§2.3).
3. `NextReceiveAddress()` → `addr(NextReceiveIndex)` (does **not** advance — advancement happens when a receive is actually committed, i.e. when the block is mined and the rescan moves the frontier).
4. `TryFindSpendingContext(string fundedAddress)` → the index + keypair for a held address, so any owned address can sign (generalizes `RegisterPassphraseWallet`).
5. **`NetworkRoot` support**: `AddressAppearsOnChain(address)` (coinbase recipient or any tx output/input) and `GetWalletTotalConfirmed(IEnumerable<string>)` (sum across an address set).

**Verification**: a unit-style DEV check in `FoundersWallets` — derive Satoshi's first N addresses, confirm `addr(0)` == current base, addresses are distinct, and rescan finds the right frontier on a bootstrapped chain.

---

### Phase 8.2 — Satoshi one-address-per-coinbase (address non-reuse, ~220 addresses)

**Touches**: `BlockTemplateBuilder.cs` (recipient parameter already exists), `NetworkRoot.cs` (coinbase recipient selection), `HistoricalBootstrapService.cs` (bootstrap coinbases), `FoundersMiningService.cs` / `SimulationService.cs` (player-era founder coinbases), `NetworkRoot.GetFounderConfirmedSpendableBtc`.

1. **Coinbase recipient = fresh derived address.** Where a founder mines (bootstrap bulk-mining and player-era `DrainFounderAttempts`), set the coinbase recipient to the founder wallet's `NextReceiveAddress()` instead of the static base address. Genesis + block-2 stay pinned (D4 / existing normalization) — they are `addr(0)`/an early index, so no conflict.
2. **Aggregate confirmed BTC across the address set.** `GetFounderConfirmedSpendableBtc(founderId)` sums `GetWalletTotalConfirmed(ownedAddresses)` (still excluding the unspendable genesis 50, OQ-8). The Satoshi regulator (`FoundersMiningService` §2.2) is unchanged — it already reads "across his address(es)."
3. **Result**: Satoshi accrues ~111 addresses in the bootstrap + ~109 in the player era ⇒ **~220 distinct one-coinbase addresses by the 11,000-BTC floor** — the fractal analog of 20,000+ (§0.1).
4. **Hal stays single-address (DECIDED 2026-06-27).** Address non-reuse is a **Satoshi-only** trait — the "Patoshi"/one-address-per-reward pattern. The source explicitly notes *other early miners reused addresses constantly*, and Hal was one of them. So **only `satoshi` gets a `ReceiveWallet`**; Hal/Hearn keep their single base address (Hal reverts to pre-8.2 behavior). This also keeps Hal's balance correct in the base-address displays with no extra work.

**Verification**: `founders_trace.csv` / a `FoundersWallets` readout shows Satoshi's **address count** climbing alongside confirmed BTC; no address receives two coinbases (`mined: N` per node already exists in Block Explorer).

---

### Phase 8.3 — UTXO-lite spends + reinstating E8 (real change output)

**Touches**: `NetworkRoot.cs` (spend path), `HistoricalEventScheduler.cs` (E4/E6/E6b/E7 + **E8**), `HistoricalBootstrapService.cs` (E4 is bootstrap-era).

1. **Generalize the spend path** to UTXO-lite (§2.4): a new `CreateSpendWithChange(fromWalletSeed/ownedSet, recipient, amount, fee)` that coin-selects one funded derived address, signs with its keypair, and emits the paired change tx to a fresh address. Used by founder scripted txs (and later the player, Phase 8.4).
2. **Rework the Hearn round-trip to produce real change** (the Step 7 sequence, now UTXO-correct):
   - **E6** — Satoshi → Hearn **32.51**, spending one matured **50-BTC** coinbase `addr(k)`.
   - **E8** — **17.49 change → a fresh Satoshi address** `addr(next)` (50 − 32.51), now a *genuine* change output, not a rejected self-send. **This is the deferred Step 7 item, resolved.**
   - **E6b** — Hearn → Satoshi **32.51** (Hearn's single outgoing). **The recipient is a *fresh* Satoshi derived address** `addr(next)` — not the E6 change address, not genesis, not any Hal-related address. Historically confirmed (`Respuesta ¿Hal Finney Envió BTC a Satoshi.txt`): the address Satoshi used to receive from Mike Hearn was *new/specific to this transaction*.
   - **E7 splits into two single-input sends (DECIDED 2026-06-26)** — one 50-BTC coinbase can't fund 82.51 under UTXO-lite (D1), and this restores the historically-accurate amounts (research doc E6=32.51 test, E7=50.00 gift):
     - **E7a** — Satoshi → Hearn **32.51** (returns the coin Hearn sent in E6b), sourced **exactly** from the fresh address that received E6b → no change.
     - **E7b** — Satoshi → Hearn **50.00** (the gift), sourced from one matured 50-BTC coinbase → no change.
   - Net Hearn = +32.51 (E6) − 32.51 (E6b) + 32.51 (E7a) + 50.00 (E7b) = **+82.51**, unchanged. Hearn still signs exactly one tx (E6b).
3. **E4** (12 Jan 10 BTC Satoshi→Hal, bootstrap) likewise spends one matured coinbase and sends **~40-BTC change minus fee** (50 − 10) to a fresh Satoshi address — making even the first p2p tx UTXO-faithful. Hal receives the 10 BTC on his own derived address.
4. **Hal stays strictly receive-only — no reciprocal tx.** The same source confirms the Satoshi↔Hal relationship was **unidirectional**: Satoshi → Hal 10 BTC is the *only* documented tx between them; **Hal never sent BTC to Satoshi** (no public record). We deliberately add **no** Hal→Satoshi tx — Hal's on-chain activity stays = his coinbases + receiving E4 (matches Step 7).
5. **Idempotency moves to salt-based** (required by 8.2): once a scripted spend can source from *any* funded derived address, its txid is no longer reproducible from a fixed base sender, so `IsHistoricalTxConfirmedStatic` must match a confirmed/pending tx by its **`Salt`** (unique per event) + recipient + amount, not by reconstructing the txid from the base address. Each `hist_*` salt stays unique; chain-derived fired-state still survives revert-to-last-block.

**Verification**: in-engine run — Block Explorer shows E6 (32.51→Hearn) **and** E8 (17.49→fresh Satoshi addr) in the same block; Satoshi's net unchanged; Hearn nets +82.51; the change address is brand-new (never seen before).

---

### Phase 8.4 — Player wallet UTXO realism

**Touches**: `NetworkRoot.cs` (player change/spend), `Screens/BTCWallet/BTCWallet.cs/.tscn`, `WalletInitializationService.cs` (player seed already present).

> **Scope (DECIDED 2026-06-27): coinbase address spread is a SATOSHI-ONLY trait in Basic Mode v1.** One-address-per-reward (the "Patoshi" pattern) is what *distinguishes Satoshi* — the player and every other miner (Hal, bots, casino) keep a **single coinbase/identity address**. The player meets UTXO realism through **change outputs on send**, not coinbase spread. (Revisit only if a concrete reason emerges later.)

1. **Player keeps one coinbase/receive address.** Mined rewards (and external deposits) accrue to the player's **base address** — no per-block rotation. The player only becomes multi-address by *spending* (change, below).
2. **Player send → UTXO-lite with change** (§2.4): the send form coin-selects a funded owned address, sends, and the change returns to a **fresh derived address** ("Change: X → gm1q…new") — this is where the player's wallet first becomes multi-address. The player node carries a `DerivedAddressWallet` (seeded from `PlayerWallet.SeedWords`, `addr(0)` == today's `BaseAddress`) used **only for change addresses + signing any owned address**; its **coinbase recipient stays the base address** (the frontier advances on change, not coinbase). Reuses `CreateSpendWithChange` from 8.3.
3. **BTCWallet aggregated view.** Show **"Wallet total (N addresses)"** = `GetWalletTotalConfirmed(ownedSet)` (base + any change addresses), plus an expandable **address list** (each address + balance) so the player *sees* a wallet is a collection of addresses/UTXOs — the educational core of OQ-2. Usually just the base; after sends, change addresses appear.
4. **Deposit address rotation** (incoming external receives to a fresh address each time): **deferred** (OQ-8.3) — v1 keeps a single shown deposit address.
5. **Passphrase wallets**: unchanged — independent seeds; each could be its own HD-lite wallet if/when needed (deferred).

**Verification**: coinbases keep landing on the **base address** (no spread). Send an amount from BTCWallet → change lands in a new address; the aggregated total is conserved; the address list grows by the change address.

---

### Phase 8.5 — (Optional flavor) The *real* Patoshi pattern — mining-forensic view

**Touches**: `Screens/BlockExplorer/BlockExplorer.cs` (read-only), no model change. **Gate**: DEV/optional — ship only if cheap.

The genuine Patoshi pattern is a **mining fingerprint**, not addresses. Our engine can't reproduce ExtraNonce/decrementing-nonce/timestamp artifacts (random-nonce search, no ExtraNonce field), so this is an **honest cosmetic stand-in**, clearly labelled:

- A Block Explorer **"forensic" toggle** that highlights all blocks where `MinedByNodeId == "satoshi"` (data we already store) as a contiguous band — visually echoing Lerner's ExtraNonce-vs-height plot ("the slope that reveals one miner").
- A one-line teaching caption: *"In real Bitcoin, Satoshi's blocks were identified forensically by the Patoshi mining fingerprint (ExtraNonce/nonce/timestamp artifacts) — here we attribute them directly from the miner id. This is distinct from address non-reuse (the many-addresses pattern), shown in the wallet."*

This keeps the two concepts **separated and correctly named** for the player. **Decision (OQ-8.5): documented only — NOT built in Basic Mode v1.** This phase stays as a designed-but-unbuilt future/optional flavor; the D0 terminology correction still ships in the docs (Phase 8.6) regardless.

---

### Phase 8.6 — Documentation alignment + terminology correction

**Touches**: `CLAUDE.md`, `Documentation/{ProjectDesignManual,DESIGN_OVERVIEW,GLOSSARY,PRIVATE_ROADMAP,PLAYER_GUIDE}.md`, `AIHelperFiles/{IMPLEMENTATION_ROADMAP,btc-wallet-system-plan,historical-blockchain-events-research,step7-historical-character-economics-plan}.md`.

- **Apply D0 globally**: replace *"Patoshi pattern"* (used for the address mechanic) with **"address non-reuse" / "one address per receive"**; reserve "Patoshi pattern" for the Phase 8.5 mining-forensic note. Fix `btc-wallet-system-plan.md` OQ-2, research doc Q-X1/Q-S2, Step 7 D2, and the CLAUDE.md balance-model paragraph.
- **Record the UTXO-lite model** (single-input + change, §2.4) as its own ProjectDesignManual chapter, cross-referencing the candidate-block chapter (Step 4) and founder economics (Ch. 28).
- **Flip §6 of `historical-blockchain-events-research.md` to RESOLVED** (D4/D5): strict one-address-per-receive holds (incl. the receive side — Satoshi received from Hearn at a *new* address); Satoshi↔Hal is unidirectional (Hal never sent). Fill the reference-address column in §2 with the captured base58 addresses (Satoshi block-9 `1HLoD9E4…`, Hal `1Q2TWHE3…`). Mark **E8 implemented** (no longer deferred).
- **Add the fractal-address mapping** (20,000 → ~220, §0.1) to the canonical decisions / glossary.
- Mark roadmap Step 8 phases done; note bots-multi-address and multi-input consolidation as the carried-forward deferrals.

---

## 3b. Implementation status & test log

| Phase | Status | Notes |
|---|---|---|
| 8.1 DerivedAddressWallet | ✅ **Done (compiles; DEV-verifiable)** | `Scripts/BlockchainPort/Blockchain/DerivedAddressWallet.cs` (pure C#): index derivation (`addr(0)`==base via empty suffix, `addr(i≥1)` = seed+`" #r"+i`), `DeriveSigningContext`, chain-`Rescan` (gap limit 20), `NextReceiveAddress`, `TryFindSpendingContext`. `NetworkRoot.CollectUsedAddressSet()` (one-pass O(1) probe, OQ-8.4) + `GetWalletTotalConfirmed(addresses)`. DEV readout in `FoundersWallets` ("Derived Addresses [DEV]"): verifies addr(0)==base + distinctness + rescan frontier/owned/total. No persisted state. |
| 8.2 Satoshi address non-reuse | ✅ **Done (compiles & DEV-verified)** | **`ReceiveWallet` on `satoshi` ONLY** (Hal/Hearn single-address — address non-reuse is Satoshi's "Patoshi" trait). Coinbase paid to `NextReceiveAddress()`; frontier advances on commit (`MarkReceiveConsumed`), positioned from chain at init (`RescanFounderReceiveWallets`). **Index 0 reserved as base/identity** (genesis, p2p receives); coinbases use index ≥1. **Balance displays aggregate across the derived set** via `AggregateSpendable` / `GetNodeBalanceDetails` (`GetNodeSpendableBalance`, Block Explorer node status, FoundersWallets base panel) — fixes the "Satoshi shows 0 BTC" issue where coinbases sit on derived addresses, not base. The spend-starvation coupling it surfaced is resolved in 8.3. |
| 8.3 UTXO-lite spends + E8 | ✅ **Done (compiles)** | `NodeAgent.CreateSignedTransactionFrom` (sign from any owned address). `InjectHistoricalSignedTxStatic` reworked to UTXO-lite: coin-selection = **exact-match UTXO first, else largest-covering** (`TrySelectSpendSource`), fresh recipient for multi-address founders, and a real **change output to a fresh address (E8)** that drains the consumed UTXO. **Idempotency now salt-based** (`IsHistoricalSaltPresent` / `IsHistoricalTxConfirmedStatic` match by `Salt`, not the now-variable txid). Scheduler split **E7 → E7a (32.51) + E7b (50.00)**: exact-match makes E7a spend precisely the E6b-returned 32.51 and E7b a whole 50-coinbase (both change-free); largest-covering makes **E6 spend a pristine 50-coinbase → the iconic 17.49 change** (not a smaller leftover like the 40-BTC E4 change). E4 → 40 change. |
| 8.4 Player wallet UTXO realism | ✅ **Done (compiles & in-engine verified)** | Player node now carries a `DerivedAddressWallet` (`addr(0)`==base) with **`NodeAgent.RotateCoinbaseAddress = false`**, so coinbases stay on the base address (spread is Satoshi-only) and the frontier advances on **change only**. New `NodeAgent.CoinbaseRecipient`/`OnCoinbaseCommitted` gate rotation. Player sends route through `NetworkRoot.CreateSpendWithChange` (UTXO-lite: coin-select one owned address via `TrySelectSpendSource`, pay payee, real **change → fresh derived address**, fee netted via `GetAddressSpendableBalance`); bots/casino/passphrase (no `ReceiveWallet`) keep the single-tx base path. BTCWallet shows **"Wallet total (N addresses)"** = aggregate across the owned set + expandable **address list** (`NetworkRoot.GetNodeAddressBook`, `[base]`/`[change]` rows). `RescanFounderReceiveWallets` repositions the player frontier from the chain too; `GetNodeAddressLines` wording differentiates player change-spread vs founder reward-spread. No persisted state. **Verified:** coinbases stay on base (no spread); a send lands change on a brand-new address; aggregated total conserved; address list grows by the change address. Documented in ProjectDesignManual Ch. 30. |
| **Full UTXO model (Appendix A)** | ✅ **Done (compiles & in-engine audited)** | **Promoted from deferred → built** (OQ-8.1 trigger fired). Real multi-input/multi-output `Transaction` (`OutPoint`/`TxInput`/`TxOutput`; `Sender`/`Recipient`/`Amount` now `[JsonIgnore]` read-only shims). Chain-replayed **UTXO set** (cached by `_chainVersion`), `GetSpendableUtxos`, balance/maturity off the set. Per-input sighash signing (`NodeAgent.BuildSignedSpend`), validation rewrite (`Σin≥Σout`, `Fee=Σin−Σout`, double-spend guard, per-input ownership+sig), coinbase = input-less. ONE unified spend path `NetworkRoot.BuildAndBroadcastUtxoSpend` (exact-match else largest-first **multi-input** coin selection + change) for all nodes; scripted events → single 1-in/2-out txs (E8 = E6's change output). **Clean reset** via `WorldFormatVersion`. **Audit:** 122 blocks — conservation holds everywhere, supply 6150.2 BTC, 0 double-spends, Satoshi 109 coinbase addresses, **100-input→5000 consolidation**, E4 ✓. UI: BTCWallet + FoundersWallets address books + "View empty addresses" toggle (default off). ProjectDesignManual Ch. 30 rewritten. |
| 8.5 Patoshi forensic view (optional) | ⏸ **Deferred (OQ-8.5)** | Documented only — **not built in Basic Mode v1**. Block Explorer highlight of Satoshi-mined blocks + teaching caption; future/optional flavor. |
| 8.6 Docs + terminology fix | ☐ TODO | Apply D0 rename globally; mark §6 + E8 resolved; record the UTXO model (Ch. 30 done). |

---

## 4. Suggested build order & dependencies

```
8.1 DerivedAddressWallet (the HD-lite core, DEV-verifiable)
      ├─> 8.2 Satoshi address non-reuse (coinbase → fresh address)
      └─> 8.3 UTXO-lite spends + E8 (needs the spend/change path)
                └─> 8.4 player wallet UTXO realism (reuses 8.3's spend-with-change)
8.5 Patoshi forensic view  ── optional, independent, last-or-skip
8.6 docs                   ── last (includes the global terminology rename)
```

8.1 is the keystone (everything derives from it). 8.2 + 8.3 are the founder showcase (Satoshi's ~220 addresses + the real E8 change). 8.4 brings it to the player. 8.5 is optional flavor. 8.6 fixes the naming everywhere.

---

## 5. File checklist

| File | Phase | Action |
|---|---|---|
| `Scripts/BlockchainPort/Blockchain/DerivedAddressWallet.cs` | 8.1 | **new** — index derivation, chain rescan, sign-any-owned-address |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | 8.1–8.4 | `AddressAppearsOnChain`, `GetWalletTotalConfirmed`; fresh coinbase recipient; `CreateSpendWithChange`; aggregate `GetFounderConfirmedSpendableBtc` |
| `Scripts/BlockchainPort/Blockchain/BlockTemplateBuilder.cs` | 8.2 | feed `NextReceiveAddress()` as the coinbase recipient (param already exists) |
| `Scripts/Services/HistoricalBootstrapService.cs` | 8.2,8.3 | bootstrap founder coinbases → fresh addresses; E4 spend-with-change |
| `Scripts/Services/FoundersMiningService.cs` / `SimulationService.cs` | 8.2 | player-era founder coinbases → fresh addresses |
| `Scripts/Services/HistoricalEventScheduler.cs` | 8.3 | E6/E6b/E7 spend-with-change; **reinstate E8** (17.49 → fresh Satoshi addr) |
| `Screens/BTCWallet/BTCWallet.{tscn,cs}` | 8.4 | aggregated wallet total + derived-address list; send-with-change UI |
| `Screens/BlockExplorer/BlockExplorer.cs` | 8.5 | optional Satoshi-mined forensic highlight + caption |
| `CLAUDE.md`, `Documentation/*`, `AIHelperFiles/*` | 8.6 | D0 rename, model chapter, §6 + E8 resolved, fractal-address mapping |

---

## 6. Open questions

### 6.1 Resolved this round
- **D0 — "Patoshi pattern" naming.** ✅ The address mechanic is **address non-reuse**, not the Patoshi mining fingerprint. Rename everywhere (§0).
- **D4 / §6 research — Satoshi address reuse.** ✅ From `patoshi pattern.txt` **+ `Respuesta ¿Hal Finney Envió BTC a Satoshi.txt`**: effectively none — strict one-address-per-receive holds **including the receive side** (Satoshi received from Hearn at a *new* address); genesis is the documented exception (unspendable + third-party tributes).
- **Unidirectional Satoshi↔Hal.** ✅ Hal never sent to Satoshi (no public record) — stays strictly receive-only, no reciprocal tx added (Phase 8.3 item 4).
- **D5 — reference base58 addresses.** ✅ Captured for the events ledger (Hal `1Q2TWHE3…`, Satoshi block-9 `1HLoD9E4…`), commented-reference only; resolves the open §6 "Hal's real receiving address" item.
- **E8 (17.49 change).** ✅ Reinstated as a real change output to a fresh address (Phase 8.3), resolving the Step 7 deferral.
- **Persistence (D3).** ✅ No new save file — derived wallet is reconstructed from the chain (gap-limit rescan).

### 6.2 Decided this round (Basic Mode v1)
- **OQ-8.1 — multi-input consolidation.** ✅ **RESOLVED — IMPLEMENTED (Appendix A promoted).** The trigger fired exactly as foreseen: the first real multi-address send no single address could fund (a player consolidating many coinbase UTXOs) hit UTXO-lite's single-input wall, so the full multi-input/multi-output UTXO model (Appendix A) was built and now supersedes UTXO-lite. In-engine audit confirmed a **100-input → 1-output 5000-BTC** consolidation, full money conservation, 0 double-spends, and Satoshi's 109-address spread. See Appendix A (marked implemented) + ProjectDesignManual Ch. 30.
- **OQ-8.2 — bot multi-address.** ✅ **DECIDED — bots deferred; casino + Hal + Hearn DONE.** **Casino, Hal Finney, and Mike Hearn now have change-address rotation** (the player's pattern: `ReceiveWallet` seeded from their own phrase, `RotateCoinbaseAddress = false` → coinbase/receives on base, change-on-send rotates to a fresh address). Casino balance aggregates across its owned set and CasinoFinances got the address-book + empty-toggle view; FoundersWallets labels Hal/Hearn derived rows *change* vs Satoshi's *coinbase*. **Hearn:** his single outgoing tx **E6b (Hearn → Satoshi 32.51)** already flows through the unified UTXO spend path; it is exact-match (no change), so rotation is inert — the `ReceiveWallet` is for consistency. **Bots stay single-address** (no stored seed — `DerivedAddressWallet` needs a seed; bots use random/registry keypairs). Revisit bots with the gradual-miner-spawning feature. Casino caveat: it sends often (pool payouts), so its change-address count grows fastest — the rescan-cache mitigation pays off there first (ProjectDesignManual Ch. 30.7).
- **OQ-8.3 — player deposit-address rotation.** ✅ **DECIDED — deferred in Basic Mode v1.** Rotating the *incoming* receive address after each external deposit (full HD behavior) is out of scope; the player/casino/Hal UTXO realism is delivered via **change outputs on send** only (coinbase spread is Satoshi-only). Scripted-receive rotation is gated to `RotateCoinbaseAddress` (Satoshi only), so Hal's E4 10-BTC receive lands on his base. The hook is noted for a later version.
- **OQ-8.4 — rescan gap limit.** ✅ **DECIDED — gap limit = 20 (BIP44 convention).** Our strictly-sequential assignment means gaps should never occur, so 20 is a pure safety margin and is educational. Implement cheaply: **one** chain pass collects every used address into a `HashSet<string>`, then derived addresses are probed against the set in O(1) — so "scan 20 past the last hit" is ~20 SHA256 derivations, not 20 full chain scans. (See §2.3.)
- **OQ-8.6 — automatic processes vs. manual balance (DECIDED 2026-06-27) + deferred consistency pass.** Surfaced while wiring 8.2/8.3: a founder's **automatic scripted activity** (the Hearn round-trip, the 10-BTC Satoshi→Hal tx) was rendered in the main wallet as a `pending outgoing`, which reads like a *manual withdrawal the founder ordered* — but founders never transact manually. **Decision:** the **main balance = AVAILABLE (spendable)** = settled holdings not committed to an in-flight automatic process (what could be moved manually now) — shown identically in **FoundersWallets and the Block Explorer** (`AggregateSpendable` / `GetNodeSpendableBalance`), which also makes the two consistent. The automatic scripted events move to a dedicated **"Automatic Activity [DEV]"** panel per founder (`GetNodeScriptedActivity`: each `hist_*` tx with direction + counterparty + pending/✓ status, internal self-change excluded). This is **founder-specific** — for the **player** (8.4) a pending-outgoing *is* manual, so it stays in the player's own wallet. **Deferred big pass (still open):** a single canonical balance model — *available · pending-in · pending-out · immature* — surfaced uniformly across **every** wallet screen (BTCWallet, CasinoFinances, BotsBtcWallets, BlockExplorer, FoundersWallets). Revisit on a wallet-UI polish pass / alongside 8.4.
- **OQ-8.5 — Phase 8.5 (Patoshi mining-forensic view).** ✅ **DECIDED — documented only, not built in Basic Mode v1.** The design stays in the plan (Phase 8.5) as a future/optional flavor; it is **not implemented** in v1. The terminology correction (D0) — keeping "Patoshi pattern" reserved for the mining fingerprint vs. "address non-reuse" — still ships in the docs (Phase 8.6), independent of whether the forensic view is built.
- **OQ-8.7 — network-wide fee activation date (DECIDED 2026-06-27 — deferred to a separate branch).** The scripted historical txs use **fee 0**, while bots/casino attach fees from the start (`ScheduleBotTransactionsAfterBlock` `MinBotFeeBtc=0.1`/`MaxBotFeeBtc`; `CasinoTxFee=0.1`). That's a **dev-time contradiction, accepted as-is for now** — nothing changes in this branch. **Deferred major adaptation (own branch, e.g. `network-fee-activation`):** make the whole network **fee-free until a `FeeActivationDate` ≈ 2009-04-26** (the nearest mined block — just after the 18 Apr Hearn round-trip), then **everyone** starts paying fees. This is historically faithful (early Bitcoin charged no fees) and resolves the contradiction at the root: the April 18 scripted txs are fee-free because *all* participants are, until the activation block. Gate points to flip on that date: bot fee in `ScheduleBotTransactionsAfterBlock`, `CasinoTxFee`, the player's default/selected fee, → 0 before the date; restore after. The candidate-block model is unchanged (it already collects `ΣFee`); this only gates whether a fee is *attached*. Provisional date `2009-04-26`; resolve to the nearest block by timestamp (dates are the source of truth — Q-E1). Recorded across CLAUDE.md, IMPLEMENTATION_ROADMAP, ProjectDesignManual, and `historical-blockchain-events-research.md`.

---

## Appendix A — Full UTXO transaction model (✅ IMPLEMENTED)

**Status:** ✅ **BUILT & in-engine audited** (promoted from deferred — the OQ-8.1 trigger fired the first time a multi-address send couldn't be funded from one address). This supersedes the UTXO-lite stand-in. The design below is the spec that was implemented; deltas: signing uses **sighash = txid** (a single committing hash, rather than a separately serialized sighash), the migration shim kept `Sender`/`Recipient`/`Amount` as `[JsonIgnore]` read-only computed properties, and the account→UTXO transition used a **clean reset** (`WorldFormatVersion`) instead of an in-place chain migration (the old chain has no input→output linkage to replay). See ProjectDesignManual Ch. 30 + the status row above.

**Original scope flag (historical):** This was the *designed* path for OQ-8.1, not part of core Step 8 (8.1–8.6); core Step 8 shipped **UTXO-lite** (single input + a paired change tx). This appendix specified the full multi-input/multi-output refactor so the path was documented and de-risked, to be promoted when fragmentation actually blocked a needed send — which is what happened.

**Design principle:** UTXO-lite is deliberately the on-ramp. Its addresses already behave like single-receive outputs, and a spend already names one source address + emits change to a fresh address. So the full model is mostly **bookkeeping** (index outputs, maintain a UTXO set, allow N inputs) rather than a rethink of wallet semantics — a low-surprise promotion.

### A.1 What it unblocks
- **Multi-input consolidation** (the actual OQ-8.1 driver): pay an amount no single address covers by combining several UTXOs.
- **True coin selection** (greatest-first / branch-and-bound) and **fan-out** (one tx, many recipients).
- A faithful **fee = Σinputs − Σoutputs** definition (vs. today's per-tx `Fee` field).
- Foundation for **Step 10** reorg accounting (UTXO set rollback on a chain switch).

### A.2 New data model (`Models.cs`)

Replace the single `Sender/Recipient/Amount` triple with input/output lists. Keep `Fee`, `Salt`, `TransactionId`, `InputData*`, `IsSpendable` at tx level.

```csharp
public sealed class OutPoint        { public string PrevTxId; public int Vout; }          // references a prior output
public sealed class TxInput         { public OutPoint Source;
                                      public string SignatureBase64;                       // per-input P-256 sig
                                      public string PublicKeyBase64;                        // per-input P-256 pubkey
                                      public string Secp256k1PublicKeyBase64; }             // per-input address-ownership key
public sealed class TxOutput        { public string Address; public decimal Amount; }      // vout = list position

public sealed class Transaction
{
    public List<TxInput>  Inputs  = new();   // empty ⇒ coinbase
    public List<TxOutput> Outputs = new();
    public decimal Fee;                       // = Σinputs − Σoutputs (validated, kept for display)
    public string  Salt, TransactionId, InputDataHex, InputDataText;
    public bool    IsSpendable = true;
}
```

**Migration shim (kept during the transition):** expose computed `Sender` (= first input's resolved address), `Recipient` (= first non-change output), `Amount` (= that output's value) so legacy read-only call sites (Block Explorer, stats) keep compiling until each is ported. Signing/validation use the new fields exclusively from day one.

### A.3 The UTXO set — derived, never persisted

Because *a block is the only commit to disk*, the UTXO set is **rebuilt by replaying the chain** at launch and after any revert-to-last-block (the same place `ChainIsValid` already walks every block):

```
utxo: Dictionary<OutPoint, (TxOutput out, int blockHeight, bool isCoinbase)>
replay each block oldest→newest:
    for each tx: remove every input's OutPoint;  add every output as a fresh OutPoint
```

Confirmed-balance and maturity both read from this set — `GetAddressData` (account model) is **replaced** by `GetAddressUtxos(address)` = the subset whose `out.Address == address`. Coinbase outputs are spendable only after `CoinbaseMaturity` confirmations (height check against the current tip).

### A.4 Validation rewrite (`BlockchainService`)

Per tx (coinbase = zero inputs, bypasses input checks):
1. Every input's `OutPoint` exists in the UTXO set, is **unspent**, and (if coinbase-sourced) **mature**.
2. **No double-spend**: no two pending/in-block txs spend the same `OutPoint` (replaces the per-address balance check).
3. `Σinputs ≥ Σoutputs`; `Fee = Σinputs − Σoutputs ≥ 0`.
4. **Per-input ownership + signature**: for each input, `DeriveAddressFromPublicKey(input.Secp256k1PublicKeyBase64)` == the referenced output's `Address`, and `Verify(sighash, input.SignatureBase64, input.PublicKeyBase64)`.
5. **Drop** the `Sender == Recipient` rejection (`AddTransactionToPendingTransactions:210`) — change-to-own-wallet is now legitimate and expressed as a distinct output address anyway.

### A.5 Signing rewrite (`NodeAgent` / `CryptoUtils`)

Signing moves from one tx-level signature to **one signature per input** (each signed by the key controlling that input's referenced output — enabling inputs across multiple derived addresses, the consolidation case). The signed message is a **sighash** committing to the whole tx so inputs/outputs can't be reshuffled:

```
sighash = Sha256( canonical(Inputs[].Source) | canonical(Outputs[].{Address,Amount}) | Fee | Salt )
TransactionId = ComputeTransactionId = double-SHA256 of the same canonical form
```

`DerivedAddressWallet` (Phase 8.1) supplies the per-input keypair via `TryFindSpendingContext(address)`; one tx may pull keypairs for several of its own derived addresses.

### A.6 txid, payload & Merkle
- `ComputeTransactionId` (currently hashes `amount|sender|recipient|fee|inputDataHex|isSpendable|salt`, `:140-157`) → hash the canonical input/output serialization instead. **Merkle leaf stays = txid** (no change to `MerkleTree`/header hashing).
- `BuildTransactionPayload` (`:160-165`) → the §A.5 sighash.

### A.7 Coinbase & genesis
- Coinbase = **input-less** tx with one output (reward + Σfees) to the miner's fresh derived address (Phase 8.2). The `CoinbaseSender = "00"` sentinel is replaced by "`Inputs.Count == 0`" detection (keep `"00"` as a display label only).
- Genesis stays pinned (its single output to Satoshi's `addr(0)`), now just expressed as an input-less tx — `ChainIsValid` validates it as a coinbase with no genesis replay (unchanged contract).

### A.8 Surfaces touched
| File | Change |
|---|---|
| `Models.cs` | `OutPoint`/`TxInput`/`TxOutput`; `Transaction` inputs/outputs + migration shim |
| `BlockchainService.cs` | UTXO set build/replay; `GetAddressUtxos`; validation (A.4); `ComputeTransactionId`/payload (A.6); coinbase detection (A.7) |
| `BlockTemplateBuilder.cs` | input-less coinbase output; tx selection by fee = Σin−Σout; double-spend guard within the template |
| `NodeAgent.cs` / `CryptoUtils.cs` | per-input signing (A.5); multi-UTXO coin selection |
| `NetworkRoot.cs` | `CreateSpendWithChange` → real N-input/2-output build; balance via UTXO set; chain-replay on load/revert |
| `Screens/BlockExplorer/*`, stats | read inputs/outputs (via shim first, then ported) |

### A.9 Sequencing & risk
- **Risk: high** — it rewrites the validation/signing core every block and tx flows through. Do it on its own branch with the existing `ChainIsValid`/durability runs as the regression gate.
- **Prereq**: core Step 8 (8.1–8.4) shipped, so derived-address wallets + change semantics already exist and the UTXO promotion is bookkeeping.
- **Recommended trigger**: the first time UTXO-lite can't fund a send from a single address, **or** when Step 10 begins (shared validation/rollback core). Until then UTXO-lite is sufficient and this stays a designed-but-unbuilt appendix.

---

*Created: 2026-06-26 — implements roadmap Step 8 (UTXO realism / address non-reuse) on the Step-4 candidate engine + Step-7 founders. Corrects the "Patoshi pattern" misnomer (D0) and resolves the §6 address-reuse research + the deferred E8 change output. Appendix A designs the deferred full multi-input/multi-output UTXO refactor (OQ-8.1). Pairs with `historical-blockchain-events-research.md` and `btc-wallet-system-plan.md`.*

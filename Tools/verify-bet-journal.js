#!/usr/bin/env node
//
// Mini-plan 08 P3 — bet-journal timestamp fidelity scanner.
//
// WHAT IT VERIFIES, and why each assertion has the shape it has:
//
//   A1  NO TWO BETS SHARE AN INSTANT, at DateTime's own resolution (100 ns ticks). The defect this plan
//       exists to fix stamped every bet settled in one frame with the SAME instant — measured at 7,926 bets
//       across 949 distinct timestamps, groups of 7-10 spaced 150.00 game-seconds (mini-plan 06 section
//       9.10c). After the writer fix each bet is back-dated by its own interval, so none may repeat.
//
//   A1b NEAR-COLLISIONS: consecutive bets 1 to NEAR_COLLISION_TICKS apart. Technically distinct instants,
//       and reported on their own line because they are the SAME defect as an exact duplicate, separated
//       only by which way a double happened to truncate. See NEAR_COLLISION_TICKS for why the threshold has
//       no false positives in practice.
//
//   A2  MEDIAN spacing equals SpeedMultiplier / credits game-seconds. NOT "uniform to the tick" — section
//       2.2 of the plan is explicit that the fix leaves a bounded frame-boundary jitter (the accumulator
//       carries a remainder, so the gap across a frame edge is not the nominal interval). Asserting
//       uniformity would fail a correct implementation.
//
//   A3  STRICTLY MONOTONIC timestamps in write order, at tick resolution. The P7 check from mini-plan 06
//       section 9.2, promoted to a standing regression test.
//
//   A4  NO BET TIMESTAMPED AFTER THE CLOCK. Section 2.2 explains why this is the property to guard rather
//       than uniformity: a future-dated bet is a worse defect than any amount of jitter.
//
// WHY A1 AND A3 ARE TICK-RESOLUTION (changed 2026-09-13). The first version compared everything at
// MILLISECONDS, because A4's block join needs block timestamps, which are Unix ms, and one truncation served
// all four assertions. That made A3 blind to any regression smaller than a millisecond, and made A1 stricter
// than the property it names. A tick-level audit after the 99-credit escalation found nothing hidden — zero
// regressions — but also found 416 pairs exactly ONE tick apart that the millisecond comparison had been
// counting as duplicates. Milliseconds are now used for the A4 join and for nothing else.
//
// A4 IS CHECKABLE, and the reason is worth stating because it is not obvious. The journal does not record
// which bet mined which block, so "no bet after the clock" looks undecidable after the fact. But
// `SimulationService.ExecutePlayerBetOnce` derives the block's timestamp from the SAME `tsUtc` it gave the
// bet — `tsMs = new DateTimeOffset(tsUtc).ToUnixTimeMilliseconds()` — so a player-mined block's timestamp
// EQUALS its mining bet's timestamp to the millisecond. That join recovers the exact commit points, and
// CLAUDE.md's canonical rule (the clock equals the timestamp of the block that most recently defines the
// checkpointed world) then makes each block timestamp a clock reading we can test bets against.
//
// USAGE
//   node Tools/verify-bet-journal.js                  # whole journal, per-segment breakdown
//   node Tools/verify-bet-journal.js --last 20000     # only the most recent N bets (one playtest)
//   node Tools/verify-bet-journal.js --credits 99     # assert A2 against a known hardware credit count
//   node Tools/verify-bet-journal.js --dir "C:/path"  # a journal archived out of user://
//
// READ THE PER-SEGMENT TABLE, NOT ONLY THE TOTAL. This journal spans several playtests at different credit
// counts AND both sides of the writer fix, so a single median over all of it is an average of unrelated
// regimes and means nothing. The per-segment implied-credit column shows where the configuration changed
// and where the fix took effect.

'use strict';

const fs = require('fs');
const path = require('path');

// .NET ticks at the Unix epoch. Journal timestamps are ISO strings, block timestamps are Unix ms; this is the
// constant that puts them in the same units.
const TICKS_AT_UNIX_EPOCH = 621355968000000000n;
const TICKS_PER_SECOND = 10000000;
const TICKS_PER_MS = 10000;

// The calendar advances this many game-seconds per simulated second while a delegated autobet runs. It is
// `CalendarTimeService.SpeedMultiplier`, which SimulationService sets to 100 for the sim. Named here rather
// than written as a bare 100 at the sites that use it (Standing Convention 15).
const SPEED_MULTIPLIER = 100;

// A gap larger than this many nominal intervals is excluded from A2's spacing statistics. Without the exclusion the
// max and the mean are dominated by hours of game time in which nobody bet, which says nothing about the writer.
//
// ⚠ An excluded gap is NOT necessarily a session break, and this comment used to say it was. A stopped session, a
// scene change or an idle world produces one — but so does a continuous autobet under a saturated backlog: the
// calendar multiplies this frame's delta by a retention ratio measured on the previous frame, so a step UP in
// frame time advances the clock past what the capped batch can fill, and the remainder is an intra-run hole.
// Measured 2026-09-13 at 99 credits x 3000X: 139 such holes in one uninterrupted run, 10-69 game-seconds each,
// 1.49% of game time — the up-step half of a 0.62% clock overspend (mini-plan 08, "session breaks are not
// breaks"). The exclusion is still right for A2; only the label was wrong.
const BREAK_FACTOR = 10;

// Consecutive bets this close are the clamp-boundary collision's fingerprint, not frame-boundary jitter. The
// only mechanism observed to produce a gap of a handful of ticks is a batch reaching exactly back onto the
// previous frame's clock, with DateTime.AddSeconds truncating the fractional tick; every such pair measured
// on 2026-09-13 was exactly 0 or exactly 1 tick apart. Legitimate jitter (plan section 2.2) spreads a
// frame-boundary gap over roughly (0, 2 x nominal), so at 99 credits it lands within 10 ticks (1 us) with
// probability around 5e-7 per frame boundary, and far less at lower credit counts.
const NEAR_COLLISION_TICKS = 10;

const ISO_RE = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?Z$/;

function parseArgs(argv) {
	const out = { dir: null, last: null, credits: null };
	for (let i = 2; i < argv.length; i++) {
		const a = argv[i];
		if (a === '--dir') out.dir = argv[++i];
		else if (a === '--last') out.last = Number(argv[++i]);
		else if (a === '--credits') out.credits = Number(argv[++i]);
		else if (a === '--help' || a === '-h') { printUsage(); process.exit(0); }
		else { console.error(`unknown argument: ${a}`); printUsage(); process.exit(2); }
	}
	return out;
}

function printUsage() {
	console.log('usage: node Tools/verify-bet-journal.js [--dir <userdata>] [--last N] [--credits N]');
}

function defaultUserDir() {
	const appData = process.env.APPDATA;
	if (!appData) return null;
	return path.join(appData, 'Godot', 'app_userdata', 'GamblingMiner');
}

// ISO-8601 with .NET's 7 fractional digits -> Unix milliseconds, TRUNCATED exactly as
// DateTimeOffset.ToUnixTimeMilliseconds truncates. Used ONLY for the A4 block join. Date.parse would round the
// sub-millisecond digits and break the join for any bet landing on a .xxxx5 boundary.
function isoToUnixMs(iso) {
	const m = ISO_RE.exec(iso);
	if (!m) return null;
	const whole = Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]);
	const frac = m[7] ? m[7].padEnd(7, '0').slice(0, 7) : '0000000';
	return whole + Math.floor(Number(frac) / TICKS_PER_MS);
}

// ISO-8601 -> absolute .NET ticks, exactly. BigInt because absolute tick counts (~6.3e17) exceed the 2^53 a
// double represents exactly; a Number here would silently merge adjacent ticks and manufacture the very
// duplicates A1 looks for.
function isoToTicks(iso) {
	const m = ISO_RE.exec(iso);
	if (!m) return null;
	const whole = BigInt(Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6])) * BigInt(TICKS_PER_MS);
	const frac = BigInt(m[7] ? m[7].padEnd(7, '0').slice(0, 7) : '0');
	return TICKS_AT_UNIX_EPOCH + whole + frac;
}

function ticksToUnixMs(ticks) {
	return Number((BigInt(ticks) - TICKS_AT_UNIX_EPOCH) / BigInt(TICKS_PER_MS));
}

function fmtMs(ms) {
	return new Date(ms).toISOString().replace('T', ' ').replace('Z', '');
}

// Verdict label padded to the column the other assertion lines use.
function label(text) {
	return `${text} ${'.'.repeat(Math.max(1, 28 - text.length))} `;
}

function loadSegments(dir) {
	const files = fs.readdirSync(dir)
		.filter(f => /^bet_history_\d+\.jsonl$/.test(f))
		.sort();  // zero-padded, so lexical order IS write order
	if (files.length === 0) return null;

	const bets = [];
	let deposits = 0;
	let malformed = 0;
	const segments = [];

	for (const file of files) {
		const startIndex = bets.length;
		const text = fs.readFileSync(path.join(dir, file), 'utf8');
		for (const line of text.split('\n')) {
			if (!line.trim()) continue;
			let rec;
			try { rec = JSON.parse(line); } catch { malformed++; continue; }
			if (rec.Type === 'deposit') { deposits++; continue; }
			const bet = rec.Bet;
			if (!bet || !bet.TimestampUtc) { malformed++; continue; }
			const ms = isoToUnixMs(bet.TimestampUtc);
			const ticks = isoToTicks(bet.TimestampUtc);
			if (ms === null || ticks === null) { malformed++; continue; }
			bets.push({ ms, ticks, id: bet.Id, iso: bet.TimestampUtc });
		}
		segments.push({ file, startIndex, endIndex: bets.length });
	}

	// Rebase to ticks relative to the first bet, as a plain Number. The retained journal spans days to months of
	// game time — at most ~1e14 ticks — far inside the 2^53 a double holds exactly, and Number arithmetic keeps a
	// 200k-record scan fast. The absolute BigInt is dropped once rebased.
	const base = bets.length ? bets[0].ticks : 0n;
	for (const b of bets) {
		b.t = Number(b.ticks - base);
		delete b.ticks;
	}

	return { bets, deposits, malformed, segments, files };
}

// ── A1 + A1b + A2 + A3 over one contiguous slice, in write order, at tick resolution ──────────────────────
function analyse(bets, from, to, nominalSeconds) {
	const n = to - from;
	const res = {
		count: n,
		distinctTimestamps: 0,
		groups: 0, groupedBets: 0, maxGroup: 0, groupSizes: new Map(),
		nearCollisions: 0,
		regressions: 0, firstRegression: null,
		gaps: [], breaks: 0,
		medianGapS: null, impliedCredits: null,
		atNominal: 0,
	};
	if (n <= 0) return res;

	// A1 exact duplicates, A1b near-collisions, A3 regressions. One pass, write order preserved.
	let runStart = from;
	for (let i = from + 1; i <= to; i++) {
		const ended = i === to || bets[i].t !== bets[runStart].t;
		if (i < to) {
			const d = bets[i].t - bets[i - 1].t;
			if (d < 0) {
				res.regressions++;
				if (!res.firstRegression) {
					res.firstRegression = { index: i, prev: bets[i - 1].iso, cur: bets[i].iso, ticks: d };
				}
			} else if (d > 0 && d <= NEAR_COLLISION_TICKS) {
				res.nearCollisions++;
			}
		}
		if (ended) {
			const size = i - runStart;
			res.distinctTimestamps++;
			if (size > 1) {
				res.groups++;
				res.groupedBets += size;
				if (size > res.maxGroup) res.maxGroup = size;
				res.groupSizes.set(size, (res.groupSizes.get(size) || 0) + 1);
			}
			runStart = i;
		}
	}

	// A2 — spacing between DISTINCT instants. Zero-gaps are A1's business, not A2's; including them would drag the
	// median to 0 the moment any duplication exists and hide the real spacing.
	const nominalTicks = nominalSeconds ? nominalSeconds * TICKS_PER_SECOND : null;
	const breakThreshold = nominalTicks ? nominalTicks * BREAK_FACTOR : Infinity;
	for (let i = from + 1; i < to; i++) {
		const d = bets[i].t - bets[i - 1].t;
		if (d <= 0) continue;
		if (d > breakThreshold) { res.breaks++; continue; }
		res.gaps.push(d);
	}
	if (res.gaps.length) {
		const sorted = res.gaps.slice().sort((a, b) => a - b);
		res.medianGapS = sorted[Math.floor(sorted.length / 2)] / TICKS_PER_SECOND;
		res.impliedCredits = res.medianGapS > 0 ? SPEED_MULTIPLIER / res.medianGapS : null;
		if (nominalTicks) {
			res.atNominal = res.gaps.filter(d => Math.abs(d - nominalTicks) <= TICKS_PER_MS).length;
		}
	}
	return res;
}

function main() {
	const args = parseArgs(process.argv);
	const dir = args.dir || defaultUserDir();
	if (!dir || !fs.existsSync(dir)) {
		console.error(`journal directory not found: ${dir}`);
		console.error('pass --dir <path> if the world lives somewhere else.');
		process.exit(2);
	}

	const loaded = loadSegments(dir);
	if (!loaded) {
		console.error(`no bet_history_*.jsonl segments in ${dir}`);
		process.exit(2);
	}
	const { bets, deposits, malformed, segments, files } = loaded;

	console.log('=== Mini-plan 08 P3 — bet-journal timestamp fidelity ===');
	console.log(`journal   ${dir}`);
	console.log(`segments  ${files.length} files, ${bets.length.toLocaleString()} bet records, ` +
		`${deposits} deposit records${malformed ? `, ${malformed} MALFORMED LINES` : ''}`);
	if (bets.length === 0) { console.log('nothing to check.'); return; }
	console.log(`span      ${fmtMs(bets[0].ms)}  ->  ${fmtMs(bets[bets.length - 1].ms)}  (game time, UTC)`);

	const nominal = args.credits ? SPEED_MULTIPLIER / args.credits : null;
	if (nominal) {
		console.log(`expected  ${nominal.toFixed(4)} game-seconds between bets ` +
			`(SpeedMultiplier ${SPEED_MULTIPLIER} / ${args.credits} credits)`);
	}

	// ── Per-segment table. The implied-credits column is the point: it is how a reader sees that two segments are
	// different experiments and must not be averaged together.
	console.log('\n--- per segment (write order, tick resolution) ---');
	console.log('segment                    bets   distinct  groups  maxGrp  nearCol   medianGap  impliedCr  regress');
	for (const seg of segments) {
		const a = analyse(bets, seg.startIndex, seg.endIndex, nominal);
		if (a.count === 0) continue;
		const med = a.medianGapS === null ? '     -   ' : `${a.medianGapS.toFixed(3).padStart(9)}`;
		const cr = a.impliedCredits === null ? '   -  ' : `${a.impliedCredits.toFixed(1).padStart(6)}`;
		console.log(
			`${seg.file.padEnd(24)} ${String(a.count).padStart(6)} ` +
			`${String(a.distinctTimestamps).padStart(10)} ${String(a.groups).padStart(7)} ` +
			`${String(a.maxGroup).padStart(7)} ${String(a.nearCollisions).padStart(8)} ${med} ${cr}   ` +
			`${String(a.regressions).padStart(6)}`);
	}

	// ── The scoped verdict.
	const from = args.last ? Math.max(0, bets.length - args.last) : 0;
	const scope = args.last ? `most recent ${(bets.length - from).toLocaleString()} bets` : 'whole journal';
	const a = analyse(bets, from, bets.length, nominal);

	console.log(`\n--- assertions over the ${scope} ---`);

	// A1
	const a1ok = a.groups === 0;
	console.log(`${label('A1 duplicate instants')}${a1ok ? 'PASS' : 'FAIL'}  ` +
		`${a.groups.toLocaleString()} groups covering ${a.groupedBets.toLocaleString()} bets` +
		(a.maxGroup ? `, largest ${a.maxGroup}` : '') + ' (tick resolution)');
	if (!a1ok) {
		const sizes = [...a.groupSizes.entries()].sort((x, y) => x[0] - y[0])
			.map(([size, count]) => `${size}x${count}`).join(' ');
		console.log(`     group size histogram: ${sizes}`);
		console.log('     NOTE: records written BEFORE the writer fix are permanently grouped and are not a');
		console.log('     regression — mini-plan 08 section 5 refuses to rewrite history. Use --last to scope');
		console.log('     to a post-fix playtest, and read the per-segment table above for where it changes.');
	}

	// A1b
	const nearOk = a.nearCollisions === 0;
	console.log(`${label('A1b near-collisions')}${nearOk ? 'PASS' : 'FAIL'}  ` +
		`${a.nearCollisions.toLocaleString()} consecutive pairs 1-${NEAR_COLLISION_TICKS} ticks apart`);
	if (!nearOk) {
		console.log('     the same defect as a duplicate, separated only by which way AddSeconds truncated —');
		console.log('     legitimate jitter lands this close about once in a million frame boundaries.');
	}

	// A2
	if (nominal) {
		const withinOnePercent = a.medianGapS !== null && Math.abs(a.medianGapS - nominal) / nominal < 0.01;
		console.log(`${label('A2 median spacing')}${withinOnePercent ? 'PASS' : 'CHECK'}  ` +
			`${a.medianGapS === null ? 'n/a' : a.medianGapS.toFixed(4)} s vs ${nominal.toFixed(4)} s expected` +
			`  (${a.atNominal.toLocaleString()} of ${a.gaps.length.toLocaleString()} gaps within 1 ms of nominal)`);
	} else {
		console.log(`${label('A2 median spacing')}INFO  ${a.medianGapS === null ? 'n/a' : a.medianGapS.toFixed(4)} s` +
			` => ${a.impliedCredits === null ? '?' : a.impliedCredits.toFixed(1)} implied credits` +
			'   (pass --credits N to assert)');
	}
	console.log(`     ${a.breaks.toLocaleString()} gaps exceeded ${BREAK_FACTOR}x nominal and were excluded from A2. NOT ` +
		'necessarily session breaks:');
	console.log('     under a saturated backlog, a step up in frame time leaves an intra-run hole (mini-plan 08:');
	console.log('     the lagged-throttle clock overspend).');

	// A3
	console.log(`${label('A3 monotonic')}${a.regressions === 0 ? 'PASS' : 'FAIL'}  ` +
		`${a.regressions.toLocaleString()} regressions (tick resolution)`);
	if (a.firstRegression) {
		console.log(`     first at write index ${a.firstRegression.index}: ` +
			`${a.firstRegression.prev} -> ${a.firstRegression.cur} (${a.firstRegression.ticks} ticks)`);
	}

	// A4 — the block join, at milliseconds because block timestamps are Unix ms.
	reportBlockJoin(dir, bets, from);

	console.log('\nA1, A1b and A3 are absolute. A2 is a MEDIAN and is expected to carry a frame-boundary tail');
	console.log('(plan section 2.2) — a distribution uniform to the tick would mean this scanner is measuring');
	console.log('something other than what the engine implements.');
}

function reportBlockJoin(dir, bets, from) {
	const chainPath = path.join(dir, 'blockchain', 'state.json');
	const cpPath = path.join(dir, 'block_session_checkpoint.json');
	if (!fs.existsSync(chainPath)) {
		console.log('A4 no bet after the clock ... SKIP  blockchain/state.json not found');
		return;
	}

	let chain;
	try { chain = JSON.parse(fs.readFileSync(chainPath, 'utf8')).PlayerChain; }
	catch (e) { console.log(`A4 no bet after the clock ... SKIP  unreadable chain: ${e.message}`); return; }
	if (!chain) { console.log('A4 no bet after the clock ... SKIP  no PlayerChain in state.json'); return; }

	// PlayerChain is an object keyed by block index, not an array.
	const blocks = Object.values(chain)
		.filter(b => b && typeof b.Timestamp === 'number')
		.sort((x, y) => x.Index - y.Index);
	const playerBlocks = blocks.filter(b => b.MinedByNodeId === 'player');

	// The join: a player-mined block takes its timestamp from the same tsUtc as its mining bet, so an exact
	// millisecond match must exist. A miss means the two diverged — which is precisely the failure the
	// back-dating fix could introduce if it ever shifted the LAST bet of a frame off the clock.
	const betMs = new Set();
	for (let i = from; i < bets.length; i++) betMs.add(bets[i].ms);
	const oldest = bets[from].ms;

	let matched = 0, missed = 0, outOfRange = 0;
	const misses = [];
	for (const b of playerBlocks) {
		if (b.Timestamp < oldest) { outOfRange++; continue; }  // block predates the scoped/retained journal
		if (betMs.has(b.Timestamp)) matched++;
		else { missed++; if (misses.length < 5) misses.push(b); }
	}

	const maxBetMs = bets[bets.length - 1].ms;
	const tipMs = blocks.length ? blocks[blocks.length - 1].Timestamp : null;

	const verdict = missed === 0 ? (matched > 0 ? 'PASS' : 'SKIP') : 'FAIL';
	console.log(`A4 block/bet timestamp join . ${verdict}  ` +
		`${matched} of ${matched + missed} player-mined blocks matched a bet to the millisecond` +
		(outOfRange ? `  (${outOfRange} predate the retained journal)` : ''));
	for (const b of misses) {
		console.log(`     block #${b.Index} at ${fmtMs(b.Timestamp)} has no bet at that instant`);
	}

	if (tipMs !== null) {
		const aheadS = (maxBetMs - tipMs) / 1000;
		console.log(`     chain tip block #${blocks[blocks.length - 1].Index} at ${fmtMs(tipMs)}; ` +
			`newest bet ${fmtMs(maxBetMs)}`);
		if (aheadS > 0) {
			console.log(`     newest bet is ${aheadS.toFixed(0)} game-seconds past the tip — EXPECTED: those bets`);
			console.log('     are uncommitted (no block has closed over them yet) and a restart discards them.');
		}
	}

	if (fs.existsSync(cpPath)) {
		try {
			const cp = JSON.parse(fs.readFileSync(cpPath, 'utf8'));
			if (cp.HistoryCheckpointUtcTicks) {
				const boundary = ticksToUnixMs(cp.HistoryCheckpointUtcTicks);
				let after = 0;
				for (let i = from; i < bets.length; i++) if (bets[i].ms > boundary) after++;
				console.log(`     history checkpoint boundary ${fmtMs(boundary)} — ` +
					`${after.toLocaleString()} bets sit after it (the uncommitted tail).`);
			}
		} catch { /* the checkpoint is a courtesy reading here, never the verdict */ }
	}
}

main();

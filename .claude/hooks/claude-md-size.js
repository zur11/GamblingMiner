#!/usr/bin/env node
/**
 * Dep-01 D3 — the CLAUDE.md size guard.
 *
 * Why it exists: in August 2026 CLAUDE.md reached 228,348 characters and nobody was aware of it, while
 * every message of every session paid for it. The failure was not that the file was long — it is that
 * NOTHING MEASURED IT. This is the measurement.
 *
 * It is wired to TWO events, deliberately, because the file grows two ways:
 *
 *   PostToolUse (Edit|Write) — catches Claude's own edit, in the same turn, which is what the Document
 *                              Policy's "say so in that same reply" actually requires.
 *   SessionStart             — catches everything else. No hook observes a manual edit in an editor, so
 *                              a PostToolUse-only guard would report a clean bill of health while the
 *                              developer's own edits doubled the file. A guard that watches one of the
 *                              two ways a file grows is not a guard.
 *
 * Silent under the warning threshold, always. A noisy hook gets ignored, and an ignored guard is worse
 * than none because it is believed to be working.
 */

const fs = require("fs");
const path = require("path");

const TARGET = 60000;
const WARN = 100000;
const HARD = 150000;

const projectDir = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const claudeMd = path.join(projectDir, "CLAUDE.md");
// Mirrors the Document Policy's scoped suspension: while a depuration plan is running the file is
// KNOWN to be over and is being actively reduced, so the warning is noise. Only the hard limit applies.
// Creating and deleting this marker is an explicit act, which is what keeps the exemption honest.
const suspendMarker = path.join(projectDir, ".claude", "hooks", ".depuration-active");

function readStdin() {
	try {
		return fs.readFileSync(0, "utf8");
	} catch {
		return "";
	}
}

function main() {
	let payload = {};
	try {
		payload = JSON.parse(readStdin() || "{}");
	} catch {
		payload = {};
	}

	// PostToolUse carries the edited path. SessionStart does not — and then we always measure.
	const editedPath = payload?.tool_input?.file_path;
	if (editedPath && path.basename(editedPath) !== "CLAUDE.md") {
		process.exit(0); // not our file: silent, always
	}

	let size;
	try {
		size = fs.statSync(claudeMd).size;
	} catch {
		process.exit(0); // no CLAUDE.md here — nothing to police
	}

	const suspended = fs.existsSync(suspendMarker);
	const floor = suspended ? HARD : WARN;
	if (size < floor) {
		process.exit(0); // silence is the normal case
	}

	const over = size - TARGET;
	const pct = Math.round((size / TARGET - 1) * 100);
	const lines = [];

	if (size >= HARD) {
		lines.push(
			`[CLAUDE.md] ${size.toLocaleString("en-US")} characters — PAST THE ${HARD.toLocaleString("en-US")} HARD LIMIT.`,
			`This is the point where Claude Code reports the file's size at startup, and every message of`,
			`every session pays for it. Stop and extract before continuing with anything else.`
		);
	} else {
		lines.push(
			`[CLAUDE.md] ${size.toLocaleString("en-US")} characters — past the ${WARN.toLocaleString("en-US")} warning threshold.`,
			`That is ${over.toLocaleString("en-US")} over the ${TARGET.toLocaleString("en-US")} target (+${pct}%).`
		);
	}

	lines.push(
		`Per the Document Policy in CLAUDE.md: say so in this same reply and propose what to extract.`,
		`What belongs here is permanent instruction; specifications, design history and status go to Documentation/.`
	);

	if (suspended) {
		lines.push(`(A depuration plan is marked active, so the ${WARN.toLocaleString("en-US")} warning is suspended — this fired on the hard limit.)`);
	}

	// Exit 2 puts stderr in front of Claude for this event. The newer documented path is exit 0 with a
	// hookSpecificOutput.additionalContext JSON payload — chosen against, deliberately: if that form is
	// unsupported by the running version the warning vanishes SILENTLY, which is the precise failure this
	// guard exists to prevent. Exit 2 either works or is visibly noisy; it cannot fail quietly.
	process.stderr.write(lines.join("\n") + "\n");
	process.exit(2);
}

main();

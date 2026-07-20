using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace GodotBlockchainPort.Blockchain;

// Step 14 (ND.8b.1, 2026-07-19) — loader for the company roster landed at ND.8b.0
// (Data/Companies/company_roster.csv, D-ND8.36…39). Pure static, self-loading (unlike
// NetworkFeePolicy/the non-miner intro schedule, this CSV has no dependency on the network
// dataset's own row iteration, so it does not need to be "pushed" by BtcNetworkDataService —
// it opens its own file via the same static Godot FileAccess API BtcNetworkDataService/
// BtcMarketDataService use). Read-only over a static asset: no persistence, no checkpoint
// coverage, no world-reset delete-list entry needed (the BtcNetworkDataService precedent).
public static class CompanyRoster
{
    private const string CsvPath = "res://Data/Companies/company_roster.csv";

    private static bool _loaded;
    private static List<CompanyRecord> _all = [];
    // D-ND8.37 hybrid intro mapping (built at BtcNetworkDataService.ComputeNonMinerIntroSchedule)
    // and the ComputeAuctionLedger identity lookup both depend on this being sorted ascending by
    // AppearanceDateLocal: BotWalletRegistry.NonMinerBots[i] <-> Auctionable[i], slot-for-slot.
    private static List<CompanyRecord> _auctionable = [];
    private static List<CompanyRecord> _nonAuctionable = [];

    public static IReadOnlyList<CompanyRecord> All => _all;
    public static IReadOnlyList<CompanyRecord> Auctionable => _auctionable;
    public static IReadOnlyList<CompanyRecord> NonAuctionable => _nonAuctionable;

    public static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        Load();
    }

    // The non-miner-index <-> company pairing (D-ND8.37): non_miner_{index+1} (BotWalletRegistry's
    // fixed creation order) always corresponds to Auctionable[index].
    public static CompanyRecord? ForNonMinerIndex(int index)
    {
        EnsureLoaded();
        return index >= 0 && index < _auctionable.Count ? _auctionable[index] : null;
    }

    public static CompanyRecord? ByCompanyId(string companyId)
    {
        EnsureLoaded();
        return _all.FirstOrDefault(c => c.CompanyId == companyId);
    }

    private static void Load()
    {
        using FileAccess file = FileAccess.Open(CsvPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"[CompanyRoster] Could not open {CsvPath} — company roster unavailable.");
            return;
        }

        string[] lines = file.GetAsText().Split('\n');
        var all = new List<CompanyRecord>(lines.Length);

        // Header: company_id,display_name,currency_band,market_category,appearance_date,auctionable,
        // inflow_weight,expansion_date,expansion_multiplier,anchor,notes
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r', '\n');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Notes are deliberately comma-free (ND.8b.0 build note) so a plain split parses safely.
            string[] cols = line.Split(',');
            if (cols.Length < 11)
            {
                continue;
            }

            all.Add(new CompanyRecord(
                CompanyId: cols[0].Trim(),
                DisplayName: cols[1].Trim(),
                CurrencyBand: cols[2].Trim(),
                MarketCategory: cols[3].Trim(),
                AppearanceDateLocal: ParseDate(cols[4]),
                Auctionable: bool.Parse(cols[5].Trim()),
                InflowWeight: int.Parse(cols[6].Trim(), CultureInfo.InvariantCulture),
                ExpansionDateLocal: ParseNullableDate(cols[7]),
                ExpansionMultiplier: ParseNullableDecimal(cols[8]),
                Anchor: cols[9].Trim(),
                Notes: cols[10].Trim()));
        }

        _all = all;
        _auctionable = all.Where(c => c.Auctionable).OrderBy(c => c.AppearanceDateLocal).ToList();
        _nonAuctionable = all.Where(c => !c.Auctionable).OrderBy(c => c.AppearanceDateLocal).ToList();

        GD.Print($"[CompanyRoster] Loaded {_all.Count} companies " +
            $"({_auctionable.Count} auctionable, {_nonAuctionable.Count} non-auctionable).");
    }

    private static DateTime ParseDate(string raw) => DateTime.SpecifyKind(
        DateTime.ParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None),
        DateTimeKind.Local);

    private static DateTime? ParseNullableDate(string raw)
    {
        string trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : ParseDate(trimmed);
    }

    private static decimal? ParseNullableDecimal(string raw)
    {
        string trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : decimal.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

// One row of Data/Companies/company_roster.csv (§12.4.6d of the step14 plan). CurrencyBand is
// "CB1".."CB5"; MarketCategory is "official" | "light_grey" | "dark_grey" | "black"; Anchor is
// "real" | "parody" | "fictional". ExpansionDateLocal/ExpansionMultiplier are null for the 37 rows
// with no scheduled expansion event (D-ND8.36, consumed starting at ND.8b.5).
public readonly record struct CompanyRecord(
    string CompanyId,
    string DisplayName,
    string CurrencyBand,
    string MarketCategory,
    DateTime AppearanceDateLocal,
    bool Auctionable,
    int InflowWeight,
    DateTime? ExpansionDateLocal,
    decimal? ExpansionMultiplier,
    string Anchor,
    string Notes);

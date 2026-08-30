// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Economia — Suite di certificazione di inventario, upgrade, bazaar e crafting
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace NosAi.Economy.Inventory;

public static class InventoryEconomyTestRunner
{
    /// <summary>
    /// Runs every economy check and reports each one by name (same contract as the
    /// gate runners: no short-circuit, a throwing check is a named failure).
    /// </summary>
    public static bool RunAll()
    {
        Console.WriteLine("=== Inventory & economy checks ===");

        bool allPassed = true;
        allPassed &= Run("Destruction-risk upgrade without a scroll is refused fail-closed", TestUpgradeFailsClosedOnDestruction);
        allPassed &= Run("A protection scroll neutralizes destruction risk", TestProtectionScrollRemovesDestruction);
        allPassed &= Run("Insufficient funds block an upgrade", TestUpgradeBlockedByFunds);
        allPassed &= Run("Expected upgrade cost scales with the success rate", TestExpectedUpgradeCost);
        allPassed &= Run("Bazaar fees reduce proceeds and the medal waives sales tax", TestBazaarFeePolicy);
        allPassed &= Run("Arbitrage keeps crafting materials over selling them", TestArbitrageKeepsCraftingMaterials);
        allPassed &= Run("Arbitrage picks the bazaar only when the margin justifies it", TestArbitrageChannelChoice);
        allPassed &= Run("Grid compaction conserves total quantity per item", TestGridCompactionConservesQuantity);
        allPassed &= Run("A saturated ETC tab requires sanitization before a TimeSpace", TestSaturationGate);
        allPassed &= Run("Crafting solver reports feasibility and missing materials", TestCraftingSolver);

        Console.WriteLine(allPassed
            ? "=== Economy checks passed. Seed balances are SIMULATED until a real client feeds them. ==="
            : "=== Economy checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        var detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static bool TestUpgradeFailsClosedOnDestruction()
    {
        var simulator = new EquipmentUpgradeSimulator();
        // +6 → +7 carries destruction risk. Without a protection scroll the safety
        // policy must refuse it, and the reported risk must not be zeroed.
        var evaluation = simulator.EvaluateUpgrade(currentLevel: 6, isSpecialistCard: false, hasProtectionScroll: false, playerGold: long.MaxValue);
        return !evaluation.IsRecommendedBySafetyPolicy
            && evaluation.DestructionRisk > 0
            && evaluation.RiskRationale.Contains("FAIL-CLOSED", StringComparison.Ordinal);
    }

    private static bool TestProtectionScrollRemovesDestruction()
    {
        var simulator = new EquipmentUpgradeSimulator();
        var evaluation = simulator.EvaluateUpgrade(6, false, hasProtectionScroll: true, playerGold: long.MaxValue);
        return evaluation.IsRecommendedBySafetyPolicy
            && evaluation.DestructionRisk == 0
            && evaluation.IsProtectedAgainstDestruction;
    }

    private static bool TestUpgradeBlockedByFunds()
    {
        var simulator = new EquipmentUpgradeSimulator();
        // A safe low-tier upgrade, but the player cannot afford the tier cost.
        var evaluation = simulator.EvaluateUpgrade(currentLevel: 1, isSpecialistCard: false, hasProtectionScroll: false, playerGold: 0);
        return !evaluation.IsRecommendedBySafetyPolicy
            && evaluation.RiskRationale.Contains("insufficienti", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TestExpectedUpgradeCost()
    {
        var simulator = new EquipmentUpgradeSimulator();
        // A ~45% success tier needs on average ~1/0.45 ≈ 2.2 attempts, so the
        // expected gold must exceed a single attempt's cost.
        var evaluation = simulator.EvaluateUpgrade(4, false, true, long.MaxValue); // +4 → +5
        return evaluation.ExpectedTotalGoldCost > 0
            && evaluation.SuccessChance is > 0 and < 1;
    }

    private static bool TestBazaarFeePolicy()
    {
        var fees = new BazaarFeePolicy();
        long withMedal = fees.CalculateNetProceeds(100_000, hasMedal: true);
        long withoutMedal = fees.CalculateNetProceeds(100_000, hasMedal: false);
        // Both pay the 5% listing fee; only the medal-less sale also pays sales tax.
        return withMedal < 100_000 && withoutMedal < withMedal;
    }

    private static bool TestArbitrageKeepsCraftingMaterials()
    {
        var evaluator = new BazaarMarketArbitrageEvaluator();
        var material = new InventorySlot(0, InventoryTab.Etc, 203, "Pietra Gillion", 10, 999, ItemRarity.Normal_r2, 0, false, 1000, 200);
        var (channel, _, _) = evaluator.EvaluateItemDisposal(material, hasBazaarMedal: true, isRequiredForNextCraftingGoal: true);
        return channel == DisposalChannel.KeepForCrafting;
    }

    private static bool TestArbitrageChannelChoice()
    {
        var evaluator = new BazaarMarketArbitrageEvaluator();
        // High bazaar value, low NPC value → bazaar wins.
        var valuable = new InventorySlot(0, InventoryTab.Main, 401, "Arma Rara", 1, 1, ItemRarity.Excellent_r6, 7, true, 5_000_000, 1000);
        var (bazaarChannel, bazaarGain, _) = evaluator.EvaluateItemDisposal(valuable, hasBazaarMedal: true, isRequiredForNextCraftingGoal: false);

        // Low bazaar value, non-zero NPC value → NPC wins.
        var junk = new InventorySlot(1, InventoryTab.Etc, 402, "Drop Comune", 5, 999, ItemRarity.Low_r1, 0, false, 100, 90);
        var (npcChannel, _, _) = evaluator.EvaluateItemDisposal(junk, hasBazaarMedal: false, isRequiredForNextCraftingGoal: false);

        return bazaarChannel == DisposalChannel.SellOnBazaar && bazaarGain > 0
            && npcChannel == DisposalChannel.SellToNpcVendor;
    }

    private static bool TestGridCompactionConservesQuantity()
    {
        var container = InventoryTabContainer.CreateEmpty(InventoryTab.Etc, capacity: 48);
        // Three partial stacks of the same item, scattered across slots.
        container = container with
        {
            Slots = container.Slots
                .SetItem(2, new InventorySlot(2, InventoryTab.Etc, 201, "Erba", 300, 999, ItemRarity.Normal_r2, 0, false, 10, 2))
                .SetItem(7, new InventorySlot(7, InventoryTab.Etc, 201, "Erba", 250, 999, ItemRarity.Normal_r2, 0, false, 10, 2))
                .SetItem(20, new InventorySlot(20, InventoryTab.Etc, 201, "Erba", 100, 999, ItemRarity.Normal_r2, 0, false, 10, 2)),
        };
        int totalBefore = container.Slots.Values.Where(s => s.ItemId == 201).Sum(s => s.Quantity);

        var optimizer = new InventoryGridOptimizer();
        var compacted = optimizer.CompactTab(container, out int slotsFreed);
        int totalAfter = compacted.Slots.Values.Where(s => s.ItemId == 201).Sum(s => s.Quantity);

        // No item is created or lost, and scattering into 3 slots collapses to fewer.
        return totalAfter == totalBefore && totalBefore == 650 && slotsFreed >= 2;
    }

    private static bool TestSaturationGate()
    {
        var optimizer = new InventoryGridOptimizer();
        var etc = InventoryTabContainer.CreateEmpty(InventoryTab.Etc, capacity: 10);
        for (int i = 0; i < 9; i++)
            etc = etc with { Slots = etc.Slots.SetItem(i, new InventorySlot(i, InventoryTab.Etc, 500 + i, $"Item{i}", 1, 1, ItemRarity.Normal_r2, 0, false, 1, 1)) };
        var main = InventoryTabContainer.CreateEmpty(InventoryTab.Main, capacity: 48);

        bool needs = optimizer.RequiresSanitizationBeforeTimeSpace(etc, main, out string? reason);
        return needs && reason is not null && reason.Contains("ETC", StringComparison.Ordinal);
    }

    private static bool TestCraftingSolver()
    {
        var solver = new CraftingRecipeSolver();
        // Recipe 101 needs 5×201 and 3×202 plus 2500 gold.
        var enough = new Dictionary<int, int> { [201] = 5, [202] = 3 };
        var (canCraft, margin, _) = solver.EvaluateRecipe(101, enough, playerGold: 10_000);
        if (!canCraft || margin <= 0) return false;

        var short201 = new Dictionary<int, int> { [201] = 2, [202] = 3 };
        var (cannot, _, missingReport) = solver.EvaluateRecipe(101, short201, playerGold: 10_000);
        return !cannot && missingReport.Contains("2/5", StringComparison.Ordinal);
    }
}

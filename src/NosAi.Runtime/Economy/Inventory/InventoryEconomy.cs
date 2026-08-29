using System.Collections.Immutable;

namespace NosAi.Economy.Inventory;

public enum InventoryTab : byte { Equipment, Main, Etc, Miniland, Specialist }
public enum ItemRarity : byte { Damaged_r0, Low_r1, Normal_r2, High_r3, Good_r4, Quality_r5, Excellent_r6, Ancient_r7, Phenomenal_r8 }
public sealed record InventorySlot(int SlotIndex,InventoryTab Tab,int ItemId,string Name,int Quantity,int MaxStackSize,ItemRarity Rarity,int UpgradeLevel,bool IsProtectedByScroll,long EstimatedBazaarValueGold,long NpcSellValueGold)
{
    public bool IsFullStack=>Quantity>=MaxStackSize;public bool IsEmpty=>Quantity<=0||ItemId==0;
    public static InventorySlot Empty(int index,InventoryTab tab)=>new(index,tab,0,"Vuoto",0,999,ItemRarity.Normal_r2,0,false,0,0);
}
public sealed record InventoryTabContainer(InventoryTab Tab,ImmutableDictionary<int,InventorySlot> Slots,int Capacity=48)
{
    public int UsedSlotsCount=>Slots.Values.Count(s=>!s.IsEmpty);public int FreeSlotsCount=>Capacity-UsedSlotsCount;public double SaturationPercentage=>(double)UsedSlotsCount/Capacity;
    public static InventoryTabContainer CreateEmpty(InventoryTab tab,int capacity=48){var b=ImmutableDictionary.CreateBuilder<int,InventorySlot>();for(int i=0;i<capacity;i++)b[i]=InventorySlot.Empty(i,tab);return new(tab,b.ToImmutable(),capacity);}
}
public sealed record EconomicStateSnapshot(long CurrentGold,long BankGold,long TotalInventoryEstimatedValueGold,double OverallInventorySaturation,int AngelFeathersCount,int FullMoonCrystalsCount,int GillionStonesCount,int ProtectionScrollsCount);

public sealed record UpgradeProbabilityTier(int TargetUpgradeLevel,float SuccessProbability,float FailureProbability,float BreakOrDestructionProbability,long RequiredGoldCost,int RequiredSoulGems,int RequiredAngelFeathers,int RequiredFullMoonCrystals);
public sealed record UpgradeRiskEvaluation(int CurrentLevel,int TargetLevel,float SuccessChance,float DestructionRisk,long ExpectedTotalGoldCost,int ExpectedTotalFeathersCost,bool IsProtectedAgainstDestruction,bool IsRecommendedBySafetyPolicy,string RiskRationale);
public sealed class EquipmentUpgradeSimulator
{
    private readonly Dictionary<int,UpgradeProbabilityTier> _equipment=new();private readonly Dictionary<int,UpgradeProbabilityTier> _specialist=new();
    public EquipmentUpgradeSimulator(){for(int i=1;i<=10;i++){float success=i switch{1=>1f,2=>.9f,3=>.8f,4=>.6f,5=>.45f,6=>.3f,7=>.2f,8=>.1f,9=>.05f,_=>.02f};float destroy=i<4?0f:i switch{4=>.05f,5=>.1f,6=>.2f,7=>.3f,8=>.4f,_=>.5f};_equipment[i]=new(i,success,1f-success-destroy,destroy,checked(1000L*(1L<<(i-1))),0,i<4?0:Math.Max(1,(i-3)*((i+1)/2)),i<7?0:i-5);} _specialist[1]=new(1,1f,0f,0f,5000,1,2,0);_specialist[5]=new(5,.5f,.5f,0f,50000,2,10,2);_specialist[10]=new(10,.2f,.6f,.2f,300000,5,40,10);_specialist[15]=new(15,.01f,.49f,.5f,2000000,20,200,50);}
    public UpgradeRiskEvaluation EvaluateUpgrade(int currentLevel,bool isSpecialistCard,bool hasProtectionScroll,long playerGold){int target=currentLevel+1;var table=isSpecialistCard?_specialist:_equipment;if(!table.TryGetValue(target,out var tier))return new(currentLevel,target,0,0,0,0,hasProtectionScroll,false,"Livello target oltre il limite supportato.");double attempts=1d/Math.Max(.01,tier.SuccessProbability);long expectedGold=(long)(attempts*tier.RequiredGoldCost);int expectedFeathers=(int)(attempts*tier.RequiredAngelFeathers);bool destruction=tier.BreakOrDestructionProbability>0;bool safe=(!destruction||hasProtectionScroll)&&playerGold>=tier.RequiredGoldCost;string rationale=destruction&&!hasProtectionScroll?$"BLOCCO SICUREZZA FAIL-CLOSED: Rischio distruzione ({tier.BreakOrDestructionProbability:P0}) senza pergamena protettiva.":playerGold<tier.RequiredGoldCost?$"Fondi insufficienti: Richiesti {tier.RequiredGoldCost:N0} Gold, Disponibili {playerGold:N0} Gold.":$"Upgrade approvato: Successo {tier.SuccessProbability:P0}, Tentativi medi stimati: {attempts:F1}.";return new(currentLevel,target,tier.SuccessProbability,hasProtectionScroll?0:tier.BreakOrDestructionProbability,expectedGold,expectedFeathers,hasProtectionScroll||!destruction,safe,rationale);}
}

public sealed class BazaarFeePolicy
{
    public double ListingFeePercentage{get;}=.05;public double SalesTaxWithMedal{get;}=0;public double SalesTaxWithoutMedal{get;}=.05;
    public long CalculateNetProceeds(long gross,bool hasMedal){double rate=ListingFeePercentage+(hasMedal?SalesTaxWithMedal:SalesTaxWithoutMedal);return Math.Max(0,gross-(long)Math.Ceiling(gross*rate));}
}
public enum DisposalChannel : byte { SellOnBazaar, SellToNpcVendor, KeepForCrafting, StoreInWarehouse }
public sealed class BazaarMarketArbitrageEvaluator
{
    private readonly BazaarFeePolicy _fees=new();
    public (DisposalChannel RecommendedChannel,long NetGainGold,string Rationale) EvaluateItemDisposal(InventorySlot slot,bool hasBazaarMedal,bool isRequiredForNextCraftingGoal)
    {if(slot.IsEmpty)return(DisposalChannel.StoreInWarehouse,0,"Slot vuoto.");if(isRequiredForNextCraftingGoal)return(DisposalChannel.KeepForCrafting,0,$"Materiale strategico richiesto per progressione: {slot.Name}");long gross=slot.EstimatedBazaarValueGold*slot.Quantity, bazaar=_fees.CalculateNetProceeds(gross,hasBazaarMedal),npc=slot.NpcSellValueGold*slot.Quantity;if(bazaar>npc+5000&&bazaar>npc*1.30)return(DisposalChannel.SellOnBazaar,bazaar,$"Vendita Bazar vantaggiosa (+{bazaar-npc:N0} Gold rispetto a NPC).");if(npc>0)return(DisposalChannel.SellToNpcVendor,npc,"Vendita diretta NPC raccomandata (Margine Bazar insufficiente).");return(DisposalChannel.StoreInWarehouse,0,"Oggetto non vendibile: stoccaggio in magazzino.");}
}

public sealed class InventoryGridOptimizer
{
    private const double HighSaturationThreshold=.85;
    public InventoryTabContainer CompactTab(InventoryTabContainer container,out int slotsFreed)
    {
        var slots=container.Slots.ToDictionary(k=>k.Key,v=>v.Value);var grouped=slots.Values.Where(s=>!s.IsEmpty&&s.MaxStackSize>1).GroupBy(s=>s.ItemId).ToDictionary(g=>g.Key,g=>g.ToList());
        var compacted=new List<InventorySlot>();var groupedIds=grouped.Keys.ToHashSet();
        foreach(var group in grouped.Values){var sample=group[0];int total=group.Sum(s=>s.Quantity);while(total>0){int qty=Math.Min(total,sample.MaxStackSize);compacted.Add(sample with{Quantity=qty});total-=qty;}}
        foreach(var slot in slots.Values.Where(s=>!s.IsEmpty&&(!groupedIds.Contains(s.ItemId)||s.MaxStackSize<=1)).OrderBy(s=>s.SlotIndex))compacted.Add(slot);
        var output=ImmutableDictionary.CreateBuilder<int,InventorySlot>();for(int i=0;i<container.Capacity;i++)output[i]=i<compacted.Count?compacted[i] with{SlotIndex=i}:InventorySlot.Empty(i,container.Tab);slotsFreed=container.UsedSlotsCount-compacted.Count;return new(container.Tab,output.ToImmutable(),container.Capacity);
    }
    public bool RequiresSanitizationBeforeTimeSpace(InventoryTabContainer etcTab,InventoryTabContainer mainTab,out string? reason){reason=null;if(etcTab.SaturationPercentage>=HighSaturationThreshold){reason=$"SATURAZIONE CRITICA SCHEDA ETC ({etcTab.SaturationPercentage:P0}): Deposito necessario prima del TimeSpace.";return true;}if(mainTab.SaturationPercentage>=HighSaturationThreshold){reason=$"SATURAZIONE CRITICA SCHEDA MAIN ({mainTab.SaturationPercentage:P0}): Spazio pozioni/drop insufficiente.";return true;}return false;}
}

public sealed record RecipeIngredient(int ItemId,string Name,int RequiredQuantity);
public sealed record CraftingRecipe(int RecipeId,string ResultItemName,int ResultItemId,int ProducedQuantity,long RequiredGoldCost,ImmutableArray<RecipeIngredient> Ingredients,long MarketValueResultGold);
public sealed class CraftingRecipeSolver
{
    private readonly Dictionary<int,CraftingRecipe> _recipes=new();
    public CraftingRecipeSolver(){_recipes[101]=new(101,"Pozione di Recupero Grande",1001,10,2500,ImmutableArray.Create(new RecipeIngredient(201,"Erba Curativa di NosVille",5),new RecipeIngredient(202,"Mela Selvatica",3)),12000);_recipes[201]=new(201,"Cristallo di Luna Piena",1002,1,5000,ImmutableArray.Create(new RecipeIngredient(203,"Pietra Gillion",10),new RecipeIngredient(204,"Polvere Purificante",2)),25000);}
    public (bool CanCraft,long NetMarginGold,string MissingReport) EvaluateRecipe(int recipeId,IReadOnlyDictionary<int,int> available,long playerGold){if(!_recipes.TryGetValue(recipeId,out var recipe))return(false,0,"Ricetta non censita nel database.");var missing=new List<string>();if(playerGold<recipe.RequiredGoldCost)missing.Add($"Oro insufficiente (Mancano {recipe.RequiredGoldCost-playerGold:N0} Gold)");foreach(var ing in recipe.Ingredients){int qty=available.GetValueOrDefault(ing.ItemId);if(qty<ing.RequiredQuantity)missing.Add($"{ing.Name}: {qty}/{ing.RequiredQuantity}");}long margin=recipe.MarketValueResultGold*recipe.ProducedQuantity-recipe.RequiredGoldCost;return(missing.Count==0,margin,missing.Count==0?$"Fattibile: Margine netto stimato +{margin:N0} Gold.":$"Materiali mancanti per {recipe.ResultItemName}: [{string.Join(", ",missing)}]");}
}

public sealed class InventoryEconomyOrchestrator
{
    private readonly Dictionary<InventoryTab,InventoryTabContainer> _containers=new();private long _currentGold=150000;private long _bankGold=500000;
    public EquipmentUpgradeSimulator UpgradeSimulator{get;}=new();public BazaarMarketArbitrageEvaluator ArbitrageEvaluator{get;}=new();public InventoryGridOptimizer GridOptimizer{get;}=new();public CraftingRecipeSolver RecipeSolver{get;}=new();
    public InventoryEconomyOrchestrator(){foreach(var tab in Enum.GetValues<InventoryTab>())_containers[tab]=InventoryTabContainer.CreateEmpty(tab);}
    public InventoryTabContainer GetTab(InventoryTab tab)=>_containers[tab];
    public void SetSlot(InventoryTab tab,InventorySlot slot){if(slot.SlotIndex<0||slot.SlotIndex>=_containers[tab].Capacity)throw new ArgumentOutOfRangeException(nameof(slot));_containers[tab]=_containers[tab] with{Slots=_containers[tab].Slots.SetItem(slot.SlotIndex,slot)};}
    public EconomicStateSnapshot CaptureEconomicSnapshot(){long value=_containers.Values.SelectMany(c=>c.Slots.Values).Where(s=>!s.IsEmpty).Sum(s=>s.EstimatedBazaarValueGold*s.Quantity);return new(_currentGold,_bankGold,value,_containers.Values.Average(c=>c.SaturationPercentage),GetTotalItemQuantity(205),GetTotalItemQuantity(206),GetTotalItemQuantity(203),GetTotalItemQuantity(301));}
    public int GetTotalItemQuantity(int itemId)=>_containers.Values.SelectMany(c=>c.Slots.Values).Where(s=>s.ItemId==itemId).Sum(s=>s.Quantity);
}

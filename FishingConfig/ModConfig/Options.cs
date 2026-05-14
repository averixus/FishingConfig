using Vintagestory.GameContent;

namespace FishingConfig;

public class Options
{
    public static Options Instance { get; set; } = new Options();
    
    public float JunkCatchChance = 0.05f;
    public float ReelInTimer = 0.7f;
    public bool CatchStockFish = true; 
    public float FishLureEntityTimer = 15f;
    public int FishHarvestLimit = 12;
    public double FishHarvestRestoreDays = 14d;
    public int FishHarvestScale = 8;
    public int MinPondSize = 100;
    public int MaxPondSize = 1200;
    public float FishStartSearchDelay = 1f;
    public float MinStockCatchTime = 5f;
    public float MaxStockCatchTime = 125f;
    public int ReeledCatchDamage = 1;
    public int RopeSnappedDamage = 2;


    public float WormHarvestRestoreDays = 7f;
    public float MaxWormDensity = 15f;
    public float WormStartSearchDelay = 1f;
    public float WormGruntingTime = 4f;
    public float GetWormChance = 0.06f;
    public float GetExtraWormChance = 0.1f;
    public int WormHarvestScale = 3;
    public float NoWormsChance = 0.5f;
    public float MinWormClimateFertility = 0.2f;
    public float MinWormTemp = 0;
    public float MaxWormTemp = 10;
    public float MinWormBlockFertility = 100;
    public bool AlwaysFertileFarmland = true;
}


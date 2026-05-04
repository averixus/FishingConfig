using Vintagestory.GameContent;

namespace FishingConfig;

public class Options
{
    public float JunkCatchChance = 0.05f;
    public float ReelInTimer = 0.7f;
    public bool CatchStockFish = true; 
    public float LureEntityTimer = 15f;
    public int HarvestLimit = 12;
    public double HarvestRestoreDays = 14d;
    public int HarvestScale = 8;
    public int MinPondSize = 100;
    public int MaxPondSize = 1200;
    public float StartSearchDelay = 1f;
    public float MinStockCatchTime = 5f;
    public float MaxStockCatchTime = 125f;
}


using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace FishingConfig;

public class FishingConfigModSystem : ModSystem
{
    public static Options options;

    public override double ExecuteOrder()
    {
        return 1.1; // Load after ModSystemFishDepletion so we can edit the values
    }
    public override void Start(ICoreAPI api)
    {
        try
        {
            options = api.LoadModConfig<Options>("FishingConfig.json") ?? new Options();
            api.StoreModConfig<Options>(options, "FishingConfig.json");
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Error loading Fishing Config options. Using default settings instead.");
            Mod.Logger.Error(e);
            options = new Options();
        }

        var harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll(typeof(FishingConfigModSystem).Assembly);

        api.ModLoader.GetModSystem<ModSystemFishDepletion>()?.Scale = options.FishHarvestScale;
        ModSystemFishDepletion.MaxHarvestablePerLocation = options.FishHarvestLimit;
        ModSystemFishDepletion.RestoreFishAfterDays = options.FishHarvestRestoreDays;
        Console.WriteLine("[FishingConfig] Finished loading Fishing Config v" + Mod.Info.Version);
    }
}

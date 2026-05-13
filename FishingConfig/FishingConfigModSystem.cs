using System;
using ConfigLib;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace FishingConfig;

public class FishingConfigModSystem : ModSystem
{
    public static Options Options => Options.Instance;

    public override double ExecuteOrder()
    {
        return 1.1; // Load after ModSystemFishDepletion so we can edit the values
    }

    public override void Start(ICoreAPI api)
    {
        try
        {
            Options.Instance = api.LoadModConfig<Options>("FishingConfig.json") ?? new Options();
            api.StoreModConfig<Options>(Options, "FishingConfig.json");
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Error loading Fishing Config options. Using default settings instead.");
            Mod.Logger.Error(e);
            Options.Instance = new Options();
        }

        UpdateFishingModSystem(api);

        var harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll(typeof(FishingConfigModSystem).Assembly);

        if (api.ModLoader.IsModEnabled("configlib"))
        {
            ConnectConfigLib(api);
        }

        Console.WriteLine("[FishingConfig] Finished loading Fishing Config v" + Mod.Info.Version);
    }

    private void ConnectConfigLib(ICoreAPI api)
    {
        ConfigLibModSystem configlib = api.ModLoader.GetModSystem<ConfigLibModSystem>();

        configlib.SettingChanged += (domain, config, setting) =>
        {
            if (domain != Mod.Info.ModID) return;
            setting.AssignSettingValue(Options);
            UpdateFishingModSystem(api);
        };

        configlib.ConfigsLoaded += () =>
        {
            configlib.GetConfig(Mod.Info.ModID)?.AssignSettingsValues(Options);
            UpdateFishingModSystem(api);
        };
    }

    private void UpdateFishingModSystem(ICoreAPI api)
    {
        api.ModLoader.GetModSystem<ModSystemFishDepletion>()?.Scale = Options.FishHarvestScale;
        ModSystemFishDepletion.MaxHarvestablePerLocation = Options.FishHarvestLimit;
        ModSystemFishDepletion.RestoreFishAfterDays = Options.FishHarvestRestoreDays;
    }
}

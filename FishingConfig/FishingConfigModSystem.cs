using HarmonyLib;
using Vintagestory.API.Common;

namespace FishingConfig;

public class FishingConfigModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        var harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll(typeof(FishingConfigModSystem).Assembly);
    }
}

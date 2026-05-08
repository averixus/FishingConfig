using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FishingConfig
{
    [HarmonyPatch(typeof(ModSystemWormGrunting), "restoreEarthWorms")]
    public class RestoreWormsPatch
    {
        static FieldInfo harvestedLocationsField = typeof(ModSystemWormGrunting).GetField("harvestedLocations", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo sapiField = typeof(ModSystemWormGrunting).GetField("sapi", BindingFlags.Instance | BindingFlags.NonPublic);
        public static bool Prefix(ModSystemWormGrunting __instance, float dt)
        {
            TypedReference thisSystem = __makeref(__instance);
            Dictionary<BlockPos, CreatureHarvest> harvestedLocations = (Dictionary<BlockPos, CreatureHarvest>) harvestedLocationsField.GetValueDirect(thisSystem);
            ICoreServerAPI sapi = (ICoreServerAPI) sapiField.GetValueDirect(thisSystem);

            List<BlockPos> list = new List<BlockPos>(harvestedLocations.Keys);
            double totalDays = sapi.World.Calendar.TotalDays;
            foreach (BlockPos item in list)
            {
                if (totalDays - harvestedLocations[item].TotalDays > FishingConfigModSystem.options.WormHarvestRestoreDays)
                {
                    harvestedLocations.Remove(item);
                }
            }

            harvestedLocationsField.SetValueDirect(thisSystem, harvestedLocations);
            return false;
        }
    }

    [HarmonyPatch(typeof(ModSystemWormGrunting), "GetInitialDensity")]
    public class InitialDensityPatch
    {
        static FieldInfo noiseGenField = typeof(ModSystemWormGrunting).GetField("noiseGen", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo sapiField = typeof(ModSystemWormGrunting).GetField("sapi", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Prefix(ModSystemWormGrunting __instance, BlockPos pos, ref float __result)
        {
            TypedReference thisSystem = __makeref(__instance);
            NormalizedSimplexNoise noiseGen = (NormalizedSimplexNoise) noiseGenField.GetValueDirect(thisSystem);
            ICoreServerAPI sapi = (ICoreServerAPI) sapiField.GetValueDirect(thisSystem);
            Options options = FishingConfigModSystem.options;

            double noise = noiseGen.Noise(pos.X, pos.Z);
            float maxed = Math.Max(0f, (float)(noise - options.NoWormsChance) * (1.5f / (1f - options.NoWormsChance))); // Scale the result to between 0 and 1.5, with NoWormsChance of being 0
            ClimateCondition climateAt = sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            __result = maxed * Math.Max(0f, (climateAt.Fertility - options.MinWormClimateFertility) * (1.04f / options.MinWormClimateFertility)); // Scale the result to between 0 and 1.56, always 0 if fertility is below MinWormFertility
            return false;
        }
    }
        
    [HarmonyPatch(typeof(ModSystemWormGrunting), "GetEarthWormAmount")]
    public class WormDensityPatch
    {
        static FieldInfo harvestedLocationsField = typeof(ModSystemWormGrunting).GetField("harvestedLocations", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo sapiField = typeof(ModSystemWormGrunting).GetField("sapi", BindingFlags.Instance | BindingFlags.NonPublic);
        public static bool Prefix(ModSystemWormGrunting __instance, BlockPos pos, ref float __result)
        {
            TypedReference thisSystem = __makeref(__instance);
            Dictionary<BlockPos, CreatureHarvest> harvestedLocations = (Dictionary<BlockPos, CreatureHarvest>) harvestedLocationsField.GetValueDirect(thisSystem);
            ICoreServerAPI sapi = (ICoreServerAPI) sapiField.GetValueDirect(thisSystem);
            Options options = FishingConfigModSystem.options;

            __result = __instance.GetInitialDensity(pos) * (options.MaxWormDensity / 1.56f); // Gives a result between 0 and MaxWormDensity
            float temperature = sapi.World.BlockAccessor.GetClimateAt(pos).Temperature;
            __result *= GameMath.Clamp((temperature - options.MinWormTemp) / (options.MaxWormTemp - options.MinWormTemp), 0f, 1f); // Below MinWormTemp is always zero, above MaxWormTemp is always maximum, scaled in between
            if (harvestedLocations.TryGetValue(pos / options.WormHarvestScale, out var value))
            {
                __result -= value.Quantity; // Subtract the amount already collected nearby
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ModSystemWormGrunting), "AddHarvest")]
    public class AddHarvestPatch
    {
        static FieldInfo harvestedLocationsField = typeof(ModSystemWormGrunting).GetField("harvestedLocations", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo sapiField = typeof(ModSystemWormGrunting).GetField("sapi", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Prefix(ModSystemWormGrunting __instance, BlockPos pos, int quantity)
        {
            TypedReference thisSystem = __makeref(__instance);
            Dictionary<BlockPos, CreatureHarvest> harvestedLocations = (Dictionary<BlockPos, CreatureHarvest>) harvestedLocationsField.GetValueDirect(thisSystem);
            ICoreServerAPI sapi = (ICoreServerAPI) sapiField.GetValueDirect(thisSystem);
            Options options = FishingConfigModSystem.options;

            harvestedLocations.TryGetValue(pos / options.WormHarvestScale, out var value);
            harvestedLocations[pos / options.WormHarvestScale] = new CreatureHarvest
            {
                TotalDays = sapi.World.Calendar.TotalDays,
                Quantity = value.Quantity + quantity
            };

            return false;
        }
    }
        
    [HarmonyPatch(typeof(ItemWormGrunter), "OnHeldInteractStart")]
    public class InteractStartPatch
    {
        static FieldInfo apiField = typeof(CollectibleObject).GetField("api", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo getCoolingMedium = typeof(CollectibleObject).GetMethod("getCoolingMedium", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo tryEatBegin = typeof(CollectibleObject).GetMethod("tryEatBegin", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo startSound = typeof(ItemWormGrunter).GetMethod("startSound", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Prefix(ItemWormGrunter __instance, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            TypedReference thisItem = __makeref(__instance);
            ICoreAPI api = (ICoreAPI) apiField.GetValueDirect(thisItem);
            Options options = FishingConfigModSystem.options;

            if (!firstEvent || blockSel == null || blockSel.Face != BlockFacing.UP || !byEntity.Controls.ShiftKey)
            {
                return false;
            }

            if (byEntity.Controls.CtrlKey)
            {
                // base.OnHeldInteractStart
                EnumHandHandling handHandling = EnumHandHandling.NotHandled;
                bool flag = false;
                CollectibleBehavior[] collectibleBehaviors = __instance.CollectibleBehaviors;
                foreach (CollectibleBehavior obj in collectibleBehaviors)
                {
                    EnumHandling handling2 = EnumHandling.PassThrough;
                    obj.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling2);
                    if (handling2 != EnumHandling.PassThrough)
                    {
                        handling = handHandling;
                        flag = true;
                    }

                    if (handling2 == EnumHandling.PreventSubsequent)
                    {
                        return false;
                    }
                }

                if (!flag)
                {
                    if (blockSel != null && getCoolingMedium.Invoke(__instance, [blockSel]) != null && __instance.GetTemperature(api.World, slot.Itemstack) > (float)GlobalConstants.TooHotToTouchTemperature)
                    {
                        handling = EnumHandHandling.Handled;
                        return false;
                    }

                    object[] parameters = [slot, byEntity, handHandling];
                    tryEatBegin.Invoke(__instance, parameters);
                    handling = (EnumHandHandling) parameters[2];
                }
                // end base.OnHeldInteractStart

                return false;
            }

            Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);
            if (block.Fertility < options.MinWormBlockFertility && !(options.AlwaysFertileFarmland && block is BlockFarmland))
            {
                return false;
            }

            if (byEntity.LeftHandItemSlot.Empty || byEntity.LeftHandItemSlot.Itemstack.Collectible.Code.Path != "stick")
            {
                (api as ICoreClientAPI)?.TriggerIngameError(__instance, "missingstick", Lang.Get("Requires a stick in offhand"));
                return false;
            }

            if (byEntity.World.BlockAccessor.GetClimateAt(blockSel.Position).Temperature <= options.MinWormTemp)
            {
                (api as ICoreClientAPI)?.TriggerIngameError(__instance, "toocold", Lang.Get("The ground is frozen, worms won't come out"));
                return false;
            }

            handling = EnumHandHandling.PreventDefault;
            startSound.Invoke(__instance, [byEntity]);
            if (api.Side == EnumAppSide.Server)
            {
                float earthWormAmount = api.ModLoader.GetModSystem<ModSystemWormGrunting>().GetEarthWormAmount(blockSel.Position);
                int spawnNow = GameMath.RoundRandom(api.World.Rand, (float)Math.Min(0.5, api.World.Rand.NextDouble()) * earthWormAmount); // Rounds a value between 0 and 0.5 (so usually 0, sometimes 1). always 0 if worm population 0
                slot.Itemstack.TempAttributes.SetInt("spawnAmount", spawnNow);
            }
        
            return false;
        }
    }
    
    
    [HarmonyPatch(typeof(ItemWormGrunter), "OnHeldInteractStep")]
    public class InteractStepPatch
    {
        static FieldInfo apiField = typeof(CollectibleObject).GetField("api", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo spawnWorm = typeof(ItemWormGrunter).GetMethod("spawnWorm", BindingFlags.Instance | BindingFlags.NonPublic);
        public static bool Prefix (ItemWormGrunter __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref bool __result)
        {
            TypedReference thisItem = __makeref(__instance);
            ICoreAPI api = (ICoreAPI) apiField.GetValueDirect(thisItem);
            Options options = FishingConfigModSystem.options;

            if (blockSel == null || blockSel.Face != BlockFacing.UP)
            {
                __result = false;
                return false;
            }

            float timeLimit = (api.Side == EnumAppSide.Server) ? options.WormGruntingTime : options.WormGruntingTime + 1;
            if (secondsUsed > timeLimit)
            {
                __result = false;
                return false;
            }

            if (api.Side == EnumAppSide.Server)
            {
                Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);
                int wormsAvailable = slot.Itemstack.TempAttributes.GetInt("spawnAmount");

                if (block.Fertility >= options.MinWormBlockFertility && secondsUsed >= options.WormStartSearchDelay && wormsAvailable > 0 && api.World.Rand.NextDouble() < options.GetWormChance)
                {
                    int spawnNow = (wormsAvailable > 1 && api.World.Rand.NextDouble() < options.GetExtraWormChance) ? 2 : 1;
                    spawnWorm.Invoke(__instance, [slot, byEntity, blockSel, spawnNow]);
                    slot.Itemstack.TempAttributes.SetInt("spawnAmount", wormsAvailable - spawnNow);
                }
            }

            __result = true;
            return false;
        }
    }
        
    [HarmonyPatch(typeof(ItemWormGrunter), "OnHeldInteractStop")]
    public class InteractStopPatch
    {
        static FieldInfo apiField = typeof(CollectibleObject).GetField("api", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo spawnWorm = typeof(ItemWormGrunter).GetMethod("spawnWorm", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo stopSound = typeof(ItemWormGrunter).GetMethod("stopSound", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo tryEatStop = typeof(CollectibleObject).GetMethod("tryEatStop", BindingFlags.Instance | BindingFlags.NonPublic);
        public static bool Prefix (ItemWormGrunter __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            TypedReference thisItem = __makeref(__instance);
            ICoreAPI api = (ICoreAPI) apiField.GetValueDirect(thisItem);
            Options options = FishingConfigModSystem.options;
            
            stopSound.Invoke(__instance, []);
            byEntity.AnimManager.StopAnimation("wormgrunting");
            if (blockSel == null || blockSel.Face != BlockFacing.UP)
            {
                return false;
            }

            Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);

            if ((block.Fertility >= options.MinWormBlockFertility || (options.AlwaysFertileFarmland && block is BlockFarmland)) && secondsUsed > options.WormGruntingTime && api.Side == EnumAppSide.Server)
            {
                int wormsAvailable = slot.Itemstack.TempAttributes.GetInt("spawnAmount");

                if (wormsAvailable > 0)
                {
                    spawnWorm.Invoke(__instance, [slot, byEntity, blockSel, wormsAvailable]);
                }
            }

            if (secondsUsed >= (options.WormGruntingTime / 4f) && (byEntity as EntityPlayer).Player.WorldData.CurrentGameMode != EnumGameMode.Creative && byEntity.World.Side == EnumAppSide.Server)
            {
                slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, slot);
                slot.MarkDirty();
            }

            // From base.OnHeldInteractStep
            bool flag = false;
            CollectibleBehavior[] collectibleBehaviors = __instance.CollectibleBehaviors;
            foreach (CollectibleBehavior obj in collectibleBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                obj.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel, ref handling);
                if (handling != EnumHandling.PassThrough)
                {
                    flag = true;
                }

                if (handling == EnumHandling.PreventSubsequent)
                {
                    return false;
                }
            }

            if (!flag)
            {
                tryEatStop.Invoke(__instance, [secondsUsed, slot, byEntity]);
            }
            // End of base.OnHeldInteractStep

            return false;
        }
    }
}
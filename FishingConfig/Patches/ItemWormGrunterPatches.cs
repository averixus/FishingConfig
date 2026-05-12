using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "harvestedLocations")]
        extern static ref Dictionary<BlockPos, CreatureHarvest> getHarvestedLocations(ModSystemWormGrunting @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "sapi")]
        extern static ref ICoreServerAPI getSapi(ModSystemWormGrunting @this);

        public static bool Prefix(ModSystemWormGrunting __instance, float dt)
        {
            Dictionary<BlockPos, CreatureHarvest> harvestedLocations = getHarvestedLocations(__instance);
            ICoreServerAPI sapi = getSapi(__instance);

            List<BlockPos> list = new List<BlockPos>(harvestedLocations.Keys);
            double totalDays = sapi.World.Calendar.TotalDays;
            foreach (BlockPos item in list)
            {
                if (totalDays - harvestedLocations[item].TotalDays > FishingConfigModSystem.options.WormHarvestRestoreDays)
                {
                    harvestedLocations.Remove(item);
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ModSystemWormGrunting), "GetInitialDensity")]
    public class InitialDensityPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "noiseGen")]
        extern static ref NormalizedSimplexNoise getNoiseGen(ModSystemWormGrunting @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "sapi")]
        extern static ref ICoreServerAPI getSapi(ModSystemWormGrunting @this);

        public static bool Prefix(ModSystemWormGrunting __instance, BlockPos pos, ref float __result)
        {
            NormalizedSimplexNoise noiseGen = getNoiseGen(__instance);
            ICoreServerAPI sapi = getSapi(__instance);
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "harvestedLocations")]
        extern static ref Dictionary<BlockPos, CreatureHarvest> getHarvestedLocations(ModSystemWormGrunting @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "sapi")]
        extern static ref ICoreServerAPI getSapi(ModSystemWormGrunting @this);

        public static bool Prefix(ModSystemWormGrunting __instance, BlockPos pos, ref float __result)
        {
            Dictionary<BlockPos, CreatureHarvest> harvestedLocations = getHarvestedLocations(__instance);
            ICoreServerAPI sapi = getSapi(__instance);
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "harvestedLocations")]
        extern static ref Dictionary<BlockPos, CreatureHarvest> getHarvestedLocations(ModSystemWormGrunting @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "sapi")]
        extern static ref ICoreServerAPI getSapi(ModSystemWormGrunting @this);
        
        public static bool Prefix(ModSystemWormGrunting __instance, BlockPos pos, int quantity)
        {
            Dictionary<BlockPos, CreatureHarvest> harvestedLocations = getHarvestedLocations(__instance);
            ICoreServerAPI sapi = getSapi(__instance);
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "api")]
        extern static ref ICoreAPI getApi(CollectibleObject @this);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getCoolingMedium")]
        extern static ICoolingMedium getCoolingMedium(CollectibleObject @this, BlockSelection blockSel);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "tryEatBegin")]
        extern static void tryEatBegin(CollectibleObject @this, ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling, string eatSound = "eat", int eatSoundRepeats = 1);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "startSound")]
        extern static void startSound(ItemWormGrunter @this, EntityAgent byEntity);

        public static bool Prefix(ItemWormGrunter __instance, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            ICoreAPI api = getApi(__instance);
            Options options = FishingConfigModSystem.options;

            if (!firstEvent || blockSel == null || blockSel.Face != BlockFacing.UP || !byEntity.Controls.ShiftKey)
            {
                return false;
            }

            if (byEntity.Controls.CtrlKey)
            {
                // taking the relevant parts from base.OnHeldInteractStart
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
                    handling = handHandling;
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
            startSound(__instance, byEntity);
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "api")]
        extern static ref ICoreAPI getApi(CollectibleObject @this);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "spawnWorm")]
        extern static void spawnWorm(ItemWormGrunter @this, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, int amount);
        
        public static bool Prefix (ItemWormGrunter __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref bool __result)
        {
            ICoreAPI api = getApi(__instance);
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
                    spawnWorm(__instance, slot, byEntity, blockSel, spawnNow);
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "api")]
        extern static ref ICoreAPI getApi(CollectibleObject @this);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "spawnWorm")]
        extern static void spawnWorm(ItemWormGrunter @this, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, int amount);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "stopSound")]
        extern static void stopSound(ItemWormGrunter @this);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "tryEatStop")]
        extern static void tryEatStop(CollectibleObject @this, float secondsUsed, ItemSlot slot, EntityAgent byEntity);

        public static bool Prefix (ItemWormGrunter __instance, float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            ICoreAPI api = getApi(__instance);
            Options options = FishingConfigModSystem.options;
            
            stopSound(__instance);
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
                    spawnWorm(__instance, slot, byEntity, blockSel, wormsAvailable);
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
                tryEatStop(__instance, secondsUsed, slot, byEntity);
            }
            // End of base.OnHeldInteractStep

            return false;
        }
    }
}
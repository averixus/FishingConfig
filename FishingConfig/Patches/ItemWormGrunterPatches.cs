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
        public static bool Prefix(ModSystemWormGrunting __instance, ref Dictionary<BlockPos, CreatureHarvest> ___harvestedLocations, ref ICoreServerAPI ___sapi, float dt)
        {
            List<BlockPos> list = new List<BlockPos>(___harvestedLocations.Keys);
            double totalDays = ___sapi.World.Calendar.TotalDays;
            foreach (BlockPos item in list)
            {
                if (totalDays - ___harvestedLocations[item].TotalDays > Options.Instance.WormHarvestRestoreDays)
                {
                    ___harvestedLocations.Remove(item);
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ModSystemWormGrunting), "GetInitialDensity")]
    public class InitialDensityPatch
    {
        public static bool Prefix(ModSystemWormGrunting __instance, BlockPos pos, ref NormalizedSimplexNoise ___noiseGen, ref ICoreServerAPI ___sapi, ref float __result)
        {
            double noise = ___noiseGen.Noise(pos.X, pos.Z);
            float maxed = Math.Max(0f, (float)(noise - Options.Instance.NoWormsChance) * (1.5f / (1f - Options.Instance.NoWormsChance))); // Scale the result to between 0 and 1.5, with NoWormsChance of being 0
            ClimateCondition climateAt = ___sapi.World.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.WorldGenValues);
            __result = maxed * Math.Max(0f, (climateAt.Fertility - Options.Instance.MinWormClimateFertility) * (1.04f / Options.Instance.MinWormClimateFertility)); // Scale the result to between 0 and 1.56, always 0 if fertility is below MinWormFertility
            return false;
        }
    }
        
    [HarmonyPatch(typeof(ModSystemWormGrunting), "GetEarthWormAmount")]
    public class WormDensityPatch
    {
        public static bool Prefix(ModSystemWormGrunting __instance, ref Dictionary<BlockPos, CreatureHarvest> ___harvestedLocations, ref ICoreServerAPI ___sapi, BlockPos pos, ref float __result)
        {
            __result = __instance.GetInitialDensity(pos) * (Options.Instance.MaxWormDensity / 1.56f); // Gives a result between 0 and MaxWormDensity
            float temperature = ___sapi.World.BlockAccessor.GetClimateAt(pos).Temperature;
            __result *= GameMath.Clamp((temperature - Options.Instance.MinWormTemp) / (Options.Instance.MaxWormTemp - Options.Instance.MinWormTemp), 0f, 1f); // Below MinWormTemp is always zero, above MaxWormTemp is always maximum, scaled in between
            if (___harvestedLocations.TryGetValue(pos / Options.Instance.WormHarvestScale, out var value))
            {
                __result -= value.Quantity; // Subtract the amount already collected nearby
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ModSystemWormGrunting), "AddHarvest")]
    public class AddHarvestPatch
    {
        public static bool Prefix(ModSystemWormGrunting __instance, ref Dictionary<BlockPos, CreatureHarvest> ___harvestedLocations, ref ICoreServerAPI ___sapi, BlockPos pos, int quantity)
        {
            ___harvestedLocations.TryGetValue(pos / Options.Instance.WormHarvestScale, out var value);
            ___harvestedLocations[pos / Options.Instance.WormHarvestScale] = new CreatureHarvest
            {
                TotalDays = ___sapi.World.Calendar.TotalDays,
                Quantity = value.Quantity + quantity
            };

            return false;
        }
    }
        
    [HarmonyPatch(typeof(ItemWormGrunter), "OnHeldInteractStart")]
    public class InteractStartPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getCoolingMedium")]
        extern static ICoolingMedium getCoolingMedium(CollectibleObject @this, BlockSelection blockSel);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "tryEatBegin")]
        extern static void tryEatBegin(CollectibleObject @this, ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling, string eatSound = "eat", int eatSoundRepeats = 1);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "startSound")]
        extern static void startSound(ItemWormGrunter @this, EntityAgent byEntity);

        public static bool Prefix(ItemWormGrunter __instance, ref ICoreAPI ___api, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
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

            Block block = ___api.World.BlockAccessor.GetBlock(blockSel.Position);
            if (block.Fertility < Options.Instance.MinWormBlockFertility && !(Options.Instance.AlwaysFertileFarmland && block is BlockFarmland))
            {
                return false;
            }

            if (byEntity.LeftHandItemSlot.Empty || byEntity.LeftHandItemSlot.Itemstack.Collectible.Code.Path != "stick")
            {
                (___api as ICoreClientAPI)?.TriggerIngameError(__instance, "missingstick", Lang.Get("Requires a stick in offhand"));
                return false;
            }

            if (byEntity.World.BlockAccessor.GetClimateAt(blockSel.Position).Temperature <= Options.Instance.MinWormTemp)
            {
                (___api as ICoreClientAPI)?.TriggerIngameError(__instance, "toocold", Lang.Get("The ground is frozen, worms won't come out"));
                return false;
            }

            handling = EnumHandHandling.PreventDefault;
            startSound(__instance, byEntity);
            if (___api.Side == EnumAppSide.Server)
            {
                float earthWormAmount = ___api.ModLoader.GetModSystem<ModSystemWormGrunting>().GetEarthWormAmount(blockSel.Position);
                int spawnNow = GameMath.RoundRandom(___api.World.Rand, (float)Math.Min(0.5, ___api.World.Rand.NextDouble()) * earthWormAmount); // Rounds a value between 0 and 0.5 (so usually 0, sometimes 1). always 0 if worm population 0
                slot.Itemstack.TempAttributes.SetInt("spawnAmount", spawnNow);
            }
        
            return false;
        }
    }
    
    [HarmonyPatch(typeof(ItemWormGrunter), "OnHeldInteractStep")]
    public class InteractStepPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "spawnWorm")]
        extern static void spawnWorm(ItemWormGrunter @this, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, int amount);
        
        public static bool Prefix (ItemWormGrunter __instance, ref ICoreAPI ___api, float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref bool __result)
        {
            if (blockSel == null || blockSel.Face != BlockFacing.UP)
            {
                __result = false;
                return false;
            }

            float timeLimit = (___api.Side == EnumAppSide.Server) ? Options.Instance.WormGruntingTime + Options.Instance.WormStartSearchDelay : Options.Instance.WormGruntingTime + Options.Instance.WormStartSearchDelay + 1;
            if (secondsUsed > timeLimit)
            {
                __result = false;
                return false;
            }

            if (___api.Side == EnumAppSide.Server)
            {
                Block block = ___api.World.BlockAccessor.GetBlock(blockSel.Position);
                int wormsAvailable = slot.Itemstack.TempAttributes.GetInt("spawnAmount");

                if (block.Fertility >= Options.Instance.MinWormBlockFertility && secondsUsed >= Options.Instance.WormStartSearchDelay && wormsAvailable > 0 && ___api.World.Rand.NextDouble() < Options.Instance.GetWormChance)
                {
                    int spawnNow = (wormsAvailable > 1 && ___api.World.Rand.NextDouble() < Options.Instance.GetExtraWormChance) ? 2 : 1;
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
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "spawnWorm")]
        extern static void spawnWorm(ItemWormGrunter @this, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, int amount);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "stopSound")]
        extern static void stopSound(ItemWormGrunter @this);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "tryEatStop")]
        extern static void tryEatStop(CollectibleObject @this, float secondsUsed, ItemSlot slot, EntityAgent byEntity);

        public static bool Prefix (ItemWormGrunter __instance, ref ICoreAPI ___api, float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            stopSound(__instance);
            byEntity.AnimManager.StopAnimation("wormgrunting");
            if (blockSel == null || blockSel.Face != BlockFacing.UP)
            {
                return false;
            }

            Block block = ___api.World.BlockAccessor.GetBlock(blockSel.Position);

            if ((block.Fertility >= Options.Instance.MinWormBlockFertility || (Options.Instance.AlwaysFertileFarmland && block is BlockFarmland)) && secondsUsed > Options.Instance.WormGruntingTime && ___api.Side == EnumAppSide.Server)
            {
                int wormsAvailable = slot.Itemstack.TempAttributes.GetInt("spawnAmount");

                if (wormsAvailable > 0)
                {
                    spawnWorm(__instance, slot, byEntity, blockSel, wormsAvailable);
                }
            }

            if (secondsUsed >= (Options.Instance.WormGruntingTime / 4f) && (byEntity as EntityPlayer).Player.WorldData.CurrentGameMode != EnumGameMode.Creative && byEntity.World.Side == EnumAppSide.Server)
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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace FishingConfig
{
    [HarmonyPatch(typeof(EntityBobber), "onServertick")]
    public class ServerTickPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "playCatchEffects")]
        extern static void playCatchEffects(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "printLocationDebugInfo")]
        extern static void printLocationDebugInfo(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getRandomFishEntityProperties")]
        extern static EntityProperties getRandomFishEntityProperties(EntityBobber @this, ItemStack baitStack, out float abundanceValue, bool printDebug = false);
        
        public static bool Prefix(EntityBobber __instance, ref bool ___wasSwimming, ref float ___swimmingAccum,
                ref float ___catchAccum, ref EnumBobberState ___bobberState, ref EntityPartitioning ___ep, float dt)
        {
            if (__instance.Swimming && !___wasSwimming)
            {
                if (__instance.Api.World.EntityDebugMode) printLocationDebugInfo(__instance);
                ___wasSwimming = true;
            }

            switch(___bobberState)
            {
                case EnumBobberState.Baiting:
                {
                    if (___swimmingAccum > Options.Instance.FishStartSearchDelay) // wait after casting, then check for entities or stock
                    {
                        Entity nearestEntity = ___ep.GetNearestEntity(__instance.Pos.XYZ, 20.0, (Entity e) => e is EntityFish, EnumEntitySearchType.Creatures);
                        ___bobberState = (nearestEntity != null) ? EnumBobberState.FishNearby : Options.Instance.CatchStockFish ? EnumBobberState.NoFishNearby : EnumBobberState.Baiting;
                    }
                    break;
                }
                case EnumBobberState.FishNearby:
                {
                    if (___swimmingAccum > Options.Instance.FishLureEntityTimer) // if entity doesn't arrive after a while, assume it's gone
                    {
                        if (Options.Instance.CatchStockFish) // switch to stock fish
                        {
                            ___bobberState = EnumBobberState.NoFishNearby;

                        }
                        else // or reset to try again if not catching stock fish
                        {
                            ___bobberState = EnumBobberState.Baiting;
                            ___swimmingAccum = 1f;
                        }
                    }
                    else // or catch if the entity comes close enough and attracted to bait
                    {
                        Entity nearestEntity = ___ep.GetNearestEntity(__instance.Pos.XYZ, 1.0, (Entity e) => e is EntityFish, EnumEntitySearchType.Creatures);
                        string bait = __instance.BaitStack?.Collectible.Attributes?["baitTag"].AsString() ?? "nobait";
                        if (nearestEntity != null && nearestEntity.Properties.Attributes["baitTags"].AsArray<string>().Contains<string>(bait))
                        {
                            ___bobberState = EnumBobberState.NoCatch; // using this as an alias for EntityFishCatch which should be a separate state
                            __instance.caughtFish = nearestEntity as EntityFish;
                            ___catchAccum += dt;
                            playCatchEffects(__instance);// .Invoke(__instance, []);
                        }
                    }
                    break;    
                }
                case EnumBobberState.NoFishNearby:
                {
                    getRandomFishEntityProperties(__instance, __instance.BaitStack, out float catchLikelihood, false);
                    if (catchLikelihood > 0 && ___swimmingAccum > Options.Instance.MinStockCatchTime / Math.Max(Options.Instance.MinStockCatchTime / Options.Instance.MaxStockCatchTime, catchLikelihood)) // wait according to abundance, then catch from stock
                    {
                        ___bobberState = __instance.Api.World.Rand.NextDouble() < (double) Options.Instance.JunkCatchChance ?
                                EnumBobberState.JunkCatch : EnumBobberState.NoEntityFishCatch; // catch junk or stock fish according to chance
                        ___catchAccum += dt;
                        playCatchEffects(__instance);
                    }   
                    break;
                }
                case EnumBobberState.NoEntityFishCatch:
                case EnumBobberState.JunkCatch:
                case EnumBobberState.NoCatch: // using this as an alias for EntityFishCatch which should be a separate state
                {
                    if (___catchAccum > Options.Instance.ReelInTimer) // wait for player to reel in catch, then reset
                    {
                        if (__instance.caughtFish != null) // if there's a fish entity, let it go
                        {
                            AiTaskManager taskManager = __instance.caughtFish.GetBehavior<EntityBehaviorTaskAI>().TaskManager;
                            IAiTask aiTask = taskManager?.GetTask("fleebobber");
                            if (aiTask != null)
                            {
                                taskManager.ExecuteTask(aiTask, aiTask.Slot);
                            }
                            __instance.caughtFish = null;
                        }
                        
                        __instance.BaitStack = null; 
                        __instance.WatchedAttributes.MarkPathDirty("baitStack");
                        ___catchAccum = 0f; // reset timers so it doesn't catch again instantly
                        ___swimmingAccum = 0f;
                        ___bobberState = EnumBobberState.Baiting;
                    }
                    else
                    {
                        ___catchAccum += dt;
                    }
                    break;
                }
            }

            return false;
        }
    }
    
    [HarmonyPatch(typeof(EntityBobber), "TryCatchFish")]
    public class TryCatchPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getRandomFishEntityProperties")]
        extern static EntityProperties getRandomFishEntityProperties(EntityBobber @this, ItemStack baitStack, out float abundanceValue, bool printDebug = false);
        
        public static bool Prefix(EntityBobber __instance, ref EnumBobberState ___bobberState, EntityAgent entityCatcher)
        {
            ItemStack[] drops = [];

            switch(___bobberState)
            {
                case EnumBobberState.NoCatch: // alias for EntityFishCatch
                {
                    if (__instance.caughtFish != null && __instance.caughtFish.Alive)
                    {
                        __instance.caughtFish.Die(EnumDespawnReason.Expire);
                        drops = __instance.caughtFish.GetDrops(__instance.World, __instance.caughtFish.Pos.XYZInt.AsBlockPos, (entityCatcher as EntityPlayer)?.Player);
                    }
                    break;
                }
                case EnumBobberState.NoEntityFishCatch:
                {
                    EntityProperties fishCatch = getRandomFishEntityProperties(__instance, __instance.BaitStack, out float abundanceValue, false);

                    ItemStack fishStack = fishCatch.Drops[0].ResolvedItemstack;
                    string age = (__instance.Api.World.Rand.NextDouble() < (double) abundanceValue) ? "adult" : "juvenile"; // reversed check so that lower abundance = fewer adults
                    CollectibleObject agedFish = __instance.Api.World.GetItem(fishStack.Collectible.CodeWithVariant("age", age));
                    fishStack = agedFish != null ? new ItemStack(agedFish) : fishStack.Clone();

                    drops = [fishStack];

                    __instance.Api.ModLoader.GetModSystem<ModSystemFishDepletion>().AddHarvest(__instance.Pos.XYZ.AsBlockPos, 1);

                    break;
                }
                case EnumBobberState.JunkCatch:
                {
                    WeightedBlockDropItemstack[] junkCatches = __instance.Properties.Attributes["junkCatches"].AsObject<WeightedBlockDropItemstack[]>();
                    double total = 0d;
                    foreach (WeightedBlockDropItemstack junkCatch in junkCatches)
                    {
                        total += junkCatch.Weight;
                    }
                    double selection = __instance.Api.World.Rand.NextDouble() * total;
                    junkCatches.Shuffle(__instance.Api.World.Rand);
                    foreach (WeightedBlockDropItemstack junkCatch in junkCatches)
                    {
                        selection -= junkCatch.Weight;
                        if (selection < 0d)
                        {
                            junkCatch.Resolve(__instance.Api.World, "bobber junk catch", __instance.Code);
                            drops = [junkCatch.ResolvedItemstack.Clone()];
                            break;
                        }
                    }
                    break;
                }
            }

            if (drops.Length > 0)
            {
                __instance.BaitStack = null;
                __instance.WatchedAttributes.MarkPathDirty("baitStack");

                foreach (ItemStack drop in drops)
                {
                    if (!entityCatcher.TryGiveItemStack(drop))
                    {
                        __instance.World.SpawnItemEntity(drop, entityCatcher.Pos.XYZ);
                    }
                }

                ItemSlot slot = entityCatcher.ActiveHandItemSlot;
                slot.Itemstack.Collectible.DamageItem(__instance.World, entityCatcher, slot);
                slot.MarkDirty();
            }

            return false;
        }
    }
       
    [HarmonyPatch(typeof(EntityBobber), "getRandomFishEntityProperties")]
    public class RandomFishPatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getPondSize")]
        extern static int calculatePondSize(EntityBobber @this);

        public static bool Prefix(EntityBobber __instance, ref int ___pondSize, ItemStack baitStack, out float abundanceValue, bool printDebug, ref EntityProperties __result)
        {
            abundanceValue = 0f;
            ClimateCondition climate = __instance.World.BlockAccessor.GetClimateAt(__instance.Pos.AsBlockPos, EnumGetClimateMode.WorldGenValues);

            if (climate == null) // no abundance and no catch chance if invalid climate
            {
                __result = null;
                return false;
            }

            ___pondSize = ___pondSize < 0 ? (int) calculatePondSize(__instance) : ___pondSize; // calculate if not yet done
            if (___pondSize < Options.Instance.MinPondSize) // no abundance and no catch chance if pond too small
            {
                __result = null;
                return false;
            }

            // Get animal spawn maps for this region
            Vec3d xYZ = __instance.Pos.XYZ;
            int regionSize = __instance.World.BlockAccessor.RegionSize;
            int animalMapsPerRegion = regionSize / TerraGenConfig.animalMapScale;
            int xInRegion = xYZ.XInt % regionSize;
            int zInRegion = xYZ.ZInt % regionSize;
            float xInAnimalMap = GameMath.Clamp((float)xInRegion / (float)regionSize * (float)animalMapsPerRegion, 0f, animalMapsPerRegion - 1);
            float zInAnimalMap = GameMath.Clamp((float)zInRegion / (float)regionSize * (float)animalMapsPerRegion, 0f, animalMapsPerRegion - 1);
            IMapRegion mapRegion = __instance.World.BlockAccessor.GetMapRegion(xYZ.XInt / regionSize, xYZ.ZInt / regionSize);

            List<EntityProperties> spawnable = [];
            Block block = __instance.World.BlockAccessor.GetBlock(__instance.Pos.XYZ.AsBlockPos, 2);
            string bait = __instance.BaitStack?.Collectible.Attributes?["baitTag"].AsString() ?? "nobait";

            // Make a list of entities that can spawn in this climate, at this block, have enough local spawn density, and like current bait
            foreach (EntityProperties entityType in __instance.World.EntityTypes)
            {
                BaseSpawnConditions spawnConditions = entityType.Server.SpawnConditions?.Runtime ?? entityType.Server.SpawnConditions?.Worldgen as BaseSpawnConditions;
                ClimateSpawnCondition spawnClimate = entityType.Server.SpawnConditions?.Climate ?? spawnConditions;
                string mapCode = entityType.Server.SpawnConditions?.Climate?.MapCode ?? entityType.Server.SpawnConditions?.Runtime?.MapCode ?? entityType.Server.SpawnConditions?.Worldgen?.MapCode;

                if (mapCode != null && spawnClimate.MatchesClimate(climate) && spawnConditions.CanSpawnInside(block))
                {
                    bool likesBait = entityType.Attributes["baitTags"].AsArray<string>().Contains<string>(bait);
                    ByteDataMap2D animalMap = mapRegion.AnimalSpawnMaps.Get(mapCode);

                    if (likesBait && animalMap.GetUnpaddedLerped(xInAnimalMap, zInAnimalMap) > 128f)
                    {
                        spawnable.Add(entityType);
                    }
                }
            }

            if (printDebug) Debug.WriteLine("1. Found suitable fish types: " + string.Join(", ", spawnable.Select(props => props.Code)));

            if (spawnable.Count == 0) // no abundance and no catch chance if no valid fish types
            {
                __result = null;
                return false;
            }

            double noisyAbundance = (__instance.Api.ModLoader.GetModSystem<FishingSupportModSystem>().NoiseGen.Noise(xYZ.X, xYZ.Z) - 0.4f) * 3.0;
            abundanceValue = (float)GameMath.Clamp(noisyAbundance, 0.2f, 1.0); 
            if (printDebug) Debug.WriteLine("2. Fish frequency map value: " + abundanceValue);

            abundanceValue *= (float)___pondSize / Options.Instance.MaxPondSize;
            if (printDebug) Debug.WriteLine("Pond size: " + ___pondSize);

            float alreadyHarvested = __instance.Api.ModLoader.GetModSystem<ModSystemFishDepletion>().GetHarvestAmount(__instance.Pos.XYZ.AsBlockPos);
            float maxHarvestable = (float)ModSystemFishDepletion.MaxHarvestablePerLocation * 0.8f;
            float remainingHarvestable = 1f - GameMath.Clamp(alreadyHarvested / maxHarvestable - 0.2f, 0f, 1f);
            abundanceValue *= remainingHarvestable;
            if (printDebug) Debug.WriteLine("4. Fish depletion here " + ((1 - remainingHarvestable) * 100) + "% (caught: " + alreadyHarvested + ")");

            __result = spawnable[__instance.Api.World.Rand.Next(spawnable.Count)];
            if (printDebug) Debug.WriteLine("5. Randomly selected fish: " + __result.Code);

            return false;
        }
    }

    [HarmonyPatch(typeof(EntityBobber), "getPondSize")]
    public class PondSizePatch
    {
        public static bool Prefix(EntityBobber __instance, ref HashSet<FastVec3i> ___visited, ref Queue<FastVec3i> ___bfsQueue, ref int __result)
        {
            ___visited.Clear();
            ___bfsQueue.Clear();
            BlockPos blockPos = __instance.Pos.AsBlockPos;
            ___bfsQueue.Enqueue(new FastVec3i(blockPos.X, blockPos.Y, blockPos.Z));
            BlockFacing[] directions =
            [
                BlockFacing.NORTH,
                BlockFacing.EAST,
                BlockFacing.SOUTH,
                BlockFacing.WEST,
                BlockFacing.DOWN
            ];
            __result = 0;
            while (___bfsQueue.Count > 0)
            {
                FastVec3i next = ___bfsQueue.Dequeue();
                foreach (BlockFacing direction in directions)
                {
                    blockPos.Set(next.X + direction.Normali.X, next.Y + direction.Normali.Y, next.Z + direction.Normali.Z);
                    FastVec3i pos = new FastVec3i(blockPos);
                    if (___visited.Add(pos))
                    {
                        if (__result > Options.Instance.MaxPondSize)
                        {
                            return false;
                        }

                        if (__instance.Api.World.BlockAccessor.GetBlock(blockPos, 2).Id != 0)
                        {
                            ___bfsQueue.Enqueue(pos);
                            __result++;
                        }
                    }
                }
            }
            return false;
        }
    }

    /* This method gets called twice on the server side and I don't know why. Let's say double damage is intentional when the rope breaks */
    [HarmonyPatch(typeof(EntityBobber), "OnRopeRipped")]
    public class RopeRippedPatch
    {
        public static bool Prefix(EntityBobber __instance, ClothSystem cs)
        {
            EntityAgent entity = __instance.Api.World.GetEntityById(__instance.AttachedToEntityId) as EntityAgent;
            ItemSlot slot = entity?.ActiveHandItemSlot;
            slot?.Itemstack?.Collectible?.DamageItem(__instance.Api.World, entity, slot);
            slot?.MarkDirty();
            return true;
        }
    }
}

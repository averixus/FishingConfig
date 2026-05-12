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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "wasSwimming")]
        extern static ref bool getWasSwimming(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swimmingAccum")]
        extern static ref float getSwimmingAccum(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "bobberState")]
        extern static ref EnumBobberState getBobberState(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ep")]
        extern static ref EntityPartitioning getEp(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "catchAccum")]
        extern static ref float getCatchAccum(EntityBobber @this);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "playCatchEffects")]
        extern static void playCatchEffects(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "printLocationDebugInfo")]
        extern static void printLocationDebugInfo(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getRandomFishEntityProperties")]
        extern static EntityProperties getRandomFishEntityProperties(EntityBobber @this, ItemStack baitStack, out float abundanceValue, bool printDebug = false);
        
        public static bool Prefix(EntityBobber __instance, float dt)
        {
            ref bool wasSwimming = ref getWasSwimming(__instance);
            ref float swimmingAccum = ref getSwimmingAccum(__instance);
            ref EnumBobberState bobberState = ref getBobberState(__instance);
            ref EntityPartitioning ep = ref getEp(__instance);
            ref float catchAccum = ref getCatchAccum(__instance);
            Options options = FishingConfigModSystem.options;

            getRandomFishEntityProperties(__instance, __instance.BaitStack, out float catchLikelihood, false);
            Console.WriteLine("[FishingConfig] catch likelihood retrieved " + catchLikelihood);

            if (__instance.Swimming && !wasSwimming)
            {
                Console.WriteLine("[FishingConfig] Bobber landed in water");
                if (__instance.Api.World.EntityDebugMode) printLocationDebugInfo(__instance);
                wasSwimming = true;
            }

            Console.WriteLine("[FishingConfig] Bobber state at start of tick: " + bobberState);
            switch(bobberState)
            {
                case EnumBobberState.Baiting:
                {
                    if (swimmingAccum > options.FishStartSearchDelay) // wait after casting, then check for entities or stock
                    {
                        Console.WriteLine("[FishingConfig] Checking for nearby entities");
                        Entity nearestEntity = ep.GetNearestEntity(__instance.Pos.XYZ, 20.0, (Entity e) => e is EntityFish, EnumEntitySearchType.Creatures);
                        bobberState = (nearestEntity != null) ? EnumBobberState.FishNearby : options.CatchStockFish ? EnumBobberState.NoFishNearby : EnumBobberState.Baiting;
                    }
                    break;
                }
                case EnumBobberState.FishNearby:
                {
                    if (swimmingAccum > options.FishLureEntityTimer) // if entity doesn't arrive after a while, assume it's gone
                    {
                        if (options.CatchStockFish) // switch to stock fish
                        {
                            Console.WriteLine("[FishingConfig] Entity has not arrived in time, switching to stock fish");
                            bobberState = EnumBobberState.NoFishNearby;

                        }
                        else // or reset to try again if not catching stock fish
                        {
                            Console.WriteLine("[FishingConfig] Entity has not arrived in time, resetting to baiting");
                            bobberState = EnumBobberState.Baiting;
                            swimmingAccum = 1f;
                        }
                    }
                    else // or catch if the entity comes close enough and attracted to bait
                    {
                        Entity nearestEntity = ep.GetNearestEntity(__instance.Pos.XYZ, 1.0, (Entity e) => e is EntityFish, EnumEntitySearchType.Creatures);
                        string bait = __instance.BaitStack?.Collectible.Attributes?["baitTag"].AsString() ?? "nobait";
                        if (nearestEntity != null && nearestEntity.Properties.Attributes["baitTags"].AsArray<string>().Contains<string>(bait))
                        {
                            Console.WriteLine("[FishingConfig] Catching entity " + nearestEntity);
                            bobberState = EnumBobberState.NoCatch; // using this as an alias for EntityFishCatch which should be a separate state
                            __instance.caughtFish = nearestEntity as EntityFish;
                            catchAccum += dt;
                            playCatchEffects(__instance);// .Invoke(__instance, []);
                        }
                    }
                    break;    
                }
                case EnumBobberState.NoFishNearby:
                {
                    if (catchLikelihood > 0 && swimmingAccum > options.MinStockCatchTime / Math.Max(options.MinStockCatchTime / options.MaxStockCatchTime, catchLikelihood)) // wait according to abundance, then catch from stock
                    {
                        Console.WriteLine("[FishingConfig] Catching from stock");
                        bobberState = __instance.Api.World.Rand.NextDouble() < (double) options.JunkCatchChance ?
                                EnumBobberState.JunkCatch : EnumBobberState.NoEntityFishCatch; // catch junk or stock fish according to chance
                        catchAccum += dt;
                        playCatchEffects(__instance);
                    }   
                    break;
                }
                case EnumBobberState.NoEntityFishCatch:
                case EnumBobberState.JunkCatch:
                case EnumBobberState.NoCatch: // using this as an alias for EntityFishCatch which should be a separate state
                {
                    if (catchAccum > options.ReelInTimer) // wait for player to reel in catch, then reset
                    {
                        Console.WriteLine("[FishingConfig] Player too slow to reel in");
                        if (__instance.caughtFish != null) // if there's a fish entity, let it go
                        {
                            Console.WriteLine("[FishingConfig] Releasing entity");
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
                        catchAccum = 0f; // reset timers so it doesn't catch again instantly
                        swimmingAccum = 0f;
                        bobberState = EnumBobberState.Baiting;
                    }
                    else
                    {
                        Console.WriteLine("[FishingConfig] Waiting for player to reel in");
                        catchAccum += dt;
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "bobberState")]
        extern static ref EnumBobberState getBobberState(EntityBobber @this);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getRandomFishEntityProperties")]
        extern static EntityProperties getRandomFishEntityProperties(EntityBobber @this, ItemStack baitStack, out float abundanceValue, bool printDebug = false);
        
        public static bool Prefix(EntityBobber __instance, EntityAgent entityCatcher)
        {
            EnumBobberState bobberState = getBobberState(__instance);
            Options options = FishingConfigModSystem.options;
 
            ItemStack[] drops = [];

            switch(bobberState)
            {
                case EnumBobberState.NoCatch: // alias for EntityFishCatch
                {
                    if (__instance.caughtFish != null && __instance.caughtFish.Alive)
                    {
                        Console.WriteLine("[FishingConfig] Killing entity and getting drops");
                        __instance.caughtFish.Die(EnumDespawnReason.Expire);
                        drops = __instance.caughtFish.GetDrops(__instance.World, __instance.caughtFish.Pos.XYZInt.AsBlockPos, (entityCatcher as EntityPlayer)?.Player);
                    }
                    break;
                }
                case EnumBobberState.NoEntityFishCatch:
                {
                    Console.WriteLine("[FishingConfig] Getting aged fish drops from stock");
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
                    Console.WriteLine("[FishingConfig] Getting junk drop");
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
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "pondSize")]
        extern static ref int getCurrentPondSize(EntityBobber @this);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "getPondSize")]
        extern static int calculatePondSize(EntityBobber @this);

        public static bool Prefix(EntityBobber __instance, ItemStack baitStack, out float abundanceValue, bool printDebug, ref EntityProperties __result)
        {
            Console.WriteLine("[FishingConfig] getRandomFishEntityProperties");
            int pondSize = getCurrentPondSize(__instance);
            Options options = FishingConfigModSystem.options;

            abundanceValue = 0f;

            ClimateCondition climate = __instance.World.BlockAccessor.GetClimateAt(__instance.Pos.AsBlockPos, EnumGetClimateMode.WorldGenValues);

            if (climate == null) // no abundance and no catch chance if invalid climate
            {
                Console.WriteLine("[FishingConfig] Invalid climate for fish stock");
                __result = null;
                return false;
            }

            pondSize = pondSize < 0 ? (int) calculatePondSize(__instance) : pondSize; // calculate if not yet done
            if (pondSize < options.MinPondSize) // no abundance and no catch chance if pond too small
            {
                Console.WriteLine("[FishingConfig] Pond too small for fish stock");
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
                        Console.WriteLine("[FishingConfig] Adding possible catch to fish stock: " + entityType.Class);
                        spawnable.Add(entityType);
                    }
                }
            }

            if (printDebug) System.Diagnostics.Debug.WriteLine("1. Found suitable fish types: " + string.Join(", ", spawnable.Select(props => props.Code)));

            if (spawnable.Count == 0) // no abundance and no catch chance if no valid fish types
            {
                Console.WriteLine("[FishingConfig] No valid stock fish");
                __result = null;
                return false;
            }

            double noisyAbundance = (__instance.Api.ModLoader.GetModSystem<FishingSupportModSystem>().NoiseGen.Noise(xYZ.X, xYZ.Z) - 0.4000000059604645) * 3.0;
            abundanceValue = (float)GameMath.Clamp(noisyAbundance, 0.20000000298023224, 1.0); 
            if (printDebug) System.Diagnostics.Debug.WriteLine("2. Fish frequency map value: " + abundanceValue);

            abundanceValue *= (float)pondSize / options.MaxPondSize;
            if (printDebug) System.Diagnostics.Debug.WriteLine("Pond size: " + pondSize);

            float alreadyHarvested = __instance.Api.ModLoader.GetModSystem<ModSystemFishDepletion>().GetHarvestAmount(__instance.Pos.XYZ.AsBlockPos);
            float maxHarvestable = (float)ModSystemFishDepletion.MaxHarvestablePerLocation * 0.8f;
            float remainingHarvestable = 1f - GameMath.Clamp(alreadyHarvested / maxHarvestable - 0.2f, 0f, 1f);
            abundanceValue *= remainingHarvestable;
            if (printDebug) System.Diagnostics.Debug.WriteLine("4. Fish depletion here " + ((1 - remainingHarvestable) * 100) + "% (caught: " + alreadyHarvested + ")");

            __result = spawnable[__instance.Api.World.Rand.Next(spawnable.Count)];
            if (printDebug) System.Diagnostics.Debug.WriteLine("5. Randomly selected fish: " + __result.Code);

            Console.WriteLine("[FishingConfig] Final abundance: " + abundanceValue);
            return false;
        }
    }

    [HarmonyPatch(typeof(EntityBobber), "getPondSize")]
    public class PondSizePatch
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "visited")]
        extern static ref HashSet<FastVec3i> getVisited(EntityBobber @this);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "bfsQueue")]
        extern static ref Queue<FastVec3i> getBfsQueue(EntityBobber @this);

        public static bool Prefix(EntityBobber __instance, ref int __result)
        {
            HashSet<FastVec3i> visited = getVisited(__instance); 
            Queue<FastVec3i> bfsQueue = getBfsQueue(__instance); 

            IBlockAccessor blockAccessor = __instance.Api.World.BlockAccessor;
            visited.Clear();
            bfsQueue.Clear();
            BlockPos blockPos = __instance.Pos.AsBlockPos;
            bfsQueue.Enqueue(new FastVec3i(blockPos.X, blockPos.Y, blockPos.Z));
            BlockFacing[] directions =
            [
                BlockFacing.NORTH,
                BlockFacing.EAST,
                BlockFacing.SOUTH,
                BlockFacing.WEST,
                BlockFacing.DOWN
            ];
            __result = 0;
            while (bfsQueue.Count > 0)
            {
                FastVec3i next = bfsQueue.Dequeue();
                foreach (BlockFacing direction in directions)
                {
                    blockPos.Set(next.X + direction.Normali.X, next.Y + direction.Normali.Y, next.Z + direction.Normali.Z);
                    FastVec3i pos = new FastVec3i(blockPos);
                    if (visited.Add(pos))
                    {
                        if (__result > FishingConfigModSystem.options.MaxPondSize)
                        {
                            return false;
                        }

                        if (blockAccessor.GetBlock(blockPos, 2).Id != 0)
                        {
                            bfsQueue.Enqueue(pos);
                            __result++;
                        }
                    }
                }
            }
            return false;
        }
    }

/* This method gets called twice on the server side and I don't know why */
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

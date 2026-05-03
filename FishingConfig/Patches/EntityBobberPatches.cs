using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace FishingConfig.Patches
{
    [HarmonyPatch(typeof(EntityBobber), "onServertick")]
    public class ServerTickPatch
    {
        static FieldInfo wasSwimmingField = typeof(EntityBobber).GetField("wasSwimming", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo swimmingAccumField = typeof(EntityBobber).GetField("swimmingAccum", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo bobberStateField = typeof(EntityBobber).GetField("bobberState", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo epField = typeof(EntityBobber).GetField("ep", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo junkCatchChanceField = typeof(EntityBobber).GetField("junkCatchChance", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo catchAccumField = typeof(EntityBobber).GetField("catchAccum", BindingFlags.Instance | BindingFlags.NonPublic);
        static FieldInfo accumField = typeof(EntityBobber).GetField("accum", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo playCatchEffects = typeof(EntityBobber).GetMethod("playCatchEffects", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo printLocationDebugInfo = typeof(EntityBobber).GetMethod("printLocationDebugInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo getRandomFishEntityProperties = typeof(EntityBobber).GetMethod("getRandomFishEntityProperties", BindingFlags.Instance | BindingFlags.NonPublic);
        public static bool Prefix(EntityBobber __instance, float dt)
        {
            TypedReference thisBobber = __makeref(__instance);

            bool wasSwimming = (bool) wasSwimmingField.GetValueDirect(thisBobber);
            float swimmingAccum = (float) swimmingAccumField.GetValueDirect(thisBobber);
            EnumBobberState bobberState = (EnumBobberState) bobberStateField.GetValueDirect(thisBobber);
            EntityPartitioning ep = (EntityPartitioning) epField.GetValueDirect(thisBobber);
            float junkCatchChance = (float) junkCatchChanceField.GetValueDirect(thisBobber);
            float catchAccum = (float) catchAccumField.GetValueDirect(thisBobber);
            float accum = (float) accumField.GetValueDirect(thisBobber);

            object[] parameters = [__instance.BaitStack, 0.5f, false];
            EntityProperties fishCatch = (EntityProperties) getRandomFishEntityProperties.Invoke(__instance, parameters);
            float catchLikelihood = (float)parameters[1];

            if (__instance.Swimming && !wasSwimming)
            {
                wasSwimming = true;
                if (__instance.Api.World.EntityDebugMode)
                {
                    printLocationDebugInfo.Invoke(__instance, []);
                }
            }

            switch(bobberState)
            {
                case EnumBobberState.Baiting:
                {
                    if (swimmingAccum > 1f) // wait 1 second after casting, then check for entities or stock
                    {
                        Entity nearestEntity = ep.GetNearestEntity(__instance.Pos.XYZ, 20.0, (Entity e) => e is EntityFish, EnumEntitySearchType.Creatures);
                        bobberState = (nearestEntity != null) ? EnumBobberState.FishNearby : EnumBobberState.NoFishNearby;
                    }
                    break;
                }
                case EnumBobberState.FishNearby:
                {
                    if (swimmingAccum > 15f) // if entity doesn't arrive after 15 seconds assume it's gone
                    {
                        bobberState = EnumBobberState.NoFishNearby;
                    }
                    else // or catch if the entity comes close enough
                    {
                        Entity nearestEntity = ep.GetNearestEntity(__instance.Pos.XYZ, 1.0, (Entity e) => e is EntityFish, EnumEntitySearchType.Creatures);
                        if (nearestEntity != null) 
                        {
                            bobberState = EnumBobberState.NoCatch; // using this as an alias for EntityFishCatch which should be a separate state
                            __instance.caughtFish = nearestEntity as EntityFish;
                            catchAccum += dt;
                            playCatchEffects.Invoke(__instance, []);
                        }
                    }
                    break;    
                }
                case EnumBobberState.NoFishNearby:
                {
                    if (catchLikelihood > 0 && swimmingAccum > 5.0 / Math.Max(0.04, catchLikelihood)) // wait according to abundance, then catch from stock
                    {
                        bobberState = __instance.Api.World.Rand.NextDouble() < (double) junkCatchChance ?
                                EnumBobberState.JunkCatch : EnumBobberState.NoEntityFishCatch; // catch junk or stock fish according to chance
                        catchAccum += dt;
                        playCatchEffects.Invoke(__instance, []);
                    }   
                    break;
                }
                case EnumBobberState.NoEntityFishCatch:
                case EnumBobberState.JunkCatch:
                case EnumBobberState.NoCatch: // using this as an alias for EntityFishCatch which should be a separate state
                {
                    if (catchAccum > 0.7f) // wait 0.7 seconds for player to reel in catch, then reset
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
                        catchAccum = 0f; // reset timers so it doesn't catch again instantly
                        swimmingAccum = 0f;
                        bobberState = EnumBobberState.Baiting;
                    }
                    else
                    {
                        catchAccum += dt;
                    }
                    break;
                }
            }

            wasSwimmingField.SetValueDirect(thisBobber, wasSwimming);
            swimmingAccumField.SetValueDirect(thisBobber, swimmingAccum);
            bobberStateField.SetValueDirect(thisBobber, bobberState);
            // epField.SetValueDirect(thisBobber, ep); never edited
            // junkCatchChanceField.SetValueDirect(thisBobber, junkCatchChance); never edited
            catchAccumField.SetValueDirect(thisBobber, catchAccum);
            accumField.SetValueDirect(thisBobber, accum);

            return false;
        }
    }

    [HarmonyPatch(typeof(EntityBobber), "getRandomFishEntityProperties")]
    
    [HarmonyPatch(typeof(EntityBobber), "TryCatchFish")]
    public class TryCatchPatch
    {
        static FieldInfo bobberStateField = typeof(EntityBobber).GetField("bobberState", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo getRandomFishEntityProperties = typeof(EntityBobber).GetMethod("getRandomFishEntityProperties", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Prefix(EntityBobber __instance, EntityAgent entityCatcher)
        {
            TypedReference thisBobber = __makeref(__instance);

            EnumBobberState bobberState = (EnumBobberState) bobberStateField.GetValueDirect(thisBobber);
 
            ItemStack[] drops = [];

            switch(bobberState)
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
                    object[] parameters = [__instance.BaitStack, 0f, false];
                    EntityProperties fishCatch = (EntityProperties) getRandomFishEntityProperties.Invoke(__instance, parameters);
                    float abundanceValue = (float)parameters[1];

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

            __instance.BaitStack = null;
            __instance.WatchedAttributes.MarkPathDirty("baitStack");

            foreach (ItemStack drop in drops)
            {
                if (!entityCatcher.TryGiveItemStack(drop))
                {
                    __instance.World.SpawnItemEntity(drop, entityCatcher.Pos.XYZ);
                }
            }

            return false;
        }
    }
    public class RandomFishPatch
    {
        static FieldInfo tmpPosField = typeof(EntityBobber).GetField("tmpPos", BindingFlags.Instance | BindingFlags.NonPublic); 
        static FieldInfo pondSizeField = typeof(EntityBobber).GetField("pondSize", BindingFlags.Instance | BindingFlags.NonPublic);
        static MethodInfo getPondSize = typeof(EntityBobber).GetMethod("getPondSize", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Prefix(EntityBobber __instance, ItemStack baitStack, out float abundanceValue, bool printDebug, ref EntityProperties __result)
        {
            TypedReference thisBobber = __makeref(__instance);
            BlockPos tmpPos = (BlockPos) tmpPosField.GetValueDirect(thisBobber); 
            int pondSize = (int) pondSizeField.GetValueDirect(thisBobber);

            abundanceValue = 0f;

            ClimateCondition climate = __instance.World.BlockAccessor.GetClimateAt(__instance.Pos.AsBlockPos, EnumGetClimateMode.WorldGenValues);

            if (climate == null) // no abundance and no catch chance if invalid climate
            {
                __result = null;
                return false;
            }

            pondSize = pondSize < 0 ? (int) getPondSize.Invoke(__instance, []) : pondSize; // calculate if not yet done
            if (pondSize < 100) // no abundance and no catch chance if pond too small
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

            if (printDebug)
            {
                System.Diagnostics.Debug.WriteLine("1. Found suitable fish types: " + string.Join(", ", spawnable.Select(props => props.Code)));
            }

            if (spawnable.Count == 0) // no abundance and no catch chance if no valid fish types
            {
                __result = null;
                return false;
            }

            double noisyAbundance = (__instance.Api.ModLoader.GetModSystem<FishingSupportModSystem>().NoiseGen.Noise(xYZ.X, xYZ.Z) - 0.4000000059604645) * 3.0;
            abundanceValue = (float)GameMath.Clamp(noisyAbundance, 0.20000000298023224, 1.0); 
            if (printDebug)
            {
                System.Diagnostics.Debug.WriteLine("2. Fish frequency map value: " + abundanceValue);
            }

            abundanceValue *= (float)pondSize / 1200f;
            if (printDebug)
            {
                System.Diagnostics.Debug.WriteLine("Pond size: " + pondSize);
            }

            float alreadyHarvested = __instance.Api.ModLoader.GetModSystem<ModSystemFishDepletion>().GetHarvestAmount(__instance.Pos.XYZ.AsBlockPos);
            float maxHarvestable = (float)ModSystemFishDepletion.MaxHarvestablePerLocation * 0.8f;
            float remainingHarvestable = 1f - GameMath.Clamp(alreadyHarvested / maxHarvestable - 0.2f, 0f, 1f);
            abundanceValue *= remainingHarvestable;
           if (printDebug)
            {
                System.Diagnostics.Debug.WriteLine("4. Fish depletion here " + ((1 - remainingHarvestable) * 100) + "% (caught: " + alreadyHarvested + ")");
            }

            __result = spawnable[__instance.Api.World.Rand.Next(spawnable.Count)];
            if (printDebug)
            {
                System.Diagnostics.Debug.WriteLine("5. Randomly selected fish: " + __result.Code);
            }
            return false;
        }
    }
}
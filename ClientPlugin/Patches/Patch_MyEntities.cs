using System;
using ClientPlugin.Services;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRageMath;
using VRage.Utils;
using VRage.Game;

namespace ClientPlugin.Patches
{
    [HarmonyPatch(typeof(MyEntities))]
    public static class Patch_MyEntities
    {
        private static EntityStreamingManager StreamingManager => Plugin.Instance?.EntityStreamingManager;
        private static VoxelPhysicsOptimizer VoxelOptimizer => Plugin.Instance?.VoxelPhysicsOptimizer;
        private static bool IsEnabled => Plugin.Instance?.Config?.Enabled ?? false;

        // Intercept parallel entity creation to queue instead
        [HarmonyPatch("CreateFromObjectBuilderParallel")]
        [HarmonyPrefix]
        public static bool CreateFromObjectBuilderParallel_Prefix(
            MyObjectBuilder_EntityBase objectBuilder,
            bool addToScene,
            Action<MyEntity> completionCallback,
            MyEntity entity,
            MyEntity relativeSpawner,
            Vector3D? relativeOffset,
            bool checkPosition,
            bool fadeIn)
        {
            if (!IsEnabled || StreamingManager == null)
                return true; // Continue with original method

            try
            {
                // Only intercept voxel/asteroid creation
                if (objectBuilder is MyObjectBuilder_VoxelMap voxelBuilder)
                {
                    // Check if this is an asteroid that should be queued
                    if (ShouldQueueVoxel(voxelBuilder))
                    {
                        LogDebug($"Intercepted parallel voxel creation: {voxelBuilder.StorageName}");
                        
                        // Queue the entity instead of immediate creation
                        var priority = EntityStreamingManager.EntityPriority.Low;
                        var position = objectBuilder.PositionAndOrientation?.Position ?? Vector3D.Zero;
                        
                        StreamingManager.QueueEntity(objectBuilder.EntityId, priority, position);
                        
                        // Call completion callback with null to indicate pending
                        completionCallback?.Invoke(null);
                        
                        return false; // Skip original method
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in CreateFromObjectBuilderParallel_Prefix: {ex}");
            }

            return true; // Continue with original
        }

        // Intercept synchronous entity creation
        [HarmonyPatch("CreateFromObjectBuilder")]
        [HarmonyPrefix]
        public static bool CreateFromObjectBuilder_Prefix(
            MyObjectBuilder_EntityBase objectBuilder,
            bool fadeIn,
            ref MyEntity __result)
        {
            if (!IsEnabled || StreamingManager == null)
                return true;

            try
            {
                // Only intercept voxel/asteroid creation
                if (objectBuilder is MyObjectBuilder_VoxelMap voxelBuilder)
                {
                    if (ShouldQueueVoxel(voxelBuilder))
                    {
                        LogDebug($"Intercepted sync voxel creation: {voxelBuilder.StorageName}");
                        
                        // Queue the entity
                        var priority = EntityStreamingManager.EntityPriority.Normal;
                        var position = objectBuilder.PositionAndOrientation?.Position ?? Vector3D.Zero;
                        
                        StreamingManager.QueueEntity(objectBuilder.EntityId, priority, position);
                        
                        // Return null to indicate entity is pending
                        __result = null;
                        return false; // Skip original method
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in CreateFromObjectBuilder_Prefix: {ex}");
            }

            return true;
        }

        // Hook entity addition to track when entities are actually created
        [HarmonyPatch("Add")]
        [HarmonyPostfix]
        public static void Add_Postfix(MyEntity entity, bool insertIntoScene)
        {
            if (!IsEnabled || StreamingManager == null)
                return;

            try
            {
                // Notify streaming manager that entity was created
                StreamingManager.OnEntityCreated(entity.EntityId);
                
                // If this is a voxel with deferred physics, handle it
                if (entity is MyVoxelBase voxel && voxel.DelayRigidBodyCreation)
                {
                    LogDebug($"Entity {entity.EntityId} added with deferred physics");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in Add_Postfix: {ex}");
            }
        }

        private static bool ShouldQueueVoxel(MyObjectBuilder_VoxelMap voxelBuilder)
        {
            // Queue asteroids during streaming
            if (MySession.Static?.StreamingInProgress ?? false)
                return true;
            
            // Queue asteroids based on name pattern
            if (voxelBuilder.StorageName != null && 
                (voxelBuilder.StorageName.Contains("asteroid") || 
                 voxelBuilder.StorageName.Contains("Asteroid")))
                return true;
            
            return false;
        }

        private static void LogDebug(string message)
        {
            if (Plugin.Instance?.Config?.LogEntityStreaming ?? false)
            {
                MyLog.Default.WriteLine($"[Phyzix.Entities] {message}");
            }
        }

        private static void LogError(string message)
        {
            MyLog.Default.WriteLine($"[Phyzix.Entities.ERROR] {message}");
        }
    }
}
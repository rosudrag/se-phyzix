using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Sandbox.Engine.Voxels;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Game.Voxels;
using VRage.ModAPI;
using VRage.Utils;
using VRage.Voxels;
using VRageMath;

namespace ClientPlugin.Services
{
    public class VoxelPhysicsOptimizer
    {
        private readonly ConcurrentDictionary<long, DeferredPhysicsData> _deferredPhysics = new ConcurrentDictionary<long, DeferredPhysicsData>();
        private readonly Config _config;
        private readonly EntityValidationQueue _validationQueue;
        
        public int DeferredPhysicsCount => _deferredPhysics.Count;

        public VoxelPhysicsOptimizer(Config config, EntityValidationQueue validationQueue)
        {
            _config = config;
            _validationQueue = validationQueue;
        }

        public bool ShouldDeferPhysics(MyVoxelBase voxelBase)
        {
            if (!_config.DeferVoxelPhysics)
                return false;

            // Don't defer physics for planets
            if (voxelBase is MyPlanet)
                return false;

            // Check if this is likely an asteroid that needs validation
            if (voxelBase.StorageName != null && voxelBase.StorageName.Contains("asteroid"))
                return true;

            // Check size - small asteroids are more likely to be the spam ones
            var size = voxelBase.Size;
            if (size.X < 256 && size.Y < 256 && size.Z < 256)
                return true;

            return false;
        }

        public void DeferPhysicsCreation(MyVoxelBase voxelBase)
        {
            var data = new DeferredPhysicsData
            {
                EntityId = voxelBase.EntityId,
                Position = voxelBase.PositionComp.GetPosition(),
                WorldMatrix = voxelBase.WorldMatrix,
                Size = voxelBase.Size,
                StorageMin = voxelBase.StorageMin,
                StorageMax = voxelBase.StorageMax,
                VoxelBase = voxelBase,
                DeferTime = DateTime.UtcNow
            };

            if (_deferredPhysics.TryAdd(voxelBase.EntityId, data))
            {
                LogDebug($"Deferred physics for voxel {voxelBase.EntityId} ({voxelBase.StorageName})");
                
                // Add to validation queue
                _validationQueue.QueueForValidation(voxelBase.EntityId, ValidationType.Asteroid);
            }
        }

        public void CreatePhysicsForValidatedEntity(long entityId)
        {
            if (!_deferredPhysics.TryRemove(entityId, out var data))
                return;

            if (data.VoxelBase == null || data.VoxelBase.Closed)
                return;

            try
            {
                // Create physics for the voxel
                CreateVoxelPhysics(data.VoxelBase);
                LogDebug($"Created physics for validated voxel {entityId}");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to create physics for voxel {entityId}: {ex.Message}");
            }
        }

        public void RemoveInvalidEntity(long entityId)
        {
            if (!_deferredPhysics.TryRemove(entityId, out var data))
                return;

            if (data.VoxelBase != null && !data.VoxelBase.Closed)
            {
                try
                {
                    // Close the entity without creating physics
                    data.VoxelBase.Close();
                    LogDebug($"Removed invalid voxel {entityId}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Failed to remove invalid voxel {entityId}: {ex.Message}");
                }
            }
        }

        public void ProcessTimeouts()
        {
            var now = DateTime.UtcNow;
            var timeoutMs = _config.ValidationTimeoutMs * 3; // Give extra time for voxels

            foreach (var kvp in _deferredPhysics)
            {
                if ((now - kvp.Value.DeferTime).TotalMilliseconds > timeoutMs)
                {
                    // Timeout - create physics anyway to avoid issues
                    CreatePhysicsForValidatedEntity(kvp.Key);
                }
            }
        }

        private void CreateVoxelPhysics(MyVoxelBase voxelBase)
        {
            // Reset the deferred flag to allow physics creation
            voxelBase.DelayRigidBodyCreation = false;
            
            // If physics is already created, nothing to do
            if (voxelBase.Physics != null)
                return;
            
            try
            {
                // For MyVoxelMap (asteroids), we need to trigger physics creation
                if (voxelBase is MyVoxelMap voxelMap)
                {
                    // Use reflection to call the protected CreateVoxelPhysics method
                    var createPhysicsMethod = typeof(MyVoxelBase).GetMethod("CreateVoxelPhysics", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (createPhysicsMethod != null)
                    {
                        createPhysicsMethod.Invoke(voxelMap, null);
                        LogDebug($"Created physics for asteroid {voxelMap.StorageName}");
                    }
                    else
                    {
                        // Fallback: Force physics update on next frame
                        voxelMap.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                        LogDebug($"Scheduled physics creation for asteroid {voxelMap.StorageName}");
                    }
                }
                else
                {
                    // For other voxel types (like planet physics chunks)
                    // Force update to create physics
                    voxelBase.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                    LogDebug($"Scheduled physics creation for voxel entity {voxelBase.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error creating physics for voxel {voxelBase.EntityId}: {ex.Message}");
                // As a last resort, ensure the entity will update and create physics naturally
                voxelBase.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
            }
        }

        public void Clear()
        {
            foreach (var kvp in _deferredPhysics)
            {
                RemoveInvalidEntity(kvp.Key);
            }
            _deferredPhysics.Clear();
        }

        private void LogDebug(string message)
        {
            if (_config.LogEntityStreaming)
            {
                MyLog.Default.WriteLine($"[Phyzix.VoxelPhysics] {message}");
            }
        }

        public class DeferredPhysicsData
        {
            public long EntityId { get; set; }
            public Vector3D Position { get; set; }
            public MatrixD WorldMatrix { get; set; }
            public Vector3I Size { get; set; }
            public Vector3I StorageMin { get; set; }
            public Vector3I StorageMax { get; set; }
            public MyVoxelBase VoxelBase { get; set; }
            public DateTime DeferTime { get; set; }
        }
    }
}
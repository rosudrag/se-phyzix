using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Game.Entities;
using MyEntities = Sandbox.Game.Entities.MyEntities;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Services
{
    public class VoxelPhysicsOptimizer
    {
        private readonly ConcurrentDictionary<long, DeferredPhysicsData> _deferredPhysics = new ConcurrentDictionary<long, DeferredPhysicsData>();
        private readonly ConcurrentQueue<long> _physicsCreationQueue = new ConcurrentQueue<long>();
        private readonly Config _config;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _processingTask;
        private DateTime _lastPhysicsCreation = DateTime.MinValue;
        
        private static readonly MethodInfo _createVoxelPhysicsMethod = typeof(MyVoxelBase).GetMethod("CreateVoxelPhysics", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        public int DeferredPhysicsCount => _deferredPhysics.Count;

        public VoxelPhysicsOptimizer(Config config)
        {
            _config = config;
            StartProcessing();
        }

        private void StartProcessing()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = Task.Run(async () => await ProcessPhysicsQueueAsync(_cancellationTokenSource.Token));
        }

        private async Task ProcessPhysicsQueueAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_physicsCreationQueue.TryDequeue(out var entityId))
                    {
                        // Check if entity is still in deferred list (not removed by server)
                        if (!_deferredPhysics.ContainsKey(entityId))
                        {
                            LogDebug($"Skipping physics creation for removed entity {entityId}");
                            continue;
                        }
                        
                        // Ensure minimum delay between physics creations
                        var timeSinceLastCreation = (DateTime.UtcNow - _lastPhysicsCreation).TotalMilliseconds;
                        var remainingDelay = _config.VoxelPhysicsDelayMs - timeSinceLastCreation;
                        
                        if (remainingDelay > 0)
                        {
                            await Task.Delay((int)remainingDelay, cancellationToken);
                        }

                        // Double-check entity is still valid before creating physics
                        if (_deferredPhysics.ContainsKey(entityId))
                        {
                            CreatePhysicsForValidatedEntity(entityId);
                            _lastPhysicsCreation = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        // No items in queue, wait a bit before checking again
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error in physics processing queue: {ex.Message}");
                }
            }
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
                
                // Add to queue for sequential processing with delays
                _physicsCreationQueue.Enqueue(voxelBase.EntityId);
            }
        }

        private void CreatePhysicsForValidatedEntity(long entityId)
        {
            if (!_deferredPhysics.TryRemove(entityId, out var data))
                return;

            if (data.VoxelBase == null || data.VoxelBase.Closed)
            {
                LogDebug($"Entity {entityId} was closed before physics creation");
                return;
            }
            
            // Final check - ensure entity still exists in world
            var entity = MyEntities.GetEntityById(entityId);
            if (entity == null || entity.Closed)
            {
                LogDebug($"Entity {entityId} no longer exists in world, skipping physics creation");
                return;
            }

            try
            {
                // Create physics for the voxel
                CreateVoxelPhysics(data.VoxelBase);
                LogDebug($"Created physics for validated voxel {entityId}");
                
                // Notify entity streaming manager
                Plugin.Instance?.EntityStreamingManager?.OnEntityCreated(entityId);
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to create physics for voxel {entityId}: {ex.Message}");
            }
        }

        public void RemoveInvalidEntity(long entityId)
        {
            // Remove from deferred physics immediately to prevent creation
            if (!_deferredPhysics.TryRemove(entityId, out var data))
                return;

            LogDebug($"Removed entity {entityId} from physics queue due to server request");
            
            if (data.VoxelBase != null && !data.VoxelBase.Closed)
            {
                try
                {
                    // Close the entity without creating physics
                    data.VoxelBase.Close();
                    LogDebug($"Closed invalid voxel {entityId}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Failed to close invalid voxel {entityId}: {ex.Message}");
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
                    // Use cached method to avoid reflection overhead
                    if (_createVoxelPhysicsMethod != null)
                    {
                        _createVoxelPhysicsMethod.Invoke(voxelMap, null);
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
            // Stop processing
            _cancellationTokenSource?.Cancel();
            try
            {
                _processingTask?.Wait(1000);
            }
            catch { }
            
            foreach (var kvp in _deferredPhysics)
            {
                RemoveInvalidEntity(kvp.Key);
            }
            _deferredPhysics.Clear();
            
            // Clear the queue
            while (_physicsCreationQueue.TryDequeue(out _)) { }
        }

        private void LogDebug(string message)
        {
            if (_config.LogEntityStreaming)
            {
                MyLog.Default.WriteLine($"[Phyzix.VoxelPhysics] {message}");
            }
        }

        private class DeferredPhysicsData
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
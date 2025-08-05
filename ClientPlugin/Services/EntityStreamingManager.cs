using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Network;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Services
{
    public class EntityStreamingManager
    {
        private readonly ConcurrentQueue<PendingEntity> _entityQueue = new ConcurrentQueue<PendingEntity>();
        private readonly HashSet<long> _processingEntities = new HashSet<long>();
        private readonly Dictionary<long, DateTime> _lastRequestTime = new Dictionary<long, DateTime>();
        private readonly Config _config;
        
        private static readonly TimeSpan RequestCooldown = TimeSpan.FromSeconds(1);
        private int _currentFrameProcessed = 0;
        
        public bool IsStreamingActive => MySession.Static?.StreamingInProgress ?? false;
        public int QueuedEntities => _entityQueue.Count;
        public int ProcessingEntities => _processingEntities.Count;

        public EntityStreamingManager(Config config)
        {
            _config = config;
        }

        public void QueueEntity(long entityId, EntityPriority priority = EntityPriority.Normal, Vector3D? position = null)
        {
            // Check if entity is stuck in processing
            if (_processingEntities.Contains(entityId))
            {
                // Allow re-queuing if it's been stuck for too long
                if (_lastRequestTime.TryGetValue(entityId, out var requestTime))
                {
                    if (DateTime.UtcNow - requestTime < TimeSpan.FromSeconds(30))
                    {
                        return; // Still processing, don't re-queue
                    }
                    else
                    {
                        // It's been stuck too long, remove and re-queue
                        _processingEntities.Remove(entityId);
                        _lastRequestTime.Remove(entityId);
                        LogDebug($"Re-queuing stuck entity {entityId}");
                    }
                }
            }

            var pendingEntity = new PendingEntity
            {
                EntityId = entityId,
                Priority = priority,
                Position = position,
                QueueTime = DateTime.UtcNow
            };

            _entityQueue.Enqueue(pendingEntity);
        }

        public void ProcessQueue()
        {
            if (IsStreamingActive)
                return;

            // Clean up stuck entities first
            CleanupStuckEntities();

            _currentFrameProcessed = 0;

            // Sort queue by priority and distance if enabled
            var sortedQueue = GetSortedQueue();

            foreach (var entity in sortedQueue)
            {
                if (_currentFrameProcessed >= _config.EntityBatchSize)
                    break;

                if (ShouldProcessEntity(entity))
                {
                    ProcessEntity(entity);
                    _currentFrameProcessed++;
                }
            }
        }

        private List<PendingEntity> GetSortedQueue()
        {
            var entities = new List<PendingEntity>();
            var tempQueue = new Queue<PendingEntity>();

            // Drain the concurrent queue
            while (_entityQueue.TryDequeue(out var entity))
            {
                entities.Add(entity);
            }

            // Sort by priority and distance
            if (_config.EnableDistancePriority && MySession.Static?.LocalCharacter != null)
            {
                var playerPos = MySession.Static.LocalCharacter.PositionComp.GetPosition();
                entities = entities.OrderByDescending(e => e.Priority)
                    .ThenBy(e => e.Position.HasValue ? Vector3D.DistanceSquared(e.Position.Value, playerPos) : double.MaxValue)
                    .ToList();
            }
            else
            {
                entities = entities.OrderByDescending(e => e.Priority).ToList();
            }

            // Re-queue entities that won't be processed this frame
            for (int i = _config.EntityBatchSize; i < entities.Count; i++)
            {
                _entityQueue.Enqueue(entities[i]);
            }

            return entities.Take(_config.EntityBatchSize).ToList();
        }

        private bool ShouldProcessEntity(PendingEntity entity)
        {
            // Check if entity already exists
            if (MyEntities.GetEntityById(entity.EntityId) != null)
                return false;

            // Check cooldown
            if (_lastRequestTime.TryGetValue(entity.EntityId, out var lastTime))
            {
                if (DateTime.UtcNow - lastTime < RequestCooldown)
                    return false;
            }

            // Check timeout
            if (DateTime.UtcNow - entity.QueueTime > TimeSpan.FromMilliseconds(_config.ValidationTimeoutMs * 2))
            {
                LogDebug($"Entity {entity.EntityId} timed out in queue");
                return false;
            }

            return true;
        }

        private void ProcessEntity(PendingEntity entity)
        {
            _processingEntities.Add(entity.EntityId);
            _lastRequestTime[entity.EntityId] = DateTime.UtcNow;

            // Request the entity from server
            RequestEntityFromServer(entity.EntityId);

            LogDebug($"Processing entity {entity.EntityId} with priority {entity.Priority}");
        }

        private void RequestEntityFromServer(long entityId)
        {
            // Direct request bypassing our patch to avoid recursion
            RequestEntityDirect(entityId);
        }

        public static void RequestEntityDirect(long entityId)
        {
            var replicationClient = GetReplicationClientStatic();
            if (replicationClient != null)
            {
                try
                {
                    // Temporarily disable our patch to avoid recursion
                    var patchEnabled = Plugin.Instance?.Config?.Enabled ?? false;
                    if (patchEnabled && Plugin.Instance != null)
                        Plugin.Instance.Config.Enabled = false;
                        
                    try
                    {
                        replicationClient.RequestReplicable(entityId, 0, true, 0L, 0L);
                            
                        if (Plugin.Instance?.Config?.LogEntityStreaming ?? false)
                            MyLog.Default.WriteLine($"[Phyzix] Requested entity {entityId} from server");
                    }
                    finally
                    {
                        // Re-enable patch
                        if (patchEnabled && Plugin.Instance != null)
                            Plugin.Instance.Config.Enabled = true;
                    }
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLine($"[Phyzix] Failed to request entity {entityId}: {ex.Message}");
                }
            }
        }

        private static MyReplicationClient GetReplicationClientStatic()
        {
            var multiplayer = MyMultiplayer.Static;
            if (multiplayer == null) return null;

            return MyMultiplayer.Static?.ReplicationLayer as MyReplicationClient;
        }

        public void OnEntityCreated(long entityId)
        {
            _processingEntities.Remove(entityId);
            _lastRequestTime.Remove(entityId);
        }

        public void ClearQueue()
        {
            while (_entityQueue.TryDequeue(out _)) { }
            _processingEntities.Clear();
            _lastRequestTime.Clear();
        }

        private void CleanupStuckEntities()
        {
            var stuckTimeout = TimeSpan.FromSeconds(30); // Entities stuck for more than 30 seconds
            var toRemove = new List<long>();

            foreach (var entityId in _processingEntities)
            {
                // Check if this entity has been processing for too long
                if (_lastRequestTime.TryGetValue(entityId, out var requestTime))
                {
                    if (DateTime.UtcNow - requestTime > stuckTimeout)
                    {
                        toRemove.Add(entityId);
                        LogDebug($"Removing stuck entity {entityId} from processing queue (timeout)");
                    }
                }
                else
                {
                    // No request time recorded, remove it
                    toRemove.Add(entityId);
                    LogDebug($"Removing stuck entity {entityId} from processing queue (no request time)");
                }
            }

            foreach (var entityId in toRemove)
            {
                _processingEntities.Remove(entityId);
                _lastRequestTime.Remove(entityId);
            }
        }

        private void LogDebug(string message)
        {
            if (_config.LogEntityStreaming)
            {
                MyLog.Default.WriteLine($"[Phyzix] {message}");
            }
        }

        public class PendingEntity
        {
            public long EntityId { get; set; }
            public EntityPriority Priority { get; set; }
            public Vector3D? Position { get; set; }
            public DateTime QueueTime { get; set; }
        }

        public enum EntityPriority
        {
            Low = 0,
            Normal = 1,
            High = 2,
            Critical = 3
        }
    }
}
using System;
using System.Collections.Generic;
using ClientPlugin.Services;
using HarmonyLib;
using VRage.Network;
using VRage.Utils;
using Sandbox.Game.Entities;

namespace ClientPlugin.Patches
{
    [HarmonyPatch(typeof(MyReplicationClient))]
    public static class Patch_MyReplicationClient
    {
        private static EntityStreamingManager StreamingManager => Plugin.Instance?.EntityStreamingManager;
        private static bool IsEnabled => Plugin.Instance?.Config?.Enabled ?? false;
        
        // Track recent requests to prevent spam
        private static readonly Dictionary<long, DateTime> RecentRequests = new Dictionary<long, DateTime>();
        private static readonly TimeSpan RequestCooldown = TimeSpan.FromMilliseconds(100);

        [HarmonyPatch("RequestReplicable")]
        [HarmonyPrefix]
        public static bool RequestReplicable_Prefix(long entityId, byte layer, bool add, long interactedEntity, long medicalRoomId = 0)
        {
            if (!IsEnabled || StreamingManager == null)
                return true; // Continue with original method

            try
            {
                // Handle entity removal requests
                if (!add)
                {
                    // Server is telling us to remove this entity
                    Plugin.Instance?.VoxelPhysicsOptimizer?.RemoveInvalidEntity(entityId);
                    LogDebug($"Server requested removal of entity {entityId}");
                    return true; // Continue with original removal
                }

                // CRITICAL: Never intercept medical spawn requests
                if (medicalRoomId != 0)
                {
                    LogDebug($"Bypassing medical spawn request for entity {entityId}, medical room {medicalRoomId}");
                    return true; // Let it through immediately
                }

                // Check if this is a duplicate request
                if (IsRecentRequest(entityId))
                {
                    LogDebug($"Blocked duplicate request for entity {entityId}");
                    return false; // Skip the original method
                }

                // Check if entity already exists
                if (MyEntities.GetEntityById(entityId) != null)
                {
                    LogDebug($"Entity {entityId} already exists, skipping request");
                    return false;
                }

                // Queue the entity instead of immediate request
                var priority = DeterminePriority(entityId, interactedEntity, medicalRoomId);
                StreamingManager.QueueEntity(entityId, priority);
                
                LogDebug($"Queued entity {entityId} with priority {priority}");
                
                // Skip the original method - we'll handle it through the queue
                return false;
            }
            catch (Exception ex)
            {
                LogError($"Error in RequestReplicable_Prefix: {ex}");
                return true; // On error, continue with original
            }
        }

        private static bool IsRecentRequest(long entityId)
        {
            if (RecentRequests.TryGetValue(entityId, out var lastRequest))
            {
                if (DateTime.UtcNow - lastRequest < RequestCooldown)
                    return true;
            }
            
            RecentRequests[entityId] = DateTime.UtcNow;
            
            // Clean up old entries
            if (RecentRequests.Count > 1000)
            {
                var toRemove = new List<long>();
                var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(10);
                
                foreach (var kvp in RecentRequests)
                {
                    if (kvp.Value < cutoff)
                        toRemove.Add(kvp.Key);
                }
                
                foreach (var key in toRemove)
                    RecentRequests.Remove(key);
            }
            
            return false;
        }

        private static EntityStreamingManager.EntityPriority DeterminePriority(long entityId, long interactedEntity, long medicalRoomId)
        {
            // Medical room spawns are critical
            if (medicalRoomId != 0)
                return EntityStreamingManager.EntityPriority.Critical;
            
            // Direct interactions are high priority
            if (interactedEntity != 0)
                return EntityStreamingManager.EntityPriority.High;
            
            // Everything else is normal
            return EntityStreamingManager.EntityPriority.Normal;
        }

        private static void LogDebug(string message)
        {
            if (Plugin.Instance?.Config?.LogEntityStreaming ?? false)
            {
                MyLog.Default.WriteLine($"[Phyzix.ReplicationClient] {message}");
            }
        }

        private static void LogError(string message)
        {
            MyLog.Default.WriteLine($"[Phyzix.ReplicationClient.ERROR] {message}");
        }
    }
}
using System;
using ClientPlugin.Services;
using HarmonyLib;
using Sandbox.Game.Entities;
using VRage.Game.Voxels;
using VRageMath;

namespace ClientPlugin.Patches
{
    [HarmonyPatch(typeof(MyVoxelBase))]
    public static class Patch_MyVoxelBase
    {
        private static VoxelPhysicsOptimizer VoxelPhysicsOptimizer => Plugin.Instance?.VoxelPhysicsOptimizer;
        private static bool IsEnabled => Plugin.Instance?.Config?.DeferVoxelPhysics ?? false;

        // Patch the InitVoxelMap method to intercept physics creation
        [HarmonyPatch("InitVoxelMap")]
        [HarmonyPrefix]
        public static bool InitVoxelMap_Prefix(MyVoxelBase __instance, MatrixD worldMatrix, Vector3I size, bool useOffset)
        {
            if (!IsEnabled || VoxelPhysicsOptimizer == null)
                return true; // Continue with original method

            try
            {
                // Check if we should defer physics for this voxel
                if (VoxelPhysicsOptimizer.ShouldDeferPhysics(__instance))
                {
                    // Set DelayRigidBodyCreation to prevent immediate physics creation
                    __instance.DelayRigidBodyCreation = true;
                    
                    LogDebug($"Intercepted InitVoxelMap for {__instance.StorageName}, deferring physics");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in InitVoxelMap_Prefix: {ex}");
            }

            return true; // Continue with original method
        }

        // Alternative patch point for voxel map initialization
        [HarmonyPatch(typeof(MyVoxelMap), "Init", typeof(string), typeof(IMyStorage), typeof(MatrixD), typeof(bool))]
        [HarmonyPrefix]
        public static void MyVoxelMap_Init_Prefix(MyVoxelMap __instance, string storageName, IMyStorage storage, MatrixD worldMatrix, bool useVoxelOffset = true)
        {
            if (!IsEnabled || VoxelPhysicsOptimizer == null)
                return;

            try
            {
                // Mark for deferred physics if it's an asteroid
                if (storageName != null && storageName.Contains("asteroid"))
                {
                    __instance.DelayRigidBodyCreation = true;
                    LogDebug($"Marked asteroid {storageName} for deferred physics");
                    
                    // Add to optimization queue for deferred physics handling
                    VoxelPhysicsOptimizer.DeferPhysicsCreation(__instance);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in MyVoxelMap_Init_Prefix: {ex}");
            }
        }

        private static void LogDebug(string message)
        {
            if (Plugin.Instance?.Config?.LogEntityStreaming ?? false)
            {
                VRage.Utils.MyLog.Default.WriteLine($"[Phyzix.VoxelBase] {message}");
            }
        }

        private static void LogError(string message)
        {
            VRage.Utils.MyLog.Default.WriteLine($"[Phyzix.VoxelBase.ERROR] {message}");
        }
    }

    // Note: MyVoxelPhysics is internal in SE, so we can't patch it directly
    // However, planet physics chunks are handled differently anyway and don't need our optimization
}
using System;
using System.Reflection;
using ClientPlugin.Services;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Plugins;
using VRage.Utils;

namespace ClientPlugin
{
    // ReSharper disable once UnusedType.Global
    public class Plugin : IPlugin, IDisposable
    {
        public const string Name = "Phyzix";
        public static Plugin Instance { get; private set; }
        private SettingsGenerator settingsGenerator;

        // Services
        public EntityStreamingManager EntityStreamingManager { get; private set; }
        public VoxelPhysicsOptimizer VoxelPhysicsOptimizer { get; private set; }
        public EntityValidationQueue EntityValidationQueue { get; private set; }
        
        // Configuration
        public Config Config => Config.Current;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Init(object gameInstance)
        {
            Instance = this;
            Instance.settingsGenerator = new SettingsGenerator();

            // Initialize services
            InitializeServices();

            // Apply Harmony patches
            Harmony harmony = new Harmony(Name);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            LogInfo($"{Name} initialized successfully");
        }

        private void InitializeServices()
        {
            // Initialize validation queue first as others depend on it
            EntityValidationQueue = new EntityValidationQueue(Config);
            
            // Initialize other services
            EntityStreamingManager = new EntityStreamingManager(Config);
            VoxelPhysicsOptimizer = new VoxelPhysicsOptimizer(Config, EntityValidationQueue);

            // Hook up validation events
            EntityValidationQueue.OnValidationComplete += OnValidationComplete;
        }

        private void OnValidationComplete(long entityId, ValidationResult result)
        {
            switch (result)
            {
                case ValidationResult.Valid:
                    VoxelPhysicsOptimizer.CreatePhysicsForValidatedEntity(entityId);
                    break;
                case ValidationResult.Invalid:
                    VoxelPhysicsOptimizer.RemoveInvalidEntity(entityId);
                    break;
                case ValidationResult.Timeout:
                    // On timeout, create physics anyway to avoid issues
                    VoxelPhysicsOptimizer.CreatePhysicsForValidatedEntity(entityId);
                    break;
            }
            
            EntityStreamingManager.OnEntityCreated(entityId);
        }

        public void Dispose()
        {
            // Clean up services
            EntityValidationQueue?.Dispose();
            VoxelPhysicsOptimizer?.Clear();
            EntityStreamingManager?.ClearQueue();

            Instance = null;
        }

        public void Update()
        {
            if (!Config.Enabled)
                return;

            // Process entity streaming queue
            EntityStreamingManager?.ProcessQueue();

            // Process voxel physics timeouts
            VoxelPhysicsOptimizer?.ProcessTimeouts();

            // Show debug info if enabled
            if (Config.ShowStreamingDebugInfo)
            {
                ShowDebugInfo();
            }
        }

        // ReSharper disable once UnusedMember.Global
        public void OpenConfigDialog()
        {
            Instance.settingsGenerator.SetLayout<Simple>();
            MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
        }

        public void ClearAllQueues()
        {
            EntityStreamingManager?.ClearQueue();
            VoxelPhysicsOptimizer?.Clear();
            EntityValidationQueue?.Clear();
            LogInfo("All queues cleared");
        }

        private void ShowDebugInfo()
        {
            // TODO: Implement debug overlay
            var queuedEntities = EntityStreamingManager?.QueuedEntities ?? 0;
            var processingEntities = EntityStreamingManager?.ProcessingEntities ?? 0;
            var deferredPhysics = VoxelPhysicsOptimizer?.DeferredPhysicsCount ?? 0;
            var pendingValidations = EntityValidationQueue?.PendingValidations ?? 0;

            if (Config.Debug)
            {
                MyLog.Default.WriteLine($"[{Name}] Queued: {queuedEntities}, Processing: {processingEntities}, " +
                                      $"Deferred Physics: {deferredPhysics}, Validations: {pendingValidations}");
            }
        }

        private void LogInfo(string message)
        {
            MyLog.Default.WriteLine($"[{Name}] {message}");
        }
    }
}
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using Sandbox.Graphics.GUI;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using VRageMath;

namespace ClientPlugin
{
    public class Config : INotifyPropertyChanged
    {
        #region Options

        // General Settings
        private bool enabled = true;
        private bool debug = false;

        // Performance Settings
        private int entityBatchSize = 5;
        private int validationTimeoutMs = 2000;
        private int maxConcurrentValidations = 10;
        private bool deferVoxelPhysics = true;
        private int voxelPhysicsDelayMs = 100;
        private bool enableDistancePriority = true;
        private float priorityDistanceThreshold = 5000f;

        // Debug Settings
        private bool showStreamingDebugInfo = false;
        private bool logEntityStreaming = false;

        #endregion

        #region User interface

        public readonly string Title = "Phyzix - Entity Streaming Optimizer";

        // General Settings
        [Checkbox(description: "Enable the entity streaming optimization")]
        public bool Enabled
        {
            get => enabled;
            set => SetField(ref enabled, value);
        }

        [Checkbox(description: "Enable general debug output")]
        public bool Debug
        {
            get => debug;
            set => SetField(ref debug, value);
        }

        // Performance Settings
        [Slider(1f, 20f, 1f, SliderAttribute.SliderType.Integer, description: "Maximum entities to process per frame")]
        public int EntityBatchSize
        {
            get => entityBatchSize;
            set => SetField(ref entityBatchSize, value);
        }

        [Slider(500f, 5000f, 100f, SliderAttribute.SliderType.Integer, description: "Timeout for entity validation in milliseconds")]
        public int ValidationTimeoutMs
        {
            get => validationTimeoutMs;
            set => SetField(ref validationTimeoutMs, value);
        }

        [Slider(1f, 50f, 1f, SliderAttribute.SliderType.Integer, description: "Maximum concurrent entity validations")]
        public int MaxConcurrentValidations
        {
            get => maxConcurrentValidations;
            set => SetField(ref maxConcurrentValidations, value);
        }

        [Checkbox(description: "Defer physics creation for voxels until validated")]
        public bool DeferVoxelPhysics
        {
            get => deferVoxelPhysics;
            set => SetField(ref deferVoxelPhysics, value);
        }

        [Slider(100f, 2000f, 100f, SliderAttribute.SliderType.Integer, description: "Delay before creating voxel physics (milliseconds)")]
        public int VoxelPhysicsDelayMs
        {
            get => voxelPhysicsDelayMs;
            set => SetField(ref voxelPhysicsDelayMs, value);
        }

        [Checkbox(description: "Prioritize entities by distance to player")]
        public bool EnableDistancePriority
        {
            get => enableDistancePriority;
            set => SetField(ref enableDistancePriority, value);
        }

        [Slider(1000f, 50000f, 1000f, SliderAttribute.SliderType.Float, description: "Distance threshold for priority loading (meters)")]
        public float PriorityDistanceThreshold
        {
            get => priorityDistanceThreshold;
            set => SetField(ref priorityDistanceThreshold, value);
        }

        // Debug Settings
        [Checkbox(description: "Show streaming debug overlay")]
        public bool ShowStreamingDebugInfo
        {
            get => showStreamingDebugInfo;
            set => SetField(ref showStreamingDebugInfo, value);
        }

        [Checkbox(description: "Log entity streaming operations to game log")]
        public bool LogEntityStreaming
        {
            get => logEntityStreaming;
            set => SetField(ref logEntityStreaming, value);
        }

        [Button(description: "Clear all entity queues and reset state")]
        public void ClearQueues()
        {
            Plugin.Instance?.ClearAllQueues();
            MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(
                MyMessageBoxStyleEnum.Info,
                buttonType: MyMessageBoxButtonsType.OK,
                messageText: new StringBuilder("All entity queues have been cleared."),
                messageCaption: new StringBuilder("Queues Cleared"),
                size: new Vector2(0.5f, 0.3f)
            ));
        }

        #endregion

        #region Property change notification boilerplate

        public static readonly Config Default = new Config();
        public static readonly Config Current = ConfigStorage.Load();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
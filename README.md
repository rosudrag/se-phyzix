# Phyzix - Space Engineers Entity Streaming Optimizer

Eliminates freezing and stuttering when loading entities (asteroids, grids, etc.) in Space Engineers.

## ⚠️ Early Release Notice
This plugin is newly released. While it's been tested and works well (on my machine ...), please report any issues you encounter.

## Overview

Phyzix solves a common Space Engineers performance issue where the game freezes when loading multiple entities simultaneously. This is especially noticeable on servers that dynamically spawn asteroids or use heavy spawn systems.

### The Problem
When Space Engineers loads many entities at once (like 50+ asteroids), it processes them all in a single frame, causing:
- Multi-second freezes
- Input lag and stuttering  
- FPS drops to single digits
- Unresponsive controls

### How Phyzix Solves It
Instead of processing all entities in one frame, Phyzix:
- Spreads the load across multiple frames (default: 5 entities per frame)
- Defers physics creation until after validation
- Prioritizes important entities (spawn points, nearby objects)
- Removes invalid entities before they impact performance

Result: Smooth, consistent framerate even during heavy entity streaming.

## Installation

1. Enable via your choice of plugin loader.
2. Configure if needed (defaults work well)


## Configuration

Access through Plugin Loader's config menu:

| Setting | Default | Description |
|---------|---------|-------------|
| Entity Batch Size | 5 | Entities processed per frame. Lower = smoother, higher = faster loading |
| Validation Timeout | 5000ms | How long to wait for server validation |
| Defer Voxel Physics | ON | Delays asteroid physics creation |
| Enable Distance Priority | ON | Loads closer entities first |

Most users won't need to change these settings.

## Compatibility

- **Multiplayer:** Yes (client-side only)
- **Servers:** Works on any server
- **Other Mods:** Generally compatible
- **Game Version:** Latest Space Engineers

## Known Limitations

- Only optimizes entity streaming, not general game performance
- Some heavily modded servers may require config adjustments
- Initial entity discovery still happens normally

## Technical Details

Phyzix uses Harmony to patch:
- `RequestReplicable` - Intercepts and queues entity requests
- `MyVoxelMap.Init` - Defers physics for asteroids
- `MyEntities.CreateFromObjectBuilder` - Manages entity creation

The plugin maintains separate queues for different priority levels and processes them based on distance and importance.

## Bug Reports

Please create issues in the github repo.

---

*This is an early release. Your feedback helps improve the plugin for everyone.*
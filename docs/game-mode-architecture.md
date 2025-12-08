# Game Mode Architecture

> **Draft** - Architecture document for Game Mode system. Subject to review.

## Overview

A **Game Mode** is a self-contained gameplay session backed by a Lua script. Each game mode creates its own ECS World, runs a bootstrap script, and destroys all associated entities when transitioning to another mode.

```
┌─────────────────────────────────────────────────────────────┐
│                     C# / Main Menu                          │
│   GameModeManager.LoadMode("roguelike")                     │
├─────────────────────────────────────────────────────────────┤
│                     Game Mode World                         │
│   Isolated ECS World with all entities for this session     │
├─────────────────────────────────────────────────────────────┤
│                   Game Mode Script (Lua)                    │
│   OnInit → OnLoad → OnUpdate → OnBeforeUnload               │
└─────────────────────────────────────────────────────────────┘
```

**Key principle:** Game modes own worlds. Worlds own entities. Mode switch = world destruction.

---

## World Isolation Strategy

### Decision: Separate ECS World per Game Mode

Each game mode creates and owns its own `World` instance. Mode transitions destroy the previous world entirely.

```
Mode A                          Mode B
┌─────────────────────┐         ┌─────────────────────┐
│ World "roguelike"   │  ──X──► │ World "base_build"  │
│ - All entities      │ Dispose │ - All entities      │
│ - All systems       │         │ - All systems       │
│ - Physics sim       │         │ - Physics sim       │
└─────────────────────┘         └─────────────────────┘
```

### Rationale

| Benefit | Description |
|---------|-------------|
| **Clean isolation** | No entity ID conflicts between modes |
| **Atomic cleanup** | `World.Dispose()` destroys everything in one call |
| **Idiomatic ECS** | Each world has own systems, queries, command buffers |
| **Physics isolation** | Each mode gets own physics simulation (Unity Physics is world-scoped) |
| **Safety** | Cannot accidentally reference entities from previous mode |

### Addressed Concerns

| Concern | Mitigation |
|---------|------------|
| Networking | Unity Netcode creates world per session; mode transitions align with session boundaries |
| Shared assets | Assets (prefabs, materials) are orthogonal to worlds; no issue |
| Creation overhead | Minimal (~1ms), acceptable during loading screen |
| System registration | All systems registered via `LuaGameBootstrap.GetLuaSystems()`; self-activate via `RequireForUpdate` |

### Rejected Alternative

**Single world with entity cleanup** was considered but rejected:
- Would require tracking all "mode entities" manually
- Bulk deletion is fragile and error-prone
- No physics isolation between modes
- Risk of orphaned references

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| World isolation | Separate world per mode | Clean lifecycle, physics isolation |
| Persistent entities | None | Entities are "alive" units; persistence is data tables, not entities |
| Mode parameters | None (v1) | Future key-value store will handle cross-mode data |
| Return data | None | Clean separation between modes |
| Main menu | Not a game mode | Will use GUI scripting (Lua+UXML) |
| Mode stacking | Not supported | Flat replacement only; UI is within mode |
| Transitions | Async with progress | Long operation, supports callbacks |
| Scene integration | Orthogonal | Scenes scriptable from Lua, not tied to mode |
| Lua VM | Shared VM, world-scoped state | Simpler implementation, single VM instance |
| Script location | `StreamingAssets/lua/GameModes/` | Uses existing script loading infrastructure |
| Default world | Null when no mode | No implicit world; explicit mode loading required |
| System activation | Self-activating via `RequireForUpdate` | Systems run when their entity types are present |

---

## Game Mode Lifecycle

```
LoadMode("roguelike")
       │
       ▼
┌──────────────────┐
│ OnBeforeUnload   │  ← Previous mode (if any)
│ (previous mode)  │    Cleanup callbacks, save state
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Destroy Previous │  World.Dispose()
│ World            │  All entities destroyed
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Create New World │  new World("roguelike")
│                  │  Register systems
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ OnInit           │  Schedule asset loading, spawn initial entities
│ (new mode)       │  Progress: 0% → N%
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ Loading          │  Async loading of scheduled assets
│                  │  Progress: N% → 100%
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ OnLoad           │  All scheduled loading complete
│                  │  Mode is now playable
└────────┬─────────┘
         │
         ▼
┌──────────────────┐
│ OnUpdate         │  Called every frame
│ (game loop)      │  Until mode switch
└──────────────────┘
```

---

## Script Callbacks

Game mode scripts are Lua files in `StreamingAssets/lua/GameModes/`:

```lua
-- GameModes/roguelike.lua

---@type OnInit
function OnInit(mode, state)
    -- Called before loading starts
    -- Schedule asset loading, create initial entities
    -- 'mode' is the game mode entity
    -- 'state' is persistent Lua table for this mode

    state.level = 1
    state.score = 0

    -- Spawn world entities
    for i = 1, 10 do
        local enemy = ecs.create_entity({ x = math.random() * 20, y = 0, z = math.random() * 20 })
        ecs.add_script(enemy, "enemy_basic")
    end

    log.info("Roguelike mode initializing...")
end

---@type OnLoad
function OnLoad(mode, state)
    -- Called when all OnInit-scheduled loading is complete
    -- Mode is now fully playable

    log.info("Roguelike mode loaded! Starting level %s", state.level)
end

---@type OnUpdate
function OnUpdate(mode, state, dt)
    -- Called every frame
    -- Main game loop for this mode

    state.elapsed = (state.elapsed or 0) + dt
end

---@type OnBeforeUnload
function OnBeforeUnload(mode, state)
    -- Called before world destruction
    -- Save state, cleanup external resources

    log.info("Roguelike mode unloading. Final score: %s", state.score)
end
```

### Callback Summary

| Callback | When | Purpose |
|----------|------|---------|
| `OnInit(mode, state)` | World created, before loading | Schedule loading, spawn entities |
| `OnLoad(mode, state)` | All loading complete | Mode is playable, finalize setup |
| `OnUpdate(mode, state, dt)` | Every frame | Main game loop |
| `OnBeforeUnload(mode, state)` | Before world destruction | Cleanup, save state |

---

## C# API

### GameModeManager

Primary C# interface for mode management:

```csharp
namespace Pala.LuaGame
{
    /// <summary>
    /// Manages game mode lifecycle: loading, transitions, world ownership.
    /// </summary>
    public class GameModeManager : IDisposable
    {
        /// <summary>Current game mode name, or null if no mode active.</summary>
        public string CurrentMode { get; }

        /// <summary>Current mode's ECS World, or null if no mode active.</summary>
        public World CurrentWorld { get; }

        /// <summary>True if a mode transition is in progress.</summary>
        public bool IsLoading { get; }

        /// <summary>Loading progress 0.0 to 1.0 during transitions.</summary>
        public float LoadingProgress { get; }

        /// <summary>
        /// Load a game mode by script name.
        /// </summary>
        /// <param name="modeName">Script name without path/extension (e.g., "roguelike")</param>
        /// <returns>Async operation that completes when mode is loaded</returns>
        public async Task LoadModeAsync(string modeName, CancellationToken ct = default);

        /// <summary>
        /// Load a game mode with progress callback.
        /// </summary>
        public async Task LoadModeAsync(string modeName, IProgress<float> progress, CancellationToken ct = default);

        /// <summary>
        /// Unload current mode without loading another.
        /// </summary>
        public async Task UnloadCurrentModeAsync(CancellationToken ct = default);

        /// <summary>
        /// Event fired when mode loading starts.
        /// </summary>
        public event Action<string> OnModeLoadStarted;

        /// <summary>
        /// Event fired when mode is fully loaded.
        /// </summary>
        public event Action<string> OnModeLoaded;

        /// <summary>
        /// Event fired before mode unloads.
        /// </summary>
        public event Action<string> OnModeUnloading;
    }
}
```

### Usage Example

```csharp
public class MainMenuController : MonoBehaviour
{
    [SerializeField] private GameModeManager gameModeManager;
    [SerializeField] private Slider loadingBar;

    public async void OnPlayButtonClicked()
    {
        loadingBar.gameObject.SetActive(true);

        var progress = new Progress<float>(p => loadingBar.value = p);
        await gameModeManager.LoadModeAsync("roguelike", progress);

        loadingBar.gameObject.SetActive(false);
    }
}
```

---

## Lua API

### Mode Switching from Lua

```lua
-- Switch to another game mode from within Lua
gamemode.load("base_building")

-- Get current mode name
local current = gamemode.current()  -- "roguelike"

-- Check if loading
local is_loading = gamemode.is_loading()  -- false
```

### Bridge Functions

| Function | Parameters | Returns | Description |
|----------|------------|---------|-------------|
| `gamemode.load(name)` | `string` | `void` | Request mode switch (async) |
| `gamemode.current()` | - | `string?` | Current mode name or nil |
| `gamemode.is_loading()` | - | `boolean` | True during transitions |

---

## Implementation Components

### New Files

```
Runtime/
  LuaGame/
    GameMode/
      GameModeManager.cs        # Main manager, world lifecycle
      GameModeState.cs          # Mode state container
      GameModeWorld.cs          # World setup, system registration
      LuaGameModeBridge.cs      # Lua API for mode switching
    Systems/
      GameModeUpdateSystem.cs   # Drives OnUpdate for mode script
```

### Integration Points

| Component | Integration |
|-----------|-------------|
| `LuaVMManager` | Shared VM; mode state isolated via Lua tables |
| `LuaEntityRegistry` | Clear on world destruction, reinitialize for new world |
| `LuaECSBridge` | Rebind `EntityManager` reference to new world |
| Lua Systems | Self-activate via `RequireForUpdate<T>` when relevant entities exist |

### World Setup Sequence

```csharp
private async Task<World> CreateModeWorld(string modeName)
{
    // 1. Create world
    var world = new World(modeName);

    // 2. Get or create system groups (systems self-register)
    world.GetOrCreateSystemManaged<InitializationSystemGroup>();
    world.GetOrCreateSystemManaged<SimulationSystemGroup>();

    // 3. Add all Lua systems - they self-activate via RequireForUpdate
    DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(
        world,
        LuaGameBootstrap.GetLuaSystems()
    );

    // 4. Rebind bridge to new world
    LuaECSBridge.SetWorld(world);
    LuaEntityRegistry.Clear();

    // 5. Create mode entity with script
    var modeEntity = world.EntityManager.CreateEntity();
    var buffer = world.EntityManager.AddBuffer<LuaScriptRequest>(modeEntity);
    buffer.Add(new LuaScriptRequest
    {
        ScriptName = $"GameModes/{modeName}",
        RequestHash = LuaScriptPathUtility.ComputeHash($"GameModes/{modeName}")
    });

    return world;
}
```

### System Self-Activation Pattern

Systems use `RequireForUpdate` to only run when their entity types exist:

```csharp
[BurstCompile]
public partial struct LuaHealthSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        // System only updates when LuaHealth entities exist
        state.RequireForUpdate<LuaHealth>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Process health...
    }
}
```

This means:
- All Lua systems are registered in every game mode world
- Systems with no matching entities have zero overhead (skip update)
- Game modes implicitly enable systems by creating relevant entities

### Shared VM with World-Scoped State

Single Lua VM instance, but each world's entities have isolated state:

```
┌─────────────────────────────────────────────────────────┐
│                    Lua VM (Single)                      │
├─────────────────────────────────────────────────────────┤
│  Global Tables:                                         │
│    ecs.*, gamemode.*, log.*, etc.                      │
├─────────────────────────────────────────────────────────┤
│  Per-Entity State (via registry refs):                  │
│    Entity 1 → { state table }                          │
│    Entity 2 → { state table }                          │
│    ...                                                  │
└─────────────────────────────────────────────────────────┘
```

On world destruction:
1. All entities in that world are destroyed
2. `LuaScriptCleanupSystem` calls `OnDestroy` for each script
3. Registry refs released → Lua GC collects state tables
4. `LuaEntityRegistry.Clear()` resets ID mappings

The VM itself persists, but all world-specific state is cleaned up through normal entity destruction flow.

---

## Backward Compatibility

The game mode system is **opt-in**. Existing code using direct world access continues to work:

```csharp
// Old way: Direct world usage (still works)
var world = World.DefaultGameObjectInjectionWorld;
var entity = world.EntityManager.CreateEntity();

// New way: Via game mode (optional)
await gameModeManager.LoadModeAsync("fruit_eater");
// Now gameModeManager.CurrentWorld is the active world
```

Tests can run without game modes by creating worlds directly.

---

## Acceptance Criteria

### Core Functionality
- [ ] `GameModeManager.LoadModeAsync()` creates isolated world
- [ ] Previous world destroyed on mode switch
- [ ] All four callbacks fire in correct order: OnInit → OnLoad → OnUpdate → OnBeforeUnload
- [ ] `gamemode.load()` works from Lua
- [ ] Loading progress reported correctly

### Integration
- [ ] `LuaEntityRegistry` cleared between modes
- [ ] `LuaECSBridge` rebinds to new world
- [ ] Systems registered correctly in new world

### Testing
- [ ] PlayMode test: Load fruit_eater as game mode
- [ ] PlayMode test: Switch between two modes
- [ ] PlayMode test: Verify entity isolation between modes
- [ ] PlayMode test: Verify callbacks fire correctly
- [ ] Existing tests pass without game mode system

---

## Future Considerations

### v2: Persistent Key-Value Store
Cross-mode data via typed key-value store with optional persistence:
```lua
-- In roguelike mode
persist.set("blueprints_unlocked", state.blueprints)

-- In base_building mode
local blueprints = persist.get("blueprints_unlocked", {})
```

### v2: Asset Loading Integration
Formalized asset loading with sectioned progress:
```lua
function OnInit(mode, state)
    assets.load("prefabs/enemies", { progress_id = "enemies" })
    assets.load("prefabs/environment", { progress_id = "environment" })
end
```

### v2: Scene Integration
Optional scene loading as part of mode initialization:
```lua
function OnInit(mode, state)
    scene.load("Levels/Forest")
end
```

---

## See Also

- [ARCHITECTURE.md](ARCHITECTURE.md) - Overall LuaGame architecture
- [Entity Cleanup Flow](entity-cleanup-flow.md) - Entity lifecycle
- [fruit_demo_bootstrap.lua](../../../Assets/StreamingAssets/lua/scripts/fruit_demo_bootstrap.lua) - Example bootstrap pattern

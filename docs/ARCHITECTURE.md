# LuaJIT ECS Architecture

> **Pre-Alpha** - This document reflects current implementation. APIs may change.

This document defines the boundary between **Framework** (C#/ECS/Burst) and **Scriptable** (Lua) layers.

## Design Philosophy

```
┌─────────────────────────────────────────────────────────────┐
│                      Lua Scripts                             │
│   "What to do" — decisions, behaviors, state machines        │
├─────────────────────────────────────────────────────────────┤
│                      ECS Bridge                              │
│   API surface — ecs.*, character.*, input.* exposed to Lua   │
├─────────────────────────────────────────────────────────────┤
│                   Framework (C#/Burst)                       │
│   "How to do it" — movement, physics, queries, data          │
└─────────────────────────────────────────────────────────────┘
```

**Guiding principle:** Lua controls *intent*, C# executes *mechanics*.

---

## Package Structure

```
Runtime/
  LuaJIT/
    LuaJIT.cs                 # P/Invoke bindings to lua51 native library
  LuaVM/
    Core/
      LuaVMManager.cs         # Lua state lifecycle, script loading
      LuaStateExtensions.cs   # Helper extensions for lua_State
    Burst/
      BurstLuaContext.cs      # SharedStatic patterns for Burst
  LuaECS/
    Core/
      LuaECSBridge.cs         # Coordinator: registration, shared state
      LuaEntityRegistry.cs    # O(1) entity ID lookups via SharedStatic
      LuaScriptPathUtility.cs # Script file resolution, hash generation
      Bridge/
        LuaEntitiesBridge.cs    # entities.create, destroy, add_script, has_script
        LuaTransformBridge.cs   # transform.get_position, set_position, move_toward
        LuaSpatialBridge.cs     # spatial.distance, query_near, get_entity_count
        LuaEventsBridge.cs      # events.send_attack
        LuaLogBridge.cs         # log.info, debug, warning, error
        LuaCharacterBridge.cs   # character.create, set_move_input, is_grounded
        LuaInputBridge.cs       # input.get_move, get_look, get_jump
        LuaPlayerBridge.cs      # player.get, is_player
        LuaDrawBridge.cs        # draw.line, draw.sphere (debug)
    Components/
      LuaScriptComponent.cs   # LuaScriptRequest and LuaScript buffers
      LuaEvent.cs             # Event dispatch to Lua
      LuaPlayerComponents.cs  # Player tag and related components
    Systems/
      LuaScriptingSystem.cs       # Orchestrator: runtime updates, events, direct ECB
      LuaPlayerBootstrapSystem.cs # Player entity setup
      Support/
        LuaScriptFulfillmentSystem.cs # Request processing, script init, disabling
        LuaEventDispatcher.cs        # Event collection, dispatch
        LuaScriptCleanupSystem.cs    # Script state cleanup, OnDestroy
    Authoring/
      LuaScriptAuthoring.cs       # MonoBehaviour for script assignment
      LuaScriptBufferAuthoring.cs # Buffer-based script authoring
    Demo/
      FruitEaterDemo.cs       # Demo bootstrapper
  LuaGame/
    Components/
      LuaHealth.cs            # Health component (Current, Max, IsDead)
    Systems/
      LuaHealthSystem.cs      # Process damage, trigger OnDeath events
    Bridge/
      LuaHealthBridge.cs      # health.get, health.set, health.damage, health.is_dead
  LuaCharacters/
    Core/
      LuaCharacterComponents.cs # Character control components
      LuaCharacterAspect.cs     # Character aspect for systems
      LuaCharacterSystems.cs    # Character update systems
      Authoring/
        LuaCharacterAuthoring.cs # MonoBehaviour for characters
    ThirdPerson/
      Components/
        ThirdPersonCharacterComponent.cs
        ThirdPersonPlayer.cs
        LuaPlayerBootstrapConfig.cs
      Aspects/
        ThirdPersonCharacterAspect.cs
      Systems/
        ThirdPersonCharacterSystems.cs
        ThirdPersonPlayerSystems.cs
        FixedTickSystem.cs
      Input/
        FixedInputEvent.cs
Editor/
  LuaHotReloadSystem.cs         # FileSystemWatcher for hot reload
  LuaScriptAssetReferenceDrawer.cs # Inspector drawer
Tests/
  LuaECS.Tests/
    LuaScriptLifecycleTest.cs
    LuaEntityCreationTest.cs
    LuaSpatialQueryTest.cs
    LuaEventDispatchTest.cs
    LuaEntityRegistryTest.cs
    LuaStateCleanupTest.cs
  LuaVM.Tests/
    BurstIdAllocatorTests.cs
    BurstIdLookupTests.cs
    BurstOperationQueueTests.cs
docs/
  entity-cleanup-flow.md       # Entity lifecycle and cleanup architecture
  sequence-runtime-flow.md     # Runtime interaction sequence diagram
  flowchart-architecture.md    # Structural relationships flowchart
  sequence-entity-lifecycle.md # Entity creation/destruction flow
```

## Visual Documentation

See the following mermaid diagrams for detailed flow visualization:

- **[entity-cleanup-flow.md](entity-cleanup-flow.md)** — Two-buffer lifecycle architecture
- **[sequence-runtime-flow.md](sequence-runtime-flow.md)** — Per-frame runtime interactions between components
- **[flowchart-architecture.md](flowchart-architecture.md)** — Structural relationships and decision points
- **[sequence-entity-lifecycle.md](sequence-entity-lifecycle.md)** — Entity creation/destruction lifecycle

---

## Script Lifecycle Architecture

The Lua script system uses a **two-buffer architecture**:

### Components

| Component          | Type                        | Purpose                                    |
| ------------------ | --------------------------- | ------------------------------------------ |
| `LuaScriptRequest` | `IBufferElementData`        | Pending script requests with unique hashes |
| `LuaScript`        | `ICleanupBufferElementData` | Initialized scripts with VM state          |

### Key Fields

```csharp
public struct LuaScriptRequest : IBufferElementData
{
    public FixedString64Bytes ScriptName;
    public Hash128 RequestHash;  // xxHash3 of script name for deduplication
    public bool Fulfilled;       // True once script is created
}

public struct LuaScript : ICleanupBufferElementData
{
    public FixedString64Bytes ScriptName;
    public int StateRef;         // VM registry reference (-1 if disabled)
    public int EntityIndex;      // Lua entity ID
    public Hash128 RequestHash;  // Links back to request
    public bool Disabled;        // True = skip execution, cleanup at entity end
}
```

### Lifecycle Systems

All lifecycle systems run in `InitializationSystemGroup`:

| System                       | Purpose                                                                       |
| ---------------------------- | ----------------------------------------------------------------------------- |
| `LuaScriptFulfillmentSystem` | Processes requests, creates VM state, calls OnInit, supports script disabling |
| `LuaScriptCleanupSystem`     | Handles entity destruction, calls OnDestroy, releases VM state                |

---

## Framework Scope (C#/ECS)

The framework provides primitives that Lua cannot efficiently implement.

### Core Systems (Implemented)

| System                                 | Purpose                                    | Thread | Burst |
| -------------------------------------- | ------------------------------------------ | ------ | ----- |
| `LuaScriptingSystem`                   | Runtime updates, event dispatch, ECB       | Main   | No    |
| `LuaScriptFulfillmentSystem`           | Script initialization, disabling           | Main   | No    |
| `LuaScriptCleanupSystem`               | Destruction, OnDestroy, cleanup            | Main   | No    |
| `LuaPlayerBootstrapSystem`             | Player entity setup                        | Main   | No    |
| `EndSimulationEntityCommandBufferSystem` | Unity built-in structural change playback | Main   | N/A   |

### Bridge API (Implemented)

The bridge uses a **domain-oriented API** with direct ECB access. Modules are under `Runtime/LuaECS/Core/Bridge/`:

| Module               | Lua Namespace | Functions                                                             | Notes                                 |
| -------------------- | ------------- | --------------------------------------------------------------------- | ------------------------------------- |
| `LuaEntitiesBridge`  | `entities`    | `create`, `destroy`, `add_script`, `has_script`                       | Entity lifecycle via direct ECB       |
| `LuaTransformBridge` | `transform`   | `get_position`, `set_position`, `get_rotation`, `move_toward`         | Transform read/write + movement       |
| `LuaSpatialBridge`   | `spatial`     | `distance`, `query_near`, `get_entity_count`                          | Distance checks, area queries         |
| `LuaEventsBridge`    | `events`      | `send_attack`                                                         | Cross-entity event dispatch           |
| `LuaLogBridge`       | `log`         | `info`, `debug`, `warning`, `error`                                   | Via Unity.Logging                     |
| `LuaCharacterBridge` | `character`   | `create`, `set_move_input`, `set_jump`, `is_grounded`, `get_velocity` | Character controller                  |
| `LuaInputBridge`     | `input`       | `get_move`, `get_look`, `get_jump`                                    | Unity Input System                    |
| `LuaPlayerBridge`    | `player`      | `get`, `is_player`                                                    | Player entity queries                 |
| `LuaDrawBridge`      | `draw`        | `line`, `sphere`                                                      | Debug visualization                   |

Legacy `ecs.*` namespace is preserved for backward compatibility.

### Support Systems

The scripting system delegates to specialized systems under `Runtime/LuaECS/Systems/Support/`:

| System                       | Responsibility                                     |
| ---------------------------- | -------------------------------------------------- |
| `LuaScriptFulfillmentSystem` | Request processing, script init, disabling         |
| `LuaEventDispatcher`         | Event collection, clearing, and dispatch           |
| `LuaScriptCleanupSystem`     | OnDestroy callbacks, state release, entity cleanup |

Entity ID management is handled by `LuaEntityRegistry` via SharedStatic for Burst compatibility.

---

## LuaGame Assembly

Gameplay-focused assembly providing entity lifecycle hooks and health/damage systems.

### Components

| Component   | Purpose                             |
| ----------- | ----------------------------------- |
| `LuaHealth` | Health state (Current, Max, IsDead) |

### Systems

| System            | Purpose                                |
| ----------------- | -------------------------------------- |
| `LuaHealthSystem` | Process damage, trigger OnDeath events |

### Bridge API

| Module            | Lua Namespace | Functions                                 | Notes             |
| ----------------- | ------------- | ----------------------------------------- | ----------------- |
| `LuaHealthBridge` | `health`      | `get`, `set`, `damage`, `heal`, `is_dead` | Health management |

---

## LuaCharacters Assembly

Separate assembly providing character controller integration with Unity Character Controller package.

### Components

| Component                       | Purpose                               |
| ------------------------------- | ------------------------------------- |
| `LuaCharacterComponent`         | Character movement parameters         |
| `LuaCharacterControl`           | Runtime input state (moveInput, jump) |
| `ThirdPersonCharacterComponent` | Third-person specific settings        |
| `ThirdPersonPlayer`             | Player-controlled character marker    |
| `LuaPlayerBootstrapConfig`      | Player prefab spawning configuration  |

### Systems

| System                                    | Purpose                             |
| ----------------------------------------- | ----------------------------------- |
| `LuaCharacterVariableUpdateSystem`        | Sync character variables each frame |
| `LuaCharacterPhysicsUpdateSystem`         | Character physics integration       |
| `ThirdPersonCharacterPhysicsUpdateSystem` | Third-person physics updates        |
| `ThirdPersonPlayerVariableUpdateSystem`   | Player input to character control   |
| `FixedTickSystem`                         | Fixed timestep event broadcasting   |

---

## Framework Scope (Planned)

Future framework additions that will be exposed to Lua.

### Combat (Planned)

| Feature   | Bridge API                              | Framework Responsibility   |
| --------- | --------------------------------------- | -------------------------- |
| Combat    | `attack`, `get_attack_range`            | Hit detection, cooldowns   |
| Animation | `play_animation`, `get_animation_state` | Animator integration       |
| Stats     | `get_stat`, `modify_stat`               | Stat system with modifiers |

### Inventory

| Feature   | Bridge API                                 | Framework Responsibility           |
| --------- | ------------------------------------------ | ---------------------------------- |
| Items     | `get_inventory`, `add_item`, `remove_item` | Inventory buffer, stack management |
| Equipment | `equip`, `unequip`, `get_equipped`         | Equipment slots, stat bonuses      |
| Transfer  | `transfer_item`, `deposit_all`             | Container interactions             |

### World & Navigation

| Feature     | Bridge API                       | Framework Responsibility |
| ----------- | -------------------------------- | ------------------------ |
| Pathfinding | `find_path`, `move_along_path`   | NavMesh integration      |
| Zones       | `get_current_zone`, `is_in_zone` | Zone detection           |
| Spawning    | `spawn_entity`, `spawn_prefab`   | Entity instantiation     |

---

## Scriptable Scope (Lua)

What Lua scripts control — the "brain" of game entities.

### Entity Behaviors

Scripts define global PascalCase functions with full IntelliSense:

```lua
-- Lua decides: "I should move toward the nearest fruit"
-- Framework executes: pathfinding, collision, transform updates

function OnUpdate(entity, state, dt)
    if not state.target then
        state.target = FindNearestFruit(entity)
    end
    if state.target then
        ecs.move_toward(entity, state.target, state.speed)
    end
end
```

### State Machines

Lua owns entity state and transitions:

```lua
state.mode = "idle"      -- Lua decides current mode
state.target = nil       -- Lua tracks targets
state.cooldown = 0       -- Lua manages timers

-- State transitions are script logic
if state.mode == "gathering" and inventory_full() then
    state.mode = "returning"
end
```

### Event Handling

Scripts respond to framework events via PascalCase callbacks:

```lua
function OnAttacked(entity, state, event)
    state.threat = event.source
    state.mode = "fleeing"
end

function OnCommand(entity, state, command)
    if command == "stop" then
        state.mode = "idle"
    end
end
```

---

## Boundary Rules

### Lua SHOULD control:
- High-level decisions (what to do next)
- State machine transitions
- Target selection and prioritization
- Behavior parameters (speeds, ranges, cooldowns)
- Game data definitions

### Lua SHOULD NOT control:
- Physics calculations
- Spatial queries (use `query_entities_near`)
- Transform math (use `move_toward`, `distance`)
- Rendering or animation triggers
- Networking or persistence

### Framework MUST provide:
- Efficient spatial queries
- Burst-compiled movement
- Thread-safe command execution
- Data validation
- Event dispatch

---

## Extension Pattern

When adding new framework features:

1. **Define the component** (C# `IComponentData`)
2. **Add bridge function** (in appropriate `Bridge/*Bridge.cs` file)
3. **Update type definitions** (`types/ecs.lua`)
4. **Document in this file**

Example for adding health (would go in a new `LuaHealthBridge.cs`):

```csharp
// 1. Component
public struct Health : IComponentData
{
    public float Current;
    public float Max;
}

// 2. Bridge function (in LuaHealthBridge.cs)
[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
static int ECS_GetHealth(lua_State L)
{
    var entity = GetEntityFromId((int)Lua.lua_tointeger(L, 1));
    if (!s_EntityManager.HasComponent<Health>(entity))
    {
        Lua.lua_pushnil(L);
        return 1;
    }
    var health = s_EntityManager.GetComponentData<Health>(entity);
    Lua.lua_pushnumber(L, health.Current);
    Lua.lua_pushnumber(L, health.Max);
    return 2;
}
```

```lua
-- 3. Type definition (types/ecs.lua)
---@field get_health fun(entity: EntityIndex): number, number Get current and max health
```

---

## Architectural Decisions

### Static Bridge for Burst Compatibility

The `LuaECSBridge` uses static fields and `[MonoPInvokeCallback]` methods because:
- Lua C function pointers cannot reference instance methods
- Static state is required for `MonoPInvokeCallback` attribute
- This enables integration with Burst-compiled systems

### Hybrid Pattern (Static + DI)

- **Static**: Bridge functions (Burst-compatible, required by Lua FFI)
- **Instance/Singleton**: `LuaVMManager` (for testability)
- **ECS-managed**: Systems follow standard ECS lifecycle

### Deferred Entity Operations

All structural changes use `EntityCommandBuffer` pattern:
- Entity creation returns ID immediately
- Actual entity created at frame end
- `m_DeferredEntities` map tracks in-flight entities

### Two-Buffer Lifecycle Architecture

Script initialization uses separate buffers for requests and fulfilled scripts:
- `LuaScriptRequest` — pending requests, can be added any time
- `LuaScript` — initialized scripts, cleanup buffer ensures proper teardown
- `RequestHash` (xxHash3 of script name) enables deduplication
- `Disabled` flag allows runtime script removal without entity destruction

---

## Current Implementation Status

| Feature           | Framework | Bridge | Types | Notes                      |
| ----------------- | --------- | ------ | ----- | -------------------------- |
| Transform         | ✅         | ✅      | ✅     | Full read/write support    |
| Movement          | ✅         | ✅      | ✅     | Burst-compiled commands    |
| Spatial Queries   | ✅         | ✅      | ✅     | Distance, area queries     |
| Commands          | ✅         | ✅      | ✅     | Deferred execution         |
| Events            | ✅         | ✅      | ✅     | Full dispatch system       |
| Logging           | ✅         | ✅      | ✅     | Unity.Logging integration  |
| Health            | ✅         | ✅      | ⚠️     | LuaGame assembly           |
| OnDestroy         | ✅         | N/A    | N/A   | Lifecycle callback         |
| Script Disabling  | ✅         | N/A    | N/A   | Runtime script removal     |
| Character Control | ✅         | ✅      | ⚠️     | Unity Character Controller |
| Input             | ✅         | ✅      | ⚠️     | Unity Input System         |
| Player            | ✅         | ✅      | ⚠️     | Basic player queries       |
| Debug Draw        | ✅         | ✅      | ⚠️     | Line, sphere primitives    |
| Inventory         | ❌         | ❌      | ❌     | Planned                    |
| Pathfinding       | ❌         | ❌      | ❌     | Planned                    |

Legend: ✅ Implemented | ⚠️ Partial/No Types | ❌ Not started

---

## See Also

- [Entity Cleanup Flow](entity-cleanup-flow.md) - Two-buffer lifecycle architecture
- [Multithreading Architecture](multithreading.md) - Future parallel script execution design (not implemented)
- [Runtime Flow](sequence-runtime-flow.md) - Frame execution sequence
- [Entity Lifecycle](sequence-entity-lifecycle.md) - Entity creation/destruction flow

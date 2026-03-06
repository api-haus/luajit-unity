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
│   API surface — ecs.*, input.* exposed to Lua                │
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
        LuaInputBridge.cs       # input.read_value, was_pressed, is_held, was_released
        LuaDrawBridge.cs        # draw.line, draw.sphere (debug)
    Components/
      LuaScriptComponent.cs   # LuaScriptRequest and LuaScript buffers
      LuaEvent.cs             # Event dispatch to Lua
      LuaPlayerComponents.cs  # Player tag and related components
    Systems/
      LuaScriptingSystem.cs       # Orchestrator: runtime updates, events, direct ECB
      Support/
        LuaScriptFulfillmentSystem.cs # Request processing, script init, disabling
        LuaEventDispatcher.cs        # Event collection, dispatch
        LuaScriptCleanupSystem.cs    # Script state cleanup, OnDestroy
    Authoring/
      LuaScriptAuthoring.cs       # MonoBehaviour for script assignment
      LuaScriptBufferAuthoring.cs # Buffer-based script authoring
    Demo/
      FruitEaterDemo.cs       # Demo bootstrapper
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
| `LuaScriptCleanupSystem`              | Destruction, OnDestroy, cleanup            | Main   | No    |
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
| `LuaInputBridge`     | `input`       | `read_value`, `was_pressed`, `is_held`, `was_released`                | Unity Input System                    |
| `LuaDrawBridge`      | `draw`        | `line`, `sphere`                                                      | Debug visualization                   |

### Support Systems

The scripting system delegates to specialized systems under `Runtime/LuaECS/Systems/Support/`:

| System                       | Responsibility                                     |
| ---------------------------- | -------------------------------------------------- |
| `LuaScriptFulfillmentSystem` | Request processing, script init, disabling         |
| `LuaEventDispatcher`         | Event collection, clearing, and dispatch           |
| `LuaScriptCleanupSystem`     | OnDestroy callbacks, state release, entity cleanup |

Entity ID management is handled by `LuaEntityRegistry` via SharedStatic for Burst compatibility.

---

## `[LuaBridge]` Codegen Pattern

Components annotated with `[LuaBridge("name")]` automatically get Roslyn source-generated Lua getter/setter bridges. This is the preferred way to expose ECS data to Lua — define a component, annotate it, and the codegen handles registration.

Character controllers, stats, and gameplay systems should be implemented at the **project level** using this pattern rather than in the package.

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

When adding new framework features, prefer the `[LuaBridge]` codegen pattern:

1. **Define the component** (C# `IComponentData`) with `[LuaBridge("name")]` attribute
2. **Codegen auto-generates** getter/setter bridge functions
3. **Update type definitions** (`types/ecs.lua`)

For custom bridge functions that don't fit the codegen pattern:

1. **Add bridge function** (in appropriate `Bridge/*Bridge.cs` file)
2. **Register in `LuaECSBridge.RegisterFunctions`**
3. **Update type definitions** (`types/ecs.lua`)
4. **Document in this file**

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
| Input             | ✅         | ✅      | ⚠️     | Unity Input System         |
| Debug Draw        | ✅         | ✅      | ⚠️     | Line, sphere primitives    |
| OnDestroy         | ✅         | N/A    | N/A   | Lifecycle callback         |
| Script Disabling  | ✅         | N/A    | N/A   | Runtime script removal     |

Legend: ✅ Implemented | ⚠️ Partial/No Types | ❌ Not started

---

## See Also

- [Entity Cleanup Flow](entity-cleanup-flow.md) - Two-buffer lifecycle architecture
- [Multithreading Architecture](multithreading.md) - Future parallel script execution design (not implemented)
- [Runtime Flow](sequence-runtime-flow.md) - Frame execution sequence
- [Entity Lifecycle](sequence-entity-lifecycle.md) - Entity creation/destruction flow

# Lua Scripting Multithreading Architecture

> **Future Work** - This document describes a planned feature that is NOT YET IMPLEMENTED.
> Currently, all Lua scripts run sequentially on the main thread.

---

## Overview

The Lua scripting system will support opt-in parallel execution of scripts. By default, all scripts run sequentially on the main thread to ensure deterministic behavior. Scripts can be annotated to run in parallel when they meet thread-safety requirements.

## Threading Modes

| Mode         | Annotation             | Execution                        | Use Case                                 |
| ------------ | ---------------------- | -------------------------------- | ---------------------------------------- |
| `Sequential` | (default)              | Main thread, deterministic order | Game managers, scripts with shared state |
| `Parallel`   | `@threading: parallel` | Worker threads, any order        | Independent entity behaviors             |

## Script Annotation

Add a threading annotation in the first 10 lines of your Lua script:

```lua
-- @threading: parallel

function OnInit(entity, state)
    state.speed = 5
end

function OnUpdate(entity, state, dt)
    local pos = ecs.get_position(entity)
    ecs.move_toward(entity, {x = 0, y = 0, z = 0}, state.speed * dt)
end
```

Scripts without annotation default to `Sequential`.

## Execution Flow

```
Frame Update
│
├─► Phase 1: Sequential Scripts (Main Thread)
│   ┌─────────────────────────────────────────┐
│   │  Script A → Script B → Script C → ...   │
│   │  (deterministic order by entity)        │
│   └─────────────────────────────────────────┘
│
├─► Sync Point
│
├─► Phase 2: Parallel Scripts (Worker Threads)
│   ┌─────────────────────────────────────────┐
│   │  ┌────┐ ┌────┐ ┌────┐ ┌────┐           │
│   │  │ P1 │ │ P2 │ │ P3 │ │ P4 │  ...      │
│   │  └────┘ └────┘ └────┘ └────┘           │
│   │    W0    W1     W0     W1   (workers)  │
│   └─────────────────────────────────────────┘
│
├─► Sync Point
│
└─► Phase 3: Process Deferred Commands
    ┌─────────────────────────────────────────┐
    │  Merge command queues from all workers  │
    │  Apply via EntityCommandBuffer          │
    └─────────────────────────────────────────┘
```

## Thread-Safety Requirements

### Scripts marked `@parallel` MUST:

1. **Only access own entity** - Use `entity` parameter, not hardcoded IDs
2. **Use state table** - Store per-entity data in `state`, not global variables
3. **Use deferred commands** - All mutations go through command queues
4. **Be order-independent** - Don't rely on execution order relative to other scripts

### Scripts marked `@parallel` MUST NOT:

1. Access global Lua variables for mutable state
2. Assume execution order with other parallel scripts
3. Read other entities' state that may be modified this frame
4. Use `require()` at runtime (load during init only)

## Bridge Function Thread-Safety

### Burst-Compiled (Thread-Safe)

| Function         | Status | Notes                          |
| ---------------- | ------ | ------------------------------ |
| `get_position`   | ✅ Safe | Read-only via ComponentLookup  |
| `set_position`   | ✅ Safe | Write via ComponentLookup      |
| `get_rotation`   | ✅ Safe | Read-only via ComponentLookup  |
| `distance`       | ✅ Safe | Pure computation               |
| `move_toward`    | ✅ Safe | Deferred via command queue     |
| `destroy_entity` | ✅ Safe | Deferred via destruction queue |

### Managed (Main Thread Only)

| Function              | Status       | Notes                                |
| --------------------- | ------------ | ------------------------------------ |
| `create_entity`       | ⚠️ Sequential | Requires atomic ID + ECB access      |
| `add_script`          | ⚠️ Sequential | String handling, ECB access          |
| `has_script`          | ⚠️ Sequential | Buffer iteration with string compare |
| `query_entities_near` | ⚠️ Sequential | EntityQuery creation                 |
| `get_entity_count`    | ⚠️ Sequential | EntityQuery creation                 |
| `log_*`               | ⚠️ Sequential | String handling                      |

### Future Work: Parallel-Safe Variants

With additional infrastructure, these could become parallel-safe:

| Function        | Requirement                                         |
| --------------- | --------------------------------------------------- |
| `create_entity` | Atomic ID generation + per-worker creation queues   |
| `add_script`    | FixedString script names + per-worker script queues |

## Implementation Components

### 1. Threading Mode Enum

```csharp
public enum LuaThreadingMode : byte
{
    Sequential = 0,  // Default
    Parallel = 1,
}
```

### 2. Extended Script Component

```csharp
public struct LuaScript : ICleanupBufferElementData
{
    public FixedString64Bytes ScriptName;
    public int StateRef;
    public int EntityIndex;
    public Hash128 RequestHash;
    public bool Disabled;
    public LuaThreadingMode ThreadingMode;  // Parsed from annotation (future)
}
```

> **Note**: The current implementation uses `ICleanupBufferElementData` for proper entity lifecycle management. The `ThreadingMode` field would be added when parallel execution is implemented.

### 3. Worker Pool (Future)

```csharp
public class LuaWorkerPool : IDisposable
{
    struct Worker
    {
        public lua_State State;
        public Dictionary<string, int> ScriptRefs;
        public EntityCommandBuffer.ParallelWriter ECB;
        public NativeList<int> Destructions;
    }

    Worker[] m_Workers;

    public void ExecuteParallel(NativeList<ScriptUpdateRequest> requests, float deltaTime);
}
```

### 4. Per-Worker Bridge Context

```csharp
public unsafe struct WorkerBridgeContext
{
    public int WorkerId;

    // Per-worker ECB writer
    public EntityCommandBuffer.ParallelWriter ECB;
    public UnsafeList<int>* Destructions;

    // Shared read-only data
    [ReadOnly] public UnsafeHashMap<int, Entity> EntityIdMap;
    [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
}
```

## Roadmap

### Phase 1: Annotation Parsing (No Runtime Change)
- Parse `@threading` annotation during script load
- Store in `LuaScript.ThreadingMode`
- Log warning if parallel script uses sequential-only functions

### Phase 2: Script Partitioning
- Separate sequential and parallel scripts during collection
- Execute in two phases with single sync point
- Still single-threaded, but prepares for Phase 3

### Phase 3: Worker Pool
- Create multiple `lua_State` instances
- Distribute parallel scripts across workers
- Merge command queues after completion

### Phase 4: Optimizations
- Script-type partitioning (same script → same worker)
- Adaptive worker count based on script load
- Per-worker ComponentLookup to reduce contention

## Performance Considerations

### When Parallel Helps

- Many independent entity scripts (100+)
- Scripts with significant computation
- Read-heavy scripts (position checks, distance calculations)

### When Parallel Hurts

- Few scripts (overhead > benefit)
- Heavy cross-entity communication
- Scripts that frequently call sequential-only functions

### Sync Point Overhead

Each sync point costs ~1-5μs. With the two-phase model:
- 1 sync between sequential → parallel
- 1 sync after parallel completion
- Total: ~2-10μs fixed overhead

This is negligible for frames with 100+ scripts but noticeable with <10 scripts.

## Example Scripts

### Sequential (Game Manager)

```lua
-- Game manager - controls global state
-- No annotation = sequential by default

local wave_data = require("data.waves")

function OnInit(entity, state)
    state.current_wave = 1
    state.wave_timer = 5
    state.enemies_alive = 0
end

function OnUpdate(entity, state, dt)
    if state.enemies_alive <= 0 then
        state.wave_timer = state.wave_timer - dt
        if state.wave_timer <= 0 then
            spawn_wave(state.current_wave)
            state.current_wave = state.current_wave + 1
            state.wave_timer = 10
        end
    end
end
```

### Parallel (Enemy AI)

```lua
-- @threading: parallel
-- Independent entity behavior - safe for parallel

function OnInit(entity, state)
    state.speed = 3 + math.random() * 2
    state.target = nil
end

function OnUpdate(entity, state, dt)
    if state.target then
        local dist = ecs.distance(entity, state.target)
        if dist < 1 then
            state.target = nil
        else
            ecs.move_toward(entity, state.target, state.speed * dt)
        end
    end
end

function OnTargetAcquired(entity, state, event)
    state.target = event.target
end
```

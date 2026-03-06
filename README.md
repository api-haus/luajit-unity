# LuaGame

> **Pre-Alpha** - This package is under active development. APIs may change without notice.

Lua scripting framework for Unity with ECS integration. Includes LuaJIT bindings, Burst-compatible VM management, and Unity ECS bridge.

## Features

- **LuaJIT Bindings** - Complete P/Invoke bindings to lua51 native library
- **Script Lifecycle** - Automatic `OnInit`, `OnTick`, `OnDestroy`, `OnCommand`, event callbacks
- **ECS Bridge** - Transform, spatial queries, entity creation/destruction from Lua
- **`[LuaBridge]` Codegen** - Roslyn source generator for automatic component-to-Lua bridges
- **Input Bridge** - Access Unity Input System from Lua
- **Hot Reload** - Editor-only file watcher for instant script updates
- **Cross-Platform** - Native binaries for Windows, macOS, Linux, iOS, Android

## Package Structure

This package is organized into three assemblies with clear dependency boundaries:

```
im.pala.luagame/
├── Runtime/
│   ├── LuaJIT/              # Assembly 1: Pure C bindings
│   │   └── LuaJIT.cs        # P/Invoke bindings to lua51 native library
│   ├── LuaVM/               # Assembly 2: Burst-compatible VM management
│   │   ├── Core/            # Script lifecycle, sandboxing
│   │   └── Burst/           # SharedStatic patterns, NativeCollections
│   └── LuaECS/              # Assembly 3: Unity ECS bridge
│       ├── Core/            # Bridge functions, entity collection
│       ├── Core/Bridge/     # Domain-specific bridge modules
│       ├── Components/      # ECS components
│       └── Systems/         # ECS systems
├── Editor/                  # Editor tools (hot reload, inspectors)
├── runtimes/                # Native binaries for all platforms
└── docs/                    # Architecture documentation
```

## Assembly Dependencies

```
LuaJIT (no Unity deps, pure C# P/Invoke)
    ↓
LuaVM (Unity.Collections, Unity.Burst, Unity.Mathematics, Unity.Logging)
    ↓
LuaECS (Unity.Entities, Unity.Transforms, Unity.Physics, ...)
```

Character controllers, gameplay systems, and stats belong at the **project level** using the `[LuaBridge]` codegen pattern.

## Usage

### LuaJIT Only (Raw Bindings)

```csharp
using LuaNET.LuaJIT;

var L = Lua.luaL_newstate();
Lua.luaL_openlibs(L);
Lua.luaL_dostring(L, "print('Hello from Lua!')");
Lua.lua_close(L);
```

### LuaVM (Managed VM)

```csharp
using LuaVM.Core;

var vm = new LuaVMManager();
vm.LoadScript("my_script");
vm.CallInit("my_script", entityId, stateRef);
vm.CallTick("my_script", entityId, stateRef, deltaTime);
```

### LuaECS (Full ECS Integration)

Scripts define callbacks that ECS systems invoke:

```lua
-- scripts/my_behavior.lua
function OnInit(entity, state)
    state.speed = 5
end

function OnTick(entity, state, dt)
    local pos = ecs.get_position(entity)
    local target = ecs.query_entities_near(entity, 10)[1]
    if target then
        ecs.move_toward(entity, target, state.speed)
    end
end

function OnDestroy(entity, state)
    log.info("Entity destroyed, cleaning up...")
end
```

### [LuaBridge] Codegen

Annotate ECS components to auto-generate Lua bridges:

```csharp
[LuaBridge("char_stats")]
public struct CharacterStats : IComponentData
{
    public float maxSpeed;
    public float jumpForce;
}
```

This generates `char_stats.get(entity)` and `char_stats.set(entity, ...)` Lua functions automatically.

## Lua API Reference

### entities namespace

| Function                               | Description                        |
| -------------------------------------- | ---------------------------------- |
| `entities.create(pos)`                 | Create entity, returns ID          |
| `entities.destroy(entity)`             | Destroy entity                     |
| `entities.add_script(entity, name)`    | Add script to entity               |
| `entities.has_script(entity, name)`    | Check if entity has script         |

### transform namespace

| Function                                    | Description                        |
| ------------------------------------------- | ---------------------------------- |
| `transform.get_position(entity)`            | Returns `{x, y, z}` table         |
| `transform.set_position(entity, pos)`       | Set entity position                |
| `transform.get_rotation(entity)`            | Returns euler angles `{x, y, z}`  |
| `transform.move_toward(entity, target, spd)`| Queue movement command             |

### spatial namespace

| Function                                    | Description                        |
| ------------------------------------------- | ---------------------------------- |
| `spatial.distance(entity1, entity2)`        | Distance between entities          |
| `spatial.query_near(entity, radius)`        | Returns array of nearby entity IDs |
| `spatial.get_entity_count()`                | Total entity count                 |

### input namespace

| Function                       | Description                          |
| ------------------------------ | ------------------------------------ |
| `input.read_value(action)`     | Read action value (Vector2/float)    |
| `input.was_pressed(action)`    | True if pressed this frame           |
| `input.is_held(action)`        | True if currently held               |
| `input.was_released(action)`   | True if released this frame          |

### log namespace

| Function           | Description       |
| ------------------ | ----------------- |
| `log.info(msg)`    | Log info message  |
| `log.debug(msg)`   | Log debug message |
| `log.warning(msg)` | Log warning       |
| `log.error(msg)`   | Log error         |

## Script Lifecycle

Scripts receive the following callbacks:

| Callback                           | Description                           |
| ---------------------------------- | ------------------------------------- |
| `OnInit(entity, state)`            | Called once when script is loaded     |
| `OnTick(entity, state, dt)`        | Called at configured tick rate        |
| `OnDestroy(entity, state)`         | Called before entity is destroyed     |
| `OnCommand(entity, state, cmd)`    | Called when command is sent to entity |
| `On<EventName>(entity, state, ev)` | Called for custom events              |

### Tick Groups

Control when `OnTick` is called using the `@tick:` annotation:

```lua
-- @tick: fixed
function OnTick(entity, state, dt)
    -- Called at fixed timestep (physics rate)
end
```

| Tick Group       | Description                              |
| ---------------- | ---------------------------------------- |
| `variable`       | Default. Every frame in SimulationGroup  |
| `fixed`          | Fixed timestep (FixedStepSimulationGroup)|
| `before_physics` | Before physics simulation                |
| `after_physics`  | After physics simulation                 |
| `after_transform`| After transform updates                  |

## Building Native Libraries

The `.github/workflows/build.yml` workflow builds LuaJIT for all supported platforms.

## Known Limitations

- **Single-threaded execution** - All Lua scripts run sequentially on the main thread
- **No pathfinding** - Navigation must be implemented in game-specific code
- **No animation bridge** - Animator integration planned for future release

## Requirements

- Unity 2022.3+
- Unity Entities 1.0.0+
- Unity Collections 2.0.0+
- Unity Burst 1.8.0+
- Unity Logging 1.3.0+
- Unity Input System 1.0.0+ (for input bridge)

## Documentation

See the `docs/` folder for detailed architecture documentation:

- [ARCHITECTURE.md](docs/ARCHITECTURE.md) - System design and bridge API reference

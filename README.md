# LuaGame

> **Pre-Alpha** - This package is under active development. APIs may change without notice.

Lua scripting framework for Unity with ECS integration. Includes LuaJIT bindings, Burst-compatible VM management, and Unity ECS bridge.

## Features

- **LuaJIT Bindings** - Complete P/Invoke bindings to lua51 native library
- **Script Lifecycle** - Automatic `OnInit`, `OnUpdate`, `OnDestroy`, `OnCommand`, event callbacks
- **ECS Bridge** - Transform, spatial queries, entity creation/destruction from Lua
- **Health System** - Health component with damage, death events, and Lua bridge
- **Character Controller** - Integration with Unity Character Controller package
- **Input Bridge** - Access Unity Input System from Lua
- **Hot Reload** - Editor-only file watcher for instant script updates
- **Cross-Platform** - Native binaries for Windows, macOS, Linux, iOS, Android

## Package Structure

This package is organized into five assemblies with clear dependency boundaries:

```
im.pala.luagame/
├── Runtime/
│   ├── LuaJIT/              # Assembly 1: Pure C bindings
│   │   └── LuaJIT.cs        # P/Invoke bindings to lua51 native library
│   ├── LuaVM/               # Assembly 2: Burst-compatible VM management
│   │   ├── Core/            # Script lifecycle, sandboxing
│   │   └── Burst/           # SharedStatic patterns, NativeCollections
│   ├── LuaECS/              # Assembly 3: Unity ECS bridge
│   │   ├── Core/            # Bridge functions, entity collection
│   │   ├── Core/Bridge/     # Domain-specific bridge modules
│   │   ├── Components/      # ECS components
│   │   └── Systems/         # ECS systems
│   ├── LuaGame/             # Assembly 4: Gameplay systems
│   │   ├── Components/      # Health, etc.
│   │   ├── Systems/         # Health processing, destroy callbacks
│   │   └── Bridge/          # Health bridge functions
│   └── LuaCharacters/       # Assembly 5: Character controller integration
│       ├── Core/            # Character components and systems
│       └── ThirdPerson/     # Third-person character implementation
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
    ↓
LuaGame (Health system, lifecycle hooks)
    ↓
LuaCharacters (Unity.CharacterController, Unity.InputSystem)
```

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
vm.CallUpdate("my_script", entityId, stateRef, deltaTime);
```

### LuaECS (Full ECS Integration)

Scripts define callbacks that ECS systems invoke:

```lua
-- scripts/fruit_eater.lua
function OnInit(entity, state)
    state.speed = 5
end

function OnUpdate(entity, state, dt)
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

### Health System

Entities can have health and respond to death:

```lua
function OnInit(entity, state)
    health.set(entity, 100, 100)  -- 100/100 HP
end

function OnUpdate(entity, state, dt)
    local hp, max = health.get(entity)
    if hp < max * 0.5 then
        state.mode = "fleeing"
    end
end

function OnDeath(entity, state, event)
    log.info("Entity died!")
    -- Drop loot, play animation, etc.
end
```

### Character Controller

Create and control character entities from Lua:

```lua
function OnInit(entity, state)
    state.player = character.create({x = 0, y = 1, z = 0})
end

function OnUpdate(entity, state, dt)
    local move = input.get_move()
    character.set_move_input(state.player, move.x, move.y)
    
    if input.get_jump() then
        character.set_jump(state.player, true)
    end
end
```

## Lua API Reference

### ecs namespace

| Function                                  | Description                        |
| ----------------------------------------- | ---------------------------------- |
| `ecs.get_position(entity)`                | Returns `{x, y, z}` table          |
| `ecs.set_position(entity, pos)`           | Set entity position                |
| `ecs.get_rotation(entity)`                | Returns euler angles `{x, y, z}`   |
| `ecs.distance(entity1, entity2)`          | Distance between entities          |
| `ecs.query_entities_near(entity, radius)` | Returns array of nearby entity IDs |
| `ecs.create_entity(pos)`                  | Create entity, returns ID          |
| `ecs.destroy_entity(entity)`              | Destroy entity                     |
| `ecs.add_script(entity, scriptName)`      | Add script to entity               |
| `ecs.move_toward(entity, target, speed)`  | Queue movement command             |
| `ecs.emit_command(entity, command)`       | Queue custom command               |

### health namespace

| Function                            | Description                          |
| ----------------------------------- | ------------------------------------ |
| `health.get(entity)`                | Returns `current, max` health values |
| `health.set(entity, current, max?)` | Set health (max optional)            |
| `health.damage(entity, amount)`     | Apply damage to entity               |
| `health.heal(entity, amount)`       | Heal entity                          |
| `health.is_dead(entity)`            | Returns true if entity is dead       |

### character namespace

| Function                                 | Description                          |
| ---------------------------------------- | ------------------------------------ |
| `character.create(pos)`                  | Create character entity with physics |
| `character.set_move_input(entity, x, y)` | Set movement input                   |
| `character.set_jump(entity, jump)`       | Set jump state                       |
| `character.is_grounded(entity)`          | Check if grounded                    |
| `character.get_velocity(entity)`         | Returns velocity `{x, y, z}`         |

### input namespace

| Function           | Description                     |
| ------------------ | ------------------------------- |
| `input.get_move()` | Returns `{x, y}` movement input |
| `input.get_look()` | Returns `{x, y}` look input     |
| `input.get_jump()` | Returns jump button state       |

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
| `OnUpdate(entity, state, dt)`      | Called every frame                    |
| `OnDestroy(entity, state)`         | Called before entity is destroyed     |
| `OnCommand(entity, state, cmd)`    | Called when command is sent to entity |
| `OnDeath(entity, state, event)`    | Called when entity health reaches 0   |
| `On<EventName>(entity, state, ev)` | Called for custom events              |

## Building Native Libraries

The `.github/workflows/build.yml` workflow builds LuaJIT for all supported platforms:

- Windows x64
- Linux x64
- macOS x64 and arm64
- iOS arm64
- Android arm64, armv7, x86

Run the workflow manually or push changes to `.github/workflows/build.yml` to trigger a build.

## Known Limitations

- **Single-threaded execution** - All Lua scripts run sequentially on the main thread
- **No inventory/items system** - Data registry infrastructure exists but is not yet integrated
- **No pathfinding** - Navigation must be implemented in game-specific code
- **No animation bridge** - Animator integration planned for future release

## Requirements

- Unity 2022.3+
- Unity Entities 1.0.0+
- Unity Collections 2.0.0+
- Unity Burst 1.8.0+
- Unity Logging 1.3.0+
- Unity Character Controller 1.0.0+ (for LuaCharacters)
- Unity Input System 1.0.0+ (for input bridge)

## Documentation

See the `docs/` folder for detailed architecture documentation:

- [ARCHITECTURE.md](docs/ARCHITECTURE.md) - System design and bridge API reference
- [multithreading.md](docs/multithreading.md) - Future multithreading design (not yet implemented)

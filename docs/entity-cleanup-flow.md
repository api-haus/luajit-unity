## Entity Cleanup Flow

This document explains how scripted entities are initialized and torn down, which systems participate, and the two-buffer lifecycle architecture.

### Architecture Overview

The Lua script lifecycle uses two separate buffers:
- **LuaScriptRequest** (`IBufferElementData`) - pending script requests with unique hashes
- **LuaScript** (`ICleanupBufferElementData`) - initialized scripts with VM state

All lifecycle systems run in `InitializationSystemGroup`:
- `LuaScriptFulfillmentSystem` - processes requests, creates VM state, supports script disabling
- `LuaScriptCleanupSystem` - handles destruction, calls OnDestroy

### Entity Creation Flow

1. Entity is created with `LuaScriptRequest` buffer containing script names and unique hashes
2. Entity has `LuaEntityId` (real ID or sentinel 0 for baked entities)
3. No `LuaScript` buffer yet

### Script Fulfillment Flow

`LuaScriptFulfillmentSystem` runs in `InitializationSystemGroup`:

1. Query entities with `LuaScriptRequest` buffer
2. For each unfulfilled request (`!Fulfilled`):
   - Check dedup: skip if `LuaScript` buffer already has matching `RequestHash`
   - Load script into VM
   - Assign/confirm `LuaEntityId` (convert sentinel to real ID)
   - Create VM state via `CreateEntityState`
   - Add entry to `LuaScript` buffer with same `RequestHash`
   - Mark request as `Fulfilled = true`
   - Call `OnInit` callback
3. Fulfilled requests remain in buffer for tracking

### Script Disabling (Runtime Removal)

Scripts can be disabled at runtime without destroying the entity:

1. Call `DisableScript(entity, scriptName)` or `DisableScriptByHash(entity, hash)`
2. System calls `OnDestroy` callback
3. Releases VM state via `ReleaseEntityState`
4. Sets `StateRef = -1` and `Disabled = true`
5. Script remains in buffer but is skipped by runtime systems
6. Actual cleanup happens when entity is destroyed

### Entity Destruction Flow

1. Destruction is requested by removing `LuaEntityId`
2. Entity now matches cleanup query (has `LuaScript` but no `LuaEntityId`)

`LuaScriptCleanupSystem` runs after fulfillment:

1. Query entities with `LuaScript` + no `LuaEntityId`
2. For each script with valid `StateRef >= 0` and `!Disabled`:
   - Call `OnDestroy` callback
   - Release VM state via `ReleaseEntityState`
3. Call `DestroyEntity` to strip non-cleanup components
4. Remove `LuaScript` buffer
5. Entity is automatically destroyed when no cleanup components remain

### Component Definitions

```csharp
// Request to add a script (regular buffer)
public struct LuaScriptRequest : IBufferElementData
{
    public FixedString64Bytes ScriptName;
    public Hash128 RequestHash;  // Unique hash for dedup
    public bool Fulfilled;       // True once script is created
}

// Initialized script with VM state (cleanup buffer)
public struct LuaScript : ICleanupBufferElementData
{
    public FixedString64Bytes ScriptName;
    public int StateRef;         // VM registry reference (-1 if disabled)
    public int EntityIndex;      // Lua entity ID
    public Hash128 RequestHash;  // Links back to request
    public bool Disabled;        // True = skip execution, cleanup at entity end
}
```

### System Ordering

```
InitializationSystemGroup
├── LuaScriptFulfillmentSystem  (processes requests, disables scripts)
└── LuaScriptCleanupSystem      (handles destruction, after fulfillment)

SimulationSystemGroup
└── LuaScriptingSystem          (runtime updates and events - skips disabled)
```

### Benefits

1. **Clear separation**: request vs fulfilled states are explicit
2. **No flag confusion**: no `Initialized` or `Registered` fields needed
3. **Single system group**: all lifecycle logic in `InitializationSystemGroup`
4. **Presence implies state**: being in `LuaScript` buffer means initialized
5. **Deduplication**: `RequestHash` prevents duplicate script initialization
6. **Dynamic removal**: scripts can be disabled without entity destruction

### Troubleshooting

- **Entity never disappears**: verify cleanup system runs after fulfillment, and that `LuaEntityId` was removed to trigger cleanup
- **Scripts not initializing**: check that `LuaScriptRequest` buffer exists with unfulfilled entries, and that fulfillment system is running
- **OnDestroy not called**: ensure script had valid `StateRef >= 0` and `!Disabled` before destruction
- **Duplicate scripts**: ensure unique `RequestHash` for each script request
- **Disabled script still runs**: check `Disabled` flag is set and runtime systems check it

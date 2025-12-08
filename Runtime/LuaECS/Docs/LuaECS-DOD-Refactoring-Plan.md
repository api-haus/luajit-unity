# LuaECS Data-Oriented Refactoring Plan

> Refactor LuaECS from class-based architecture to data-oriented design using structs, NativeCollections, and SharedStatic for Burst compatibility. Eliminate proxy methods by injecting dependencies directly at call sites.

---

## Current Architecture Summary

### Classes and Purposes

| Class                        | Purpose                                                | Public Methods                                                                                                                                                                                                                                                                                                                             |
| ---------------------------- | ------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `LuaScriptingSystem`         | Orchestrates Lua runtime, updates, events              | `EntityCollection`, `CreateEntityDeferred()`, `AddScriptDeferred()`, `SetPositionDeferred()`, `DestroyEntityDeferred()`, `GetEntityIdFromEntity()`, `GetEntityFromId()`, `IsDeferred()`, `SendCommand()`                                                                                                                                   |
| `LuaScriptFulfillmentSystem` | Processes script requests, creates initialized scripts | `EntityIdManager`, `EntityCollection`, `GetEntityIdFromEntity()`, `GetEntityFromId()`, `DisableScript()`, `DisableScriptByHash()`                                                                                                                                                                                                          |
| `LuaEntityIdManager`         | Facade for entity operations, delegates to collection  | `Collection`, `BeginFrame()`, `GetOrAssignEntityId()`, `CreateEntityDeferred()`, `CreateEntityWithId()`, `AddScriptDeferred()`, `SetPositionDeferred()`, `DestroyEntityDeferred()`, `GetEntityIdFromEntity()`, `GetEntityFromId()`, `IsDeferred()`, `RegisterExisting()`, `SyncWithWorld()`                                                |
| `LuaEntityCollection`        | Owns entity ID mappings, O(1) bidirectional lookup     | `EntityIdMap`, `Count`, `IsCreated`, `Create()`, `CreateWithId()`, `Register()`, `RegisterBaked()`, `RegisterImmediate()`, `Destroy()`, `CommitPendingCreations()`, `CommitPendingDestructions()`, `GetEntity()`, `GetId()`, `Contains()`, `IsPending()`, `IsMarkedForDestruction()`, `GetAllIds()`, `GetAllEntities()`, `SyncWithWorld()` |
| `LuaEventDispatcher`         | Event collection and dispatch to Lua scripts           | `SetVM()`, `CollectPendingEvents()`, `ClearEventBuffers()`, `DispatchEvents()`                                                                                                                                                                                                                                                             |

### Proxy Call Chains (Problems)

```
GetEntityIdFromEntity:
  LuaScriptingSystem → LuaScriptFulfillmentSystem → LuaEntityIdManager → LuaEntityCollection

GetEntityFromId:
  LuaScriptingSystem → LuaScriptFulfillmentSystem → LuaEntityIdManager → LuaEntityCollection

EntityCollection:
  LuaScriptingSystem → LuaScriptFulfillmentSystem → LuaEntityIdManager.Collection
```

### Current SharedStatic Usage (Already DOD)

```csharp
// LuaECSBridge.cs - already uses SharedStatic
SharedStatic<BurstBridgeContext> s_BurstContext;        // Entity lookups, transforms, pending ops
SharedStatic<int> s_NextEntityId;                        // Atomic entity ID counter
SharedStatic<CharacterBridgeContext> s_CharacterContext; // Character component lookups
SharedStatic<TransformFieldNames> s_FieldNames;          // Lua field name cache
```

---

## Target Architecture

### New Structs with SharedStatic

```csharp
// Single source of truth for entity ID mappings
public struct LuaEntityRegistry
{
    public UnsafeHashMap<int, Entity> IdToEntity;
    public UnsafeHashMap<Entity, int> EntityToId;
    public UnsafeHashMap<int, Entity> PendingCreations;
    public UnsafeHashSet<int> PendingDestructions;
    public int NextId;
    public bool IsCreated;
}
static readonly SharedStatic<LuaEntityRegistry> s_EntityRegistry;

// Event processing state
public struct LuaEventContext
{
    public UnsafeList<PendingEventDispatch> PendingEvents;
    public UnsafeList<Entity> EntitiesToClear;
    public bool IsValid;
}
static readonly SharedStatic<LuaEventContext> s_EventContext;
```

### Dependency Injection Pattern

Instead of proxy methods, callers access SharedStatic directly:

```csharp
// Before (proxy chain)
var entityId = scriptingSystem.GetEntityIdFromEntity(entity);

// After (direct access)
ref var registry = ref LuaEntityRegistry.Data;
var entityId = registry.EntityToId.TryGetValue(entity, out var id) ? id : -1;
```

---

## Implementation Steps

### Step 1: Create LuaEntityRegistry struct + SharedStatic

Create new file `LuaEntityRegistry.cs`:
- Define `LuaEntityRegistry` struct with all NativeCollections
- Add `SharedStatic<LuaEntityRegistry>` accessor
- Add static helper methods: `Initialize()`, `Dispose()`, `GetEntityFromId()`, `GetIdFromEntity()`
- Keep `LuaEntityCollection` functional but delegate to SharedStatic internally

**Validation:** All existing tests pass, no behavior change.

### Step 2: Migrate LuaEntityCollection to use LuaEntityRegistry

Modify `LuaEntityCollection.cs`:
- Replace instance `UnsafeHashMap` fields with references to `LuaEntityRegistry.Data`
- Keep class interface intact for backward compatibility
- Methods now operate on SharedStatic data

**Validation:** All existing tests pass, no behavior change.

### Step 3: Eliminate LuaEntityIdManager facade

Modify `LuaEntityIdManager.cs` → inline into callers:
- `BeginFrame()` → move to `LuaScriptFulfillmentSystem.OnUpdate()`
- `GetOrAssignEntityId()` → inline in `LuaScriptFulfillmentSystem`
- `CreateEntityDeferred()` → use `LuaEntityRegistry` directly
- `GetEntityIdFromEntity()` / `GetEntityFromId()` → use `LuaEntityRegistry` static methods

Update all callers to use `LuaEntityRegistry` directly:
- `LuaScriptingSystem.ProcessPendingOperations()`
- `LuaCharacterBridge.CreateCharacterEntity()`
- `LuaPlayerBridge.TryEnsureLuaEntityId()`

**Validation:** All existing tests pass, LuaEntityIdManager deleted.

### Step 4: Create LuaEventContext struct + SharedStatic

Create event processing in DOD style:
- Add `LuaEventContext` struct to `LuaECSBridge.cs`
- Add `SharedStatic<LuaEventContext>` accessor
- Migrate `LuaEventDispatcher` managed Lists to `UnsafeList`

**Validation:** Event dispatch still works in tests.

### Step 5: Inline LuaEventDispatcher into LuaScriptingSystem

Modify `LuaScriptingSystem.cs`:
- Remove `m_EventDispatcher` field
- Inline `CollectPendingEvents()`, `ClearEventBuffers()`, `DispatchEvents()` using `LuaEventContext`
- Delete `LuaEventDispatcher.cs`

**Validation:** Event processing tests pass.

### Step 6: Remove proxy methods from LuaScriptingSystem

Update `LuaScriptingSystem.cs`:
- Remove `GetEntityIdFromEntity()`, `GetEntityFromId()`, `IsDeferred()` proxy methods
- Update callers (bridges) to use `LuaEntityRegistry` directly
- Keep `EntityCollection` property temporarily for compatibility

Update bridge files:
- `LuaPlayerBridge.cs`: Replace `s_ScriptingSystem.GetEntityFromId()` with `LuaEntityRegistry.GetEntityFromId()`
- `LuaSpatialBridge.cs`: Replace `s_ScriptingSystem.GetEntityIdFromEntity()` with `LuaEntityRegistry.GetIdFromEntity()`

**Validation:** All bridge functions work correctly.

### Step 7: Remove proxy methods from LuaScriptFulfillmentSystem

Update `LuaScriptFulfillmentSystem.cs`:
- Remove `GetEntityIdFromEntity()`, `GetEntityFromId()` proxy methods
- Remove `EntityIdManager` property, use `LuaEntityRegistry` directly
- Keep `EntityCollection` property temporarily

**Validation:** Script fulfillment works correctly.

### Step 8: Consolidate EntityCollection into LuaEntityRegistry

Final cleanup:
- Move remaining `LuaEntityCollection` logic into `LuaEntityRegistry` static methods
- Delete `LuaEntityCollection.cs`
- Delete `LuaEntityIdManager.cs`
- Update `LuaScriptFulfillmentSystem` to use `LuaEntityRegistry` directly
- Remove `EntityCollection` properties from systems

**Validation:** Full test suite passes.

---

## File Changes Summary

| File                            | Action                                               |
| ------------------------------- | ---------------------------------------------------- |
| `LuaEntityRegistry.cs`          | **CREATE** - SharedStatic struct for entity mappings |
| `LuaEntityCollection.cs`        | **DELETE** - Merged into LuaEntityRegistry           |
| `LuaEntityIdManager.cs`         | **DELETE** - Facade removed, logic inlined           |
| `LuaEventDispatcher.cs`         | **DELETE** - Inlined into LuaScriptingSystem         |
| `LuaECSBridge.cs`               | **MODIFY** - Add LuaEventContext, update callers     |
| `LuaScriptingSystem.cs`         | **MODIFY** - Remove proxies, inline event handling   |
| `LuaScriptFulfillmentSystem.cs` | **MODIFY** - Use LuaEntityRegistry directly          |
| `LuaCharacterBridge.cs`         | **MODIFY** - Use LuaEntityRegistry directly          |
| `LuaPlayerBridge.cs`            | **MODIFY** - Use LuaEntityRegistry directly          |
| `LuaSpatialBridge.cs`           | **MODIFY** - Use LuaEntityRegistry directly          |

---

## Dependency Graph (After Refactoring)

```
                    SharedStatic<LuaEntityRegistry>
                              │
         ┌────────────────────┼────────────────────┐
         │                    │                    │
         ▼                    ▼                    ▼
  LuaScriptingSystem   LuaScriptFulfillment   LuaECSBridge
         │                    System           (partials)
         │                    │                    │
         │                    │                    │
         └────────────────────┴────────────────────┘
                              │
                    SharedStatic<BurstBridgeContext>
                    SharedStatic<LuaEventContext>
                    SharedStatic<CharacterBridgeContext>
```

All data flows through SharedStatic containers, enabling Burst compilation and eliminating object-oriented indirection.

---

## Detailed Class Analysis

### LuaScriptingSystem.cs (Lines 37-326)

**Location:** `Runtime/LuaECS/Systems/LuaScriptingSystem.cs`

**Fields (managed state - problematic):**
```csharp
LuaVMManager m_VM;                           // Managed, cannot eliminate
LuaEntityCommandBufferSystem m_ECBSystem;    // System reference
LuaScriptFulfillmentSystem m_FulfillmentSystem; // System reference - PROXY TARGET
LuaEventDispatcher m_EventDispatcher;        // Class instance - ELIMINATE
EntityCommandBuffer m_CurrentECB;            // Per-frame ECB
bool m_ECBValid;                             // ECB validity flag
ComponentLookup<LocalTransform> m_TransformLookup;
BufferLookup<LuaScript> m_ScriptBufferLookup;
ComponentLookup<LuaCharacterControl> m_CharacterControlLookup;
ComponentLookup<KinematicCharacterBody> m_CharacterBodyLookup;
ComponentLookup<PhysicsVelocity> m_PhysicsVelocityLookup;
List<(...)> m_PendingUpdates;                // Managed List - CONVERT TO NATIVE
```

**Proxy Methods to Eliminate:**
```csharp
// Line 292-295
public int GetEntityIdFromEntity(Entity entity)
    => m_FulfillmentSystem.GetEntityIdFromEntity(entity);

// Line 297-300
public Entity GetEntityFromId(int entityId)
    => m_FulfillmentSystem.GetEntityFromId(entityId);

// Line 302-305
public bool IsDeferred(int entityId)
    => m_FulfillmentSystem.EntityIdManager.IsDeferred(entityId);

// Line 246
public LuaEntityCollection EntityCollection
    => m_FulfillmentSystem.EntityCollection;
```

### LuaEntityCollection.cs (Lines 1-370)

**Location:** `Runtime/LuaECS/Core/LuaEntityCollection.cs`

**Fields (already using Unsafe collections):**
```csharp
int m_NextId;
UnsafeHashMap<int, Entity> m_IdToEntity;
UnsafeHashMap<Entity, int> m_EntityToId;
UnsafeHashMap<int, Entity> m_PendingCreations;
UnsafeHashSet<int> m_PendingDestructions;
EntityManager m_EntityManager;  // Problem: EntityManager stored in class
```

**Key insight:** This class already uses `UnsafeHashMap` internally. The refactoring moves these to a SharedStatic struct so they can be accessed from anywhere without class instance.

### LuaEntityIdManager.cs (Lines 1-203)

**Location:** `Runtime/LuaECS/Systems/Support/LuaEntityIdManager.cs`

**Pure facade pattern - entire class can be eliminated:**
```csharp
// Every method just delegates to m_Collection
public int CreateEntityDeferred(float3 position, EntityCommandBuffer ecb)
    => m_Collection.Create(position, ecb);

public Entity GetEntityFromId(int entityId)
    => m_Collection.GetEntity(entityId);

public int GetEntityIdFromEntity(Entity entity)
{
    // Slight logic - needs inlining
    var id = m_Collection.GetId(entity);
    if (id > 0) return id;
    if (m_PendingEntityIds.TryGetValue(entity, out var pendingId))
        return pendingId;
    return -1;
}
```

### LuaEventDispatcher.cs (Lines 1-148)

**Location:** `Runtime/LuaECS/Systems/Support/LuaEventDispatcher.cs`

**Managed collections - must convert:**
```csharp
readonly List<(Entity, int, string, int, int, List<LuaEvent>)> m_PendingEvents;
readonly List<Entity> m_EntitiesToClear;
```

**Conversion target:**
```csharp
public struct PendingEventDispatch
{
    public Entity Entity;
    public int ScriptIndex;
    public FixedString64Bytes ScriptName;
    public int EntityIndex;
    public int StateRef;
    public int EventStartIndex;  // Index into shared event buffer
    public int EventCount;
}

public struct LuaEventContext
{
    public UnsafeList<PendingEventDispatch> PendingEvents;
    public UnsafeList<LuaEvent> EventBuffer;  // Flat buffer for all events
    public UnsafeList<Entity> EntitiesToClear;
    public bool IsValid;
}
```

---

## Bridge Files Analysis

### LuaCharacterBridge.cs

**Proxy usage at line 163:**
```csharp
scriptingSystem.EntityCollection.RegisterImmediate(entity, entityId);
```

**After refactoring:**
```csharp
LuaEntityRegistry.RegisterImmediate(entity, entityId, entityManager);
```

### LuaPlayerBridge.cs

**Proxy usages:**
```csharp
// Line 86
var characterId = s_ScriptingSystem?.GetEntityIdFromEntity(control.controlledCharacter) ?? -1;

// Line 119
characterEntity = s_ScriptingSystem.GetEntityFromId(characterId);

// Line 199
entity = s_ScriptingSystem.GetEntityFromId(entityId);

// Line 212
var existingId = s_ScriptingSystem.GetEntityIdFromEntity(entity);

// Line 235
s_ScriptingSystem.EntityCollection.RegisterImmediate(entity, entityId);
```

**After refactoring:**
```csharp
// All become:
var characterId = LuaEntityRegistry.GetIdFromEntity(entity);
var entity = LuaEntityRegistry.GetEntityFromId(entityId);
LuaEntityRegistry.RegisterImmediate(entity, entityId, s_EntityManager);
```

### LuaSpatialBridge.cs

**Proxy usage at line 131:**
```csharp
var entityId = s_ScriptingSystem.GetEntityIdFromEntity(entities[i]);
```

**After refactoring:**
```csharp
var entityId = LuaEntityRegistry.GetIdFromEntity(entities[i]);
```

---

## Risk Mitigation

1. **Incremental approach:** Each step maintains backward compatibility until the next step
2. **Test validation:** Run test suite after each step
3. **SharedStatic initialization:** Ensure `Initialize()` called before any access
4. **Thread safety:** `SharedStatic` is inherently thread-safe for reads, writes need synchronization where needed
5. **Disposal:** Ensure `Dispose()` called on domain reload / shutdown

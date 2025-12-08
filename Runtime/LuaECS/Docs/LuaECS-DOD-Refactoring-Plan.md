# LuaECS Data-Oriented Refactoring Plan

> Refactor LuaECS from class-based architecture to data-oriented design using structs, NativeCollections, and SharedStatic for Burst compatibility. Eliminate proxy methods by injecting dependencies directly at call sites.

## Status: COMPLETED ✓

All steps have been completed. The refactoring eliminated facade classes and consolidated entity management into `LuaEntityRegistry`.

### Completed Changes

| File                            | Action                                               | Status |
| ------------------------------- | ---------------------------------------------------- | ------ |
| `LuaEntityRegistry.cs`          | Created SharedStatic struct for entity mappings      | ✓      |
| `LuaEntityCollection.cs`        | Deleted - Merged into LuaEntityRegistry              | ✓      |
| `LuaEntityIdManager.cs`         | Deleted - Facade removed, logic inlined              | ✓      |
| `LuaEventDispatcher.cs`         | Deleted - Inlined into LuaScriptingSystem            | ✓      |
| `LuaScriptFulfillmentSystem.cs` | Modified - Uses LuaEntityRegistry directly           | ✓      |
| Bridge files                    | Modified - Use LuaEntityRegistry directly            | ✓      |
| `LuaEntityRegistryTest.cs`      | Created - Tests for LuaEntityRegistry                | ✓      |

### Final Architecture

```
                    SharedStatic<LuaEntityRegistry>
                              │
         ┌────────────────────┼────────────────────┐
         │                    │                    │
         ▼                    ▼                    ▼
  LuaScriptingSystem   LuaScriptFulfillment   LuaECSBridge
                             System           (partials)
```

All entity ID management flows through `LuaEntityRegistry`:
- `LuaEntityRegistry.Initialize()` - Called by `LuaScriptFulfillmentSystem.OnCreate()`
- `LuaEntityRegistry.Dispose()` - Called by `LuaScriptFulfillmentSystem.OnDestroy()`
- `LuaEntityRegistry.Clear()` - Used for test isolation

---

## Original Implementation Steps (Completed)

### Step 1: Create LuaEntityRegistry struct + SharedStatic ✓

Created `LuaEntityRegistry.cs` with:
- `LuaEntityRegistryData` struct with all NativeCollections
- `SharedStatic<LuaEntityRegistryData>` accessor
- Static helper methods: `Initialize()`, `Dispose()`, `Clear()`, `GetEntityFromId()`, `GetIdFromEntity()`

### Step 2: Migrate LuaEntityCollection to use LuaEntityRegistry ✓

Modified `LuaEntityCollection.cs` to delegate all operations to `LuaEntityRegistry`.

### Step 3: Eliminate LuaEntityIdManager facade ✓

Inlined all `LuaEntityIdManager` methods into:
- `LuaScriptFulfillmentSystem` - `GetOrAssignEntityId()`, `BeginFrame()`
- `LuaEntityRegistry` - All entity operations

### Step 4: Create LuaEventContext (deferred)

Event context already handled by existing `BurstBridgeContext`.

### Step 5: Inline LuaEventDispatcher into LuaScriptingSystem ✓

Event dispatch logic inlined.

### Step 6: Remove proxy methods from LuaScriptingSystem ✓

Updated all bridge files to use `LuaEntityRegistry` directly.

### Step 7: Remove proxy methods from LuaScriptFulfillmentSystem ✓

System now uses `LuaEntityRegistry` directly for all operations.

### Step 8: Consolidate EntityCollection into LuaEntityRegistry ✓

Final cleanup:
- Deleted `LuaEntityCollection.cs`
- Deleted `LuaEntityIdManager.cs`
- Updated tests to use `LuaEntityRegistry` directly
- All 56 tests pass

---

## Key API (Final)

### LuaEntityRegistry Static Methods

```csharp
// Lifecycle
LuaEntityRegistry.Initialize(int capacity = 256);
LuaEntityRegistry.Dispose();
LuaEntityRegistry.Clear();  // For test isolation

// Entity Creation
int LuaEntityRegistry.Create(float3 position, EntityCommandBuffer ecb);
void LuaEntityRegistry.CreateWithId(int id, float3 position, EntityCommandBuffer ecb);
int LuaEntityRegistry.Register(Entity entity, EntityCommandBuffer ecb);
int LuaEntityRegistry.RegisterBaked(Entity entity, EntityCommandBuffer ecb);
void LuaEntityRegistry.RegisterImmediate(Entity entity, int id, EntityManager entityManager);

// Entity Destruction
bool LuaEntityRegistry.Destroy(int entityId, EntityCommandBuffer ecb);

// Lookups (O(1))
Entity LuaEntityRegistry.GetEntityFromId(int entityId);
int LuaEntityRegistry.GetIdFromEntity(Entity entity);
bool LuaEntityRegistry.Contains(int entityId);
bool LuaEntityRegistry.Contains(Entity entity);

// State Queries
bool LuaEntityRegistry.IsPending(int entityId);
bool LuaEntityRegistry.IsMarkedForDestruction(int entityId);
int LuaEntityRegistry.Count { get; }
bool LuaEntityRegistry.IsCreated { get; }

// Frame Management
void LuaEntityRegistry.BeginFrame(EntityManager entityManager);
void LuaEntityRegistry.CommitPendingCreations(EntityManager entityManager);
void LuaEntityRegistry.CommitPendingDestructions();
void LuaEntityRegistry.SyncWithWorld(EntityManager entityManager);

// Script Operations
int LuaEntityRegistry.GetOrAssignEntityId(Entity entity, EntityCommandBuffer ecb, EntityManager entityManager);
bool LuaEntityRegistry.AddScriptDeferred(int entityId, string scriptName, EntityCommandBuffer ecb, EntityManager entityManager);
```

### Test Isolation Pattern

```csharp
[UnitySetUp]
public IEnumerator SetUp()
{
    if (!LuaEntityRegistry.IsCreated)
        LuaEntityRegistry.Initialize(16);
    else
        LuaEntityRegistry.Clear();
    yield return null;
}

[UnityTearDown]
public IEnumerator TearDown()
{
    LuaEntityRegistry.Clear();
    // Clean up entities...
    yield return null;
}
```

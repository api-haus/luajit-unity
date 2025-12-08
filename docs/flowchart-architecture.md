# Architecture Flowchart

This diagram shows the structural relationships and decision points in the LuaJIT ECS package.

```mermaid
flowchart TD
    subgraph Lua["Lua Layer (Scripts)"]
        LuaScript[/"Lua Script<br/>(OnInit, OnUpdate, OnEvent, OnDestroy)"/]
    end

    subgraph Core["Core (Runtime/Core)"]
        VM[LuaVMManager<br/>Singleton]
        Bridge[LuaECSBridge<br/>Static]
        PathUtil[LuaScriptPathUtility<br/>xxHash3]
        
        subgraph BridgeFunctions["Bridge API"]
            Transform[Transform<br/>get/set_position<br/>get_rotation]
            Spatial[Spatial<br/>distance<br/>query_entities_near]
            Entity[Entity<br/>create/destroy<br/>add_script]
            Command[Command<br/>emit_command<br/>move_toward]
            Log[Logging<br/>log, log_debug<br/>log_warning, log_error]
        end
    end

    subgraph Systems["Systems (Runtime/Systems)"]
        subgraph InitGroup["InitializationSystemGroup"]
            Fulfill[LuaScriptFulfillmentSystem<br/>Request Processing]
            Cleanup[LuaScriptCleanupSystem<br/>OnDestroy + Cleanup]
        end
        subgraph SimGroup["SimulationSystemGroup"]
            LSS[LuaScriptingSystem<br/>Runtime Updates]
            CmdProc[LuaCommandProcessorSystem<br/>Burst]
            ECBSys[LuaEntityCommandBufferSystem]
        end
    end

    subgraph Components["Components"]
        LuaScriptReq[LuaScriptRequest Buffer<br/>IBufferElementData]
        LuaScriptComp[LuaScript Buffer<br/>ICleanupBufferElementData]
        LuaEventComp[LuaEvent Buffer]
        LuaCommandComp[LuaCommand Buffer]
        EntityId[LuaEntityId]
    end

    %% Lua to Bridge
    LuaScript -->|"ecs.* calls"| Bridge
    Bridge --> Transform
    Bridge --> Spatial
    Bridge --> Entity
    Bridge --> Command
    Bridge --> Log

    %% Core relationships
    VM -->|"manages"| LuaScript
    Bridge -->|"queries"| LSS
    Bridge -->|"queues commands"| CmdProc
    PathUtil -->|"generates hash"| LuaScriptReq

    %% System relationships
    Fulfill -->|"processes"| LuaScriptReq
    Fulfill -->|"creates"| LuaScriptComp
    Fulfill -->|"calls OnInit via"| VM
    Cleanup -->|"calls OnDestroy via"| VM
    Cleanup -->|"removes"| LuaScriptComp
    LSS -->|"calls OnUpdate via"| VM
    LSS -->|"structural changes"| ECBSys
    CmdProc -->|"processes"| LuaCommandComp

    %% Component relationships
    LSS -->|"reads (skip Disabled)"| LuaScriptComp
    LSS -->|"dispatches"| LuaEventComp
    Fulfill -->|"assigns"| EntityId

    %% Styling
    classDef burst fill:#4a6,stroke:#2a4,color:#fff
    classDef mainThread fill:#46a,stroke:#24a,color:#fff
    classDef singleton fill:#a64,stroke:#842,color:#fff
    classDef lua fill:#64a,stroke:#428,color:#fff
    classDef cleanup fill:#a46,stroke:#824,color:#fff

    class CmdProc burst
    class LSS,ECBSys,Fulfill mainThread
    class Cleanup cleanup
    class VM singleton
    class LuaScript lua
```

## Component Responsibilities

| Layer   | Component                    | Thread | Responsibility                                         |
| ------- | ---------------------------- | ------ | ------------------------------------------------------ |
| Core    | LuaVMManager                 | Main   | Lua state lifecycle, script loading, callback dispatch |
| Core    | LuaECSBridge                 | Main   | Static API surface for Lua → ECS communication         |
| Core    | LuaScriptPathUtility         | N/A    | Script path resolution, xxHash3 hash generation        |
| Systems | LuaScriptFulfillmentSystem   | Main   | Request processing, script init, OnInit, disabling     |
| Systems | LuaScriptCleanupSystem       | Main   | OnDestroy callbacks, state release, entity cleanup     |
| Systems | LuaScriptingSystem           | Main   | Runtime updates, event dispatch, ECB coordination      |
| Systems | LuaCommandProcessorSystem    | Worker | Burst-compiled movement processing                     |
| Systems | LuaEntityCommandBufferSystem | Main   | Structural change playback                             |

## Two-Buffer Architecture

```mermaid
flowchart LR
    subgraph Creation["Entity Creation"]
        A[Create Entity] --> B[Add LuaScriptRequest]
        B --> C[Add LuaEntityId]
    end
    
    subgraph Fulfillment["Script Fulfillment"]
        D[Process Request] --> E{Already Fulfilled?}
        E -->|No| F[Check Dedup via Hash]
        F --> G[Create VM State]
        G --> H[Add to LuaScript]
        H --> I[Call OnInit]
        I --> J[Mark Fulfilled]
        E -->|Yes| K[Skip]
    end
    
    subgraph Runtime["Runtime"]
        L[OnUpdate Loop] --> M{Disabled?}
        M -->|No| N[Call OnUpdate]
        M -->|Yes| O[Skip]
    end
    
    subgraph Destruction["Entity Destruction"]
        P[Remove LuaEntityId] --> Q[Cleanup Query Matches]
        Q --> R[Call OnDestroy]
        R --> S[Release VM State]
        S --> T[Remove LuaScript]
        T --> U[Entity Destroyed]
    end
    
    Creation --> Fulfillment
    Fulfillment --> Runtime
    Runtime --> Destruction
```

## Decision Points

```mermaid
flowchart TD
    A[Lua calls ecs.*] --> B{Function Type?}
    
    B -->|Transform Read| C[Immediate: EntityManager.GetComponentData]
    B -->|Transform Write| D[Immediate: EntityManager.SetComponentData]
    B -->|Movement| E[Deferred: Queue to s_PendingCommands]
    B -->|Entity Create| F[Deferred: ECB.CreateEntity + LuaScriptRequest]
    B -->|Entity Destroy| G[Deferred: ECB.RemoveComponent<LuaEntityId>]
    B -->|Spatial Query| H[Immediate: EntityQuery iteration]
    
    E --> I[LuaCommandProcessorSystem]
    I --> J{Command Type?}
    J -->|Move/MoveToward| K[Burst Job: ProcessMoveCommandsJob]
    J -->|Attack| L[Main Thread: Add LuaEvent]
    J -->|DestroyEntity| M[Main Thread: ECB operations]
    
    F --> N[LuaEntityCommandBufferSystem]
    G --> N
    M --> N
    N --> O[Frame End: Playback]
    
    O --> P[Next Frame: InitializationSystemGroup]
    P --> Q[LuaScriptFulfillmentSystem]
    P --> R[LuaScriptCleanupSystem]
```

## Script Disabling Flow

```mermaid
flowchart TD
    A[DisableScript Called] --> B[Find Script by Name/Hash]
    B --> C{Found & Active?}
    C -->|Yes| D[Call OnDestroy]
    D --> E[ReleaseEntityState]
    E --> F[Set StateRef = -1]
    F --> G[Set Disabled = true]
    G --> H[Script Stays in Buffer]
    H --> I[Skipped by Runtime]
    C -->|No| J[Return false]
    
    I --> K[Entity Destruction Later]
    K --> L[Cleanup Skips Already Disabled]
    L --> M[Remove Buffer]
    M --> N[Entity Fully Destroyed]
```

## Static vs Instance Pattern

The package uses a hybrid approach:

1. **Static (Burst-compatible)**: `LuaECSBridge` uses static fields and `[MonoPInvokeCallback]` methods because Lua C function pointers cannot be instance methods.

2. **Singleton (Managed)**: `LuaVMManager` uses singleton pattern for easy access but could support interface injection for testing.

3. **System (ECS-managed)**: Systems are managed by the World and follow standard ECS lifecycle.

4. **Utility (Static)**: `LuaScriptPathUtility` provides static methods for path resolution and hash generation.

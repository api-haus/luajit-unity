# Runtime Flow Sequence Diagram

This diagram shows the runtime interactions between components during a typical frame.

```mermaid
sequenceDiagram
    participant Unity as Unity Engine
    participant Fulfill as LuaScriptFulfillmentSystem
    participant Cleanup as LuaScriptCleanupSystem
    participant LSS as LuaScriptingSystem
    participant VM as LuaVMManager
    participant Bridge as LuaECSBridge
    participant CmdProc as LuaCommandProcessorSystem
    participant ECB as EntityCommandBuffer

    Note over Unity,ECB: Frame Start - InitializationSystemGroup

    Unity->>Fulfill: OnUpdate()
    
    rect rgb(40, 60, 80)
        Note over Fulfill,VM: Phase 1: Fulfill Pending Script Requests
        Fulfill->>Fulfill: Query entities with LuaScriptRequest
        loop Each unfulfilled request
            Fulfill->>Fulfill: Check dedup via RequestHash
            Fulfill->>VM: LoadScript(scriptName)
            VM-->>Fulfill: script environment
            Fulfill->>VM: CreateEntityState()
            Fulfill->>Fulfill: Add to LuaScript buffer
            Fulfill->>Fulfill: Mark request.Fulfilled = true
            Fulfill->>VM: CallInit(script, entity, state)
            VM->>Bridge: ecs.* calls (if any)
            Bridge-->>VM: results
        end
    end

    Fulfill-->>Unity: Fulfillment complete

    Unity->>Cleanup: OnUpdate()

    rect rgb(80, 40, 40)
        Note over Cleanup,VM: Phase 2: Cleanup Orphaned Entities
        Cleanup->>Cleanup: Query: LuaScript + no LuaEntityId
        loop Each orphaned entity
            loop Each script (StateRef >= 0, !Disabled)
                Cleanup->>VM: CallFunction("OnDestroy")
                Cleanup->>VM: ReleaseEntityState()
            end
            Cleanup->>Cleanup: DestroyEntity, RemoveComponent<LuaScript>
        end
    end

    Cleanup-->>Unity: Cleanup complete

    Note over Unity,ECB: SimulationSystemGroup

    Unity->>LSS: OnUpdate()

    rect rgb(60, 40, 80)
        Note over LSS,Bridge: Phase 3: Update Scripts
        LSS->>LSS: Query LuaScript (StateRef >= 0, !Disabled)
        loop Each active script
            LSS->>VM: CallUpdate(script, entity, state, dt)
            VM->>Bridge: ecs.move_toward()
            Bridge->>Bridge: Queue to s_PendingCommands
            VM->>Bridge: ecs.query_entities_near()
            Bridge-->>VM: entity list
            VM->>Bridge: ecs.emit_command()
            Bridge->>Bridge: Queue command
        end
    end

    rect rgb(80, 60, 40)
        Note over LSS,ECB: Phase 4: Dispatch Events
        LSS->>LSS: Collect pending events (skip Disabled scripts)
        loop Each event
            LSS->>VM: CallEvent(script, entity, state, event)
        end
        LSS->>ECB: Clear event buffers
    end

    rect rgb(60, 80, 40)
        Note over LSS,ECB: Phase 5: Process Pending Operations
        LSS->>LSS: Flush entity creations
        LSS->>LSS: Flush script additions
        LSS->>LSS: Flush entity destructions
    end

    LSS-->>Unity: Update complete

    Note over Unity,CmdProc: Command Processing (After LuaScriptingSystem)

    Unity->>CmdProc: OnUpdate()
    CmdProc->>Bridge: FlushCommands()
    Bridge-->>CmdProc: NativeList<LuaCommand>
    
    rect rgb(40, 80, 60)
        Note over CmdProc: Burst-Compiled Parallel Job
        CmdProc->>CmdProc: Sort commands (Move vs Other)
        CmdProc->>CmdProc: ProcessMoveCommandsJob.ScheduleParallel()
        CmdProc->>CmdProc: Process Attack/Destroy on main thread
    end

    CmdProc-->>Unity: Commands processed

    Note over Unity,ECB: Frame End - ECB Playback
    Unity->>ECB: Playback structural changes
```

## System Ordering

```
InitializationSystemGroup
├── LuaScriptFulfillmentSystem  (processes requests, creates VM state)
└── LuaScriptCleanupSystem      (handles destruction, releases VM state)

SimulationSystemGroup
├── LuaScriptingSystem          (runtime updates, events, pending operations)
├── LuaCommandProcessorSystem   (Burst-compiled movement)
└── LuaEntityCommandBufferSystem (structural change playback)
```

## Key Observations

1. **Two-Phase Lifecycle**: `LuaScriptFulfillmentSystem` and `LuaScriptCleanupSystem` run in `InitializationSystemGroup` before simulation, ensuring scripts are ready before update and cleaned up promptly after destruction.

2. **Disabled Script Filtering**: Runtime systems (update, events) skip scripts with `Disabled=true` or `StateRef < 0`.

3. **Deferred Commands**: Lua scripts queue commands via `LuaECSBridge.s_PendingCommands`; these are processed by `LuaCommandProcessorSystem` after all scripts have run.

4. **Burst Isolation**: Movement commands are processed in a Burst-compiled parallel job (`ProcessMoveCommandsJob`), separate from main-thread operations.

5. **ECB Pattern**: All structural changes (entity creation, component addition) go through `EntityCommandBuffer` and are played back at frame end via `LuaEntityCommandBufferSystem`.

6. **Request Persistence**: Fulfilled `LuaScriptRequest` entries remain in the buffer with `Fulfilled=true` for tracking and deduplication.

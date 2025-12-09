-- Test script for fruit entities
-- Used by LuaECS test suite

function OnInit(entity, state)
    state.spawned_at = 0
    state.collected = false
end

function OnTick(entity, state, dt)
    state.spawned_at = state.spawned_at + dt
end

function OnDestroy(entity, state)
    -- Cleanup when entity is destroyed
end

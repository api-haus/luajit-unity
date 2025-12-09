-- Test script for fruit eater agent entities
-- Used by LuaECS test suite

function OnInit(entity, state)
    state.score = 0
    state.move_speed = 5.0
end

function OnTick(entity, state, dt)
    -- Agent update logic would go here
end

function OnDestroy(entity, state)
    -- Cleanup when entity is destroyed
end

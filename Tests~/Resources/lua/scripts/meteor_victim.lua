-- meteor_victim.lua
-- Agent that can be damaged by meteors in the meteor shower game mode

---@type OnInit
function OnInit(entity, state)
	state.spawned_at = 0
end

---@type OnTick
function OnTick(entity, state, dt)
	-- Victims just exist and can take damage from damage zones
	state.spawned_at = state.spawned_at + dt
end

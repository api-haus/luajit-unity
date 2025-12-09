-- Test game mode script for fruit eater mode
-- Used by GameModeManager test suite

local GRID_SIZE = 10
local AGENT_COUNT = 5
local INITIAL_FRUIT_COUNT = 20
local MIN_FRUITS = 15
local SPAWN_BATCH = 5
local SPAWN_INTERVAL = 2.0

-- Local helper: spawn a single fruit (adds to pending list)
local function spawn_fruit(state)
    local x = math.random() * GRID_SIZE - GRID_SIZE / 2
    local z = math.random() * GRID_SIZE - GRID_SIZE / 2
    local id = entities.create(x, 0, z)
    if id then
        entities.add_script(id, "fruit")
        table.insert(state.pending_fruits, id)
    end
end

-- Local helper: update a single agent's movement toward nearest fruit
local function update_agent(agent_id, state, dt)
    local pos = transform.get_position(agent_id)
    if not pos then return end

    -- Find nearest fruit using position-based distance (avoids pending entity issues)
    local nearest_dist = 999999
    local nearest_fruit = nil
    local nearest_idx = nil
    local nearest_pos = nil

    for idx, fruit_id in ipairs(state.fruits) do
        local fruit_pos = transform.get_position(fruit_id)
        if fruit_pos then
            local dx = fruit_pos.x - pos.x
            local dz = fruit_pos.z - pos.z
            local dist = math.sqrt(dx * dx + dz * dz)
            if dist < nearest_dist then
                nearest_dist = dist
                nearest_fruit = fruit_id
                nearest_idx = idx
                nearest_pos = fruit_pos
            end
        end
    end

    if nearest_fruit and nearest_pos then
        -- Move toward fruit position
        local speed = 3.0
        local dx = nearest_pos.x - pos.x
        local dz = nearest_pos.z - pos.z
        local dist = math.sqrt(dx * dx + dz * dz)
        if dist > 0.01 then
            local move_dist = math.min(speed * dt, dist)
            local new_x = pos.x + (dx / dist) * move_dist
            local new_z = pos.z + (dz / dist) * move_dist
            transform.set_position(agent_id, new_x, pos.y, new_z)
        end

        -- Eat fruit if close enough
        if nearest_dist < 0.5 then
            entities.destroy(nearest_fruit)
            table.remove(state.fruits, nearest_idx)
        end
    end
end

function OnInit(_, state)
    state.initialized = true
    state.agents = {}
    state.pending_agents = {}  -- Newly spawned, not yet realized
    state.fruits = {}
    state.pending_fruits = {}  -- Newly spawned, not yet realized
    state.spawn_timer = 0

    -- Spawn initial agents (add to pending, will be realized next tick)
    for _ = 1, AGENT_COUNT do
        local x = math.random() * GRID_SIZE - GRID_SIZE / 2
        local z = math.random() * GRID_SIZE - GRID_SIZE / 2
        local id = entities.create(x, 0, z)
        if id then
            entities.add_script(id, "fruit_eater")
            table.insert(state.pending_agents, id)
        end
    end
    log.info("Spawned " .. #state.pending_agents .. " agents")

    -- Spawn initial fruits (add to pending, will be realized next tick)
    for _ = 1, INITIAL_FRUIT_COUNT do
        spawn_fruit(state)
    end
    log.info("Initial fruits: " .. #state.pending_fruits)
end

function OnLoad(_, state)
    state.loaded = true
end

function OnTick(_, state, dt)
    -- Promote pending entities to active lists (they're now realized after ECB playback)
    for _, id in ipairs(state.pending_agents) do
        table.insert(state.agents, id)
    end
    state.pending_agents = {}

    for _, id in ipairs(state.pending_fruits) do
        table.insert(state.fruits, id)
    end
    state.pending_fruits = {}

    -- Periodically spawn more fruits if below threshold
    state.spawn_timer = state.spawn_timer + dt
    if state.spawn_timer >= SPAWN_INTERVAL then
        state.spawn_timer = 0

        -- Count existing fruits (remove destroyed ones)
        local live_fruits = {}
        for _, id in ipairs(state.fruits) do
            if entities.has_script(id, "fruit") then
                table.insert(live_fruits, id)
            end
        end
        state.fruits = live_fruits

        if #state.fruits < MIN_FRUITS then
            for _ = 1, SPAWN_BATCH do
                spawn_fruit(state)
            end
        end
    end

    -- Simple agent movement toward nearest fruit
    for _, agent_id in ipairs(state.agents) do
        update_agent(agent_id, state, dt)
    end
end

function OnBeforeUnload(_, state)
    state.unloading = true
end

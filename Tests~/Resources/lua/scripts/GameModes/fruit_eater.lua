-- Test game mode script for fruit eater mode
-- Used by GameModeManager test suite

local GRID_SIZE = 10
local AGENT_COUNT = 5
local INITIAL_FRUIT_COUNT = 20
local MIN_FRUITS = 15
local SPAWN_BATCH = 5
local SPAWN_INTERVAL = 2.0

function OnInit(world, state)
    state.initialized = true
    state.agents = {}
    state.fruits = {}
    state.spawn_timer = 0

    -- Spawn initial agents
    for i = 1, AGENT_COUNT do
        local x = math.random() * GRID_SIZE - GRID_SIZE / 2
        local z = math.random() * GRID_SIZE - GRID_SIZE / 2
        local id = entities.create(x, 0, z)
        if id then
            entities.add_script(id, "fruit_eater")
            table.insert(state.agents, id)
        end
    end
    log.info("Spawned " .. #state.agents .. " agents")

    -- Spawn initial fruits
    for i = 1, INITIAL_FRUIT_COUNT do
        spawn_fruit(state)
    end
    log.info("Initial fruits: " .. #state.fruits)
end

function spawn_fruit(state)
    local x = math.random() * GRID_SIZE - GRID_SIZE / 2
    local z = math.random() * GRID_SIZE - GRID_SIZE / 2
    local id = entities.create(x, 0, z)
    if id then
        entities.add_script(id, "fruit")
        table.insert(state.fruits, id)
    end
end

function OnLoad(world, state)
    state.loaded = true
end

function OnTick(world, state, dt)
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
            for i = 1, SPAWN_BATCH do
                spawn_fruit(state)
            end
        end
    end

    -- Simple agent movement toward nearest fruit
    for _, agent_id in ipairs(state.agents) do
        update_agent(agent_id, state, dt)
    end
end

function update_agent(agent_id, state, dt)
    local pos = transform.get_position(agent_id)
    if not pos then return end

    -- Find nearest fruit
    local nearest_dist = 999999
    local nearest_fruit = nil
    local nearest_idx = nil

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
            end
        end
    end

    if nearest_fruit then
        -- Move toward fruit
        local fruit_pos = transform.get_position(nearest_fruit)
        if fruit_pos then
            local speed = 3.0
            transform.move_toward(agent_id, fruit_pos.x, 0, fruit_pos.z, speed * dt)

            -- Eat fruit if close enough
            if nearest_dist < 0.5 then
                entities.destroy(nearest_fruit)
                table.remove(state.fruits, nearest_idx)
            end
        end
    end
end

function OnBeforeUnload(world, state)
    state.unloading = true
end

-- Meteor Shower game mode
-- Demonstrates damage zones system by spawning agents and bombarding them with meteor impacts
-- Agents gradually lose HP until they all die

local GRID_SIZE = 20
local AGENT_COUNT = 10
local AGENT_MAX_HEALTH = 100

-- Meteor settings
local METEOR_INTERVAL = 0.3       -- Time between meteor spawns (seconds)
local METEOR_RADIUS = 1.5         -- Damage zone radius
local METEOR_DAMAGE = 15          -- Damage per hit
local METEOR_LIFETIME = 0.5       -- How long the damage zone persists
local METEORS_PER_WAVE = 3        -- Number of meteors per spawn interval

function OnInit(world, state)
    state.initialized = true
    state.agents = {}
    state.meteors = {}
    state.meteor_timer = 0
    state.total_deaths = 0
    state.game_over = false
    state.elapsed_time = 0

    -- Spawn agents at random positions
    log.info("Meteor Shower: Spawning " .. AGENT_COUNT .. " agents...")
    for i = 1, AGENT_COUNT do
        local x = math.random() * GRID_SIZE - GRID_SIZE / 2
        local z = math.random() * GRID_SIZE - GRID_SIZE / 2
        local id = entities.create(x, 0, z)
        if id then
            -- Add health component to make agent damageable
            health.add(id, AGENT_MAX_HEALTH)
            entities.add_script(id, "meteor_victim")
            table.insert(state.agents, {
                id = id,
                spawn_x = x,
                spawn_z = z,
                alive = true
            })
        end
    end
    log.info("Meteor Shower: Spawned " .. #state.agents .. " agents with " .. AGENT_MAX_HEALTH .. " HP each")
end

function OnLoad(world, state)
    state.loaded = true
    log.info("Meteor Shower: Game loaded, let the bombardment begin!")
end

function OnTick(world, state, dt)
    -- Skip updates until fully loaded (entities may not exist yet during loading)
    if not state.loaded then
        return
    end

    if state.game_over then
        return
    end

    state.elapsed_time = state.elapsed_time + dt

    -- Update meteor spawn timer
    state.meteor_timer = state.meteor_timer + dt
    if state.meteor_timer >= METEOR_INTERVAL then
        state.meteor_timer = 0

        -- Spawn multiple meteors per wave
        for i = 1, METEORS_PER_WAVE do
            spawn_meteor(state)
        end
    end

    -- Check agent health status and remove dead agents
    local alive_count = 0
    for _, agent in ipairs(state.agents) do
        if agent.alive then
            if health.is_dead(agent.id) then
                agent.alive = false
                state.total_deaths = state.total_deaths + 1
                log.info("Meteor Shower: Agent " .. agent.id .. " has perished! Deaths: " .. state.total_deaths .. "/" .. AGENT_COUNT)
            else
                alive_count = alive_count + 1

                -- Make agents wander randomly to make it more interesting
                wander_agent(agent, dt)
            end
        end
    end

    -- Clean up expired meteors
    cleanup_meteors(state)

    -- Check for game over
    if alive_count == 0 then
        state.game_over = true
        log.info("Meteor Shower: Game Over! All agents eliminated in " .. string.format("%.1f", state.elapsed_time) .. " seconds")
        log.info("Meteor Shower: Total meteors spawned: " .. #state.meteors)
    end
end

function spawn_meteor(state)
    -- Random position within the grid (target the area where agents might be)
    local x = math.random() * GRID_SIZE - GRID_SIZE / 2
    local z = math.random() * GRID_SIZE - GRID_SIZE / 2

    -- Create damage zone at impact point
    local meteor_id = damagezone.create(x, 0, z, METEOR_RADIUS, METEOR_DAMAGE)
    if meteor_id and meteor_id > 0 then
        -- Set tick interval to 0 for instant damage (one-shot)
        damagezone.set_tick_interval(meteor_id, 0)

        -- Set TTL so the meteor disappears after a short time
        ttl.set(meteor_id, METEOR_LIFETIME)

        table.insert(state.meteors, {
            id = meteor_id,
            x = x,
            z = z,
            spawn_time = state.elapsed_time
        })
    end
end

function cleanup_meteors(state)
    -- Remove meteors that have expired (TTL system handles destruction)
    local live_meteors = {}
    for _, meteor in ipairs(state.meteors) do
        -- Check if meteor still exists by checking if TTL has expired
        -- (The entity will be destroyed by TTL system automatically)
        local remaining = ttl.get(meteor.id)
        if remaining and remaining > 0 then
            table.insert(live_meteors, meteor)
        end
    end
    state.meteors = live_meteors
end

function wander_agent(agent, dt)
    -- Simple random walk for agents
    if math.random() < 0.1 then  -- 10% chance to change direction each frame
        agent.target_x = agent.spawn_x + (math.random() * 6 - 3)
        agent.target_z = agent.spawn_z + (math.random() * 6 - 3)

        -- Clamp to grid
        agent.target_x = math.max(-GRID_SIZE/2, math.min(GRID_SIZE/2, agent.target_x))
        agent.target_z = math.max(-GRID_SIZE/2, math.min(GRID_SIZE/2, agent.target_z))
    end

    if agent.target_x and agent.target_z then
        local speed = 2.0
        transform.move_toward(agent.id, agent.target_x, 0, agent.target_z, speed * dt)
    end
end

function OnBeforeUnload(world, state)
    state.unloading = true
    log.info("Meteor Shower: Unloading... Final stats: " .. state.total_deaths .. " deaths in " .. string.format("%.1f", state.elapsed_time) .. "s")

    -- Clean up remaining meteors
    for _, meteor in ipairs(state.meteors) do
        damagezone.destroy(meteor.id)
    end

    -- Clean up remaining agents
    for _, agent in ipairs(state.agents) do
        if agent.alive then
            entities.destroy(agent.id)
        end
    end
end

-- Test script for fruit eater agent entities
-- Used by LuaECS test suite

function OnInit(entity, state)
    state.score = 0
    state.move_speed = 3.0
end

function OnTick(entity, state, dt)
    local pos = transform.get_position(entity)
    if not pos then return end

    -- Find nearest fruit
    local nearest_dist = 999999
    local nearest_pos = nil

    -- Query nearby entities with "fruit" script
    local nearby = spatial.query_near(entity, 20)
    for _, other_id in ipairs(nearby) do
        if other_id ~= entity and entities.has_script(other_id, "fruit") then
            local other_pos = transform.get_position(other_id)
            if other_pos then
                local dx = other_pos.x - pos.x
                local dz = other_pos.z - pos.z
                local dist = math.sqrt(dx * dx + dz * dz)
                if dist < nearest_dist then
                    nearest_dist = dist
                    nearest_pos = other_pos
                end
            end
        end
    end

    -- Move toward nearest fruit
    if nearest_pos then
        local dx = nearest_pos.x - pos.x
        local dz = nearest_pos.z - pos.z
        local dist = math.sqrt(dx * dx + dz * dz)

        if dist > 0.5 then
            -- Move toward fruit
            local move_dist = math.min(state.move_speed * dt, dist)
            local new_x = pos.x + (dx / dist) * move_dist
            local new_z = pos.z + (dz / dist) * move_dist
            transform.set_position(entity, new_x, pos.y, new_z)
        else
            -- Close enough - eat the fruit
            for _, other_id in ipairs(nearby) do
                if entities.has_script(other_id, "fruit") then
                    local other_pos = transform.get_position(other_id)
                    if other_pos then
                        local fdx = other_pos.x - pos.x
                        local fdz = other_pos.z - pos.z
                        local fdist = math.sqrt(fdx * fdx + fdz * fdz)
                        if fdist < 0.5 then
                            entities.destroy(other_id)
                            state.score = state.score + 1
                            break
                        end
                    end
                end
            end
        end
    end
end

function OnDestroy(entity, state)
    -- Cleanup when entity is destroyed
end

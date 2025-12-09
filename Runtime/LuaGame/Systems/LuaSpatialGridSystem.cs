namespace LuaGame.Systems
{
	using LuaECS.Components;
	using LuaECS.Core;
	using LuaECS.Systems;
	using LuaGame.Components;
	using LuaGame.Core;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Transforms;

	/// <summary>
	/// Rebuilds the spatial grid each frame from damageable entities.
	/// Runs before damage systems to ensure grid is up-to-date.
	/// </summary>
	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateBefore(typeof(LuaHealthSystem))]
	public partial class LuaSpatialGridSystem : SystemBase
	{
		EntityQuery m_DamageableQuery;

		protected override void OnCreate()
		{
			// Query for damageable entities with transform and Lua entity ID
			m_DamageableQuery = GetEntityQuery(
				ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<LuaDamageableTag>(),
				ComponentType.ReadOnly<LuaEntityId>()
			);
		}

		protected override void OnStartRunning()
		{
			if (!LuaSpatialGrid.IsCreated)
			{
				LuaSpatialGrid.Initialize(cellSize: 4f, initialCapacity: 1024);
			}
		}

		protected override void OnUpdate()
		{
			if (!LuaSpatialGrid.IsCreated)
				return;

			// Clear and rebuild the grid
			LuaSpatialGrid.Clear();

			// Get all damageable entities
			var entities = m_DamageableQuery.ToEntityArray(Allocator.Temp);
			var transforms = m_DamageableQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
			var luaIds = m_DamageableQuery.ToComponentDataArray<LuaEntityId>(Allocator.Temp);

			for (var i = 0; i < entities.Length; i++)
			{
				var entity = entities[i];
				var position = transforms[i].Position;
				var entityId = luaIds[i].value;

				// Default radius; could be extended to use a component
				var radius = 0.5f;

				// Check if entity has a bounding radius component
				if (SystemAPI.HasComponent<LuaDamageZone>(entity))
				{
					var zone = SystemAPI.GetComponent<LuaDamageZone>(entity);
					radius = zone.radius;
				}

				LuaSpatialGrid.Add(entity, entityId, position, radius);
			}

			entities.Dispose();
			transforms.Dispose();
			luaIds.Dispose();
		}

		protected override void OnDestroy()
		{
			LuaSpatialGrid.Dispose();
		}
	}
}

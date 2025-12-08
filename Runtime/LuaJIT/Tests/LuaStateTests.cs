namespace LuaJIT.Tests
{
	using LuaNET.LuaJIT;
	using NUnit.Framework;

	[TestFixture]
	public class LuaStateTests
	{
		lua_State m_L;

		[SetUp]
		public void SetUp()
		{
			m_L = Lua.luaL_newstate();
		}

		[TearDown]
		public void TearDown()
		{
			if (m_L.IsNotNull)
				Lua.lua_close(m_L);
		}

		[Test]
		public void luaL_newstate_CreatesValidState()
		{
			Assert.IsTrue(m_L.IsNotNull);
		}

		[Test]
		public void luaL_openlibs_LoadsStandardLibraries()
		{
			Lua.luaL_openlibs(m_L);

			// Check that math library is loaded
			Lua.lua_getglobal(m_L, "math");
			Assert.AreEqual(1, Lua.lua_istable(m_L, -1));
		}

		[Test]
		public void lua_pushstring_PushesString()
		{
			Lua.lua_pushstring(m_L, "hello");

			Assert.AreEqual(1, Lua.lua_gettop(m_L));
			Assert.AreEqual("hello", Lua.lua_tostring(m_L, -1));
		}

		[Test]
		public void lua_pushinteger_PushesInteger()
		{
			Lua.lua_pushinteger(m_L, 42);

			Assert.AreEqual(1, Lua.lua_gettop(m_L));
			Assert.AreEqual(42, Lua.lua_tointeger(m_L, -1));
		}

		[Test]
		public void lua_pushnumber_PushesNumber()
		{
			Lua.lua_pushnumber(m_L, 3.14159);

			Assert.AreEqual(1, Lua.lua_gettop(m_L));
			Assert.AreEqual(3.14159, Lua.lua_tonumber(m_L, -1), 0.00001);
		}

		[Test]
		public void lua_pushboolean_PushesBoolean()
		{
			Lua.lua_pushboolean(m_L, 1);

			Assert.AreEqual(1, Lua.lua_gettop(m_L));
			Assert.AreEqual(1, Lua.lua_toboolean(m_L, -1));
		}

		[Test]
		public void lua_pushnil_PushesNil()
		{
			Lua.lua_pushnil(m_L);

			Assert.AreEqual(1, Lua.lua_gettop(m_L));
			Assert.AreEqual(1, Lua.lua_isnil(m_L, -1));
		}

		[Test]
		public void lua_newtable_CreatesTable()
		{
			Lua.lua_newtable(m_L);

			Assert.AreEqual(1, Lua.lua_gettop(m_L));
			Assert.AreEqual(1, Lua.lua_istable(m_L, -1));
		}

		[Test]
		public void lua_setfield_SetsTableField()
		{
			Lua.lua_newtable(m_L);
			Lua.lua_pushinteger(m_L, 100);
			Lua.lua_setfield(m_L, -2, "value");

			Lua.lua_getfield(m_L, -1, "value");
			Assert.AreEqual(100, Lua.lua_tointeger(m_L, -1));
		}

		[Test]
		public void lua_pop_RemovesFromStack()
		{
			Lua.lua_pushinteger(m_L, 1);
			Lua.lua_pushinteger(m_L, 2);
			Lua.lua_pushinteger(m_L, 3);

			Assert.AreEqual(3, Lua.lua_gettop(m_L));

			Lua.lua_pop(m_L, 2);

			Assert.AreEqual(1, Lua.lua_gettop(m_L));
			Assert.AreEqual(1, Lua.lua_tointeger(m_L, -1));
		}

		[Test]
		public void luaL_dostring_ExecutesCode()
		{
			Lua.luaL_openlibs(m_L);

			var result = Lua.luaL_dostring(m_L, "return 1 + 2");

			Assert.AreEqual(Lua.LUA_OK, result);
			Assert.AreEqual(3, Lua.lua_tointeger(m_L, -1));
		}

		[Test]
		public void luaL_dostring_ReturnsErrorOnInvalidCode()
		{
			Lua.luaL_openlibs(m_L);

			var result = Lua.luaL_dostring(m_L, "this is not valid lua code !!!!");

			Assert.AreNotEqual(Lua.LUA_OK, result);
		}

		[Test]
		public void luaL_ref_CreatesReference()
		{
			Lua.lua_newtable(m_L);
			var refId = Lua.luaL_ref(m_L, Lua.LUA_REGISTRYINDEX);

			Assert.AreNotEqual(Lua.LUA_NOREF, refId);
			Assert.AreEqual(0, Lua.lua_gettop(m_L)); // Table removed from stack
		}

		[Test]
		public void lua_rawgeti_RetrievesReference()
		{
			Lua.lua_newtable(m_L);
			Lua.lua_pushinteger(m_L, 999);
			Lua.lua_setfield(m_L, -2, "test");
			var refId = Lua.luaL_ref(m_L, Lua.LUA_REGISTRYINDEX);

			Lua.lua_rawgeti(m_L, Lua.LUA_REGISTRYINDEX, refId);
			Lua.lua_getfield(m_L, -1, "test");

			Assert.AreEqual(999, Lua.lua_tointeger(m_L, -1));

			Lua.luaL_unref(m_L, Lua.LUA_REGISTRYINDEX, refId);
		}
	}
}

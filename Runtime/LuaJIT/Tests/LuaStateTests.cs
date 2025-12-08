namespace LuaJIT.Tests
{
	using LuaNET.LuaJIT;
	using NUnit.Framework;

	[TestFixture]
	public class LuaStateTests
	{
		lua_State L;

		[SetUp]
		public void SetUp()
		{
			L = Lua.luaL_newstate();
		}

		[TearDown]
		public void TearDown()
		{
			if (L.IsNotNull)
				Lua.lua_close(L);
		}

		[Test]
		public void luaL_newstate_CreatesValidState()
		{
			Assert.IsTrue(L.IsNotNull);
		}

		[Test]
		public void luaL_openlibs_LoadsStandardLibraries()
		{
			Lua.luaL_openlibs(L);

			// Check that math library is loaded
			Lua.lua_getglobal(L, "math");
			Assert.AreEqual(1, Lua.lua_istable(L, -1));
		}

		[Test]
		public void lua_pushstring_PushesString()
		{
			Lua.lua_pushstring(L, "hello");

			Assert.AreEqual(1, Lua.lua_gettop(L));
			Assert.AreEqual("hello", Lua.lua_tostring(L, -1));
		}

		[Test]
		public void lua_pushinteger_PushesInteger()
		{
			Lua.lua_pushinteger(L, 42);

			Assert.AreEqual(1, Lua.lua_gettop(L));
			Assert.AreEqual(42, Lua.lua_tointeger(L, -1));
		}

		[Test]
		public void lua_pushnumber_PushesNumber()
		{
			Lua.lua_pushnumber(L, 3.14159);

			Assert.AreEqual(1, Lua.lua_gettop(L));
			Assert.AreEqual(3.14159, Lua.lua_tonumber(L, -1), 0.00001);
		}

		[Test]
		public void lua_pushboolean_PushesBoolean()
		{
			Lua.lua_pushboolean(L, 1);

			Assert.AreEqual(1, Lua.lua_gettop(L));
			Assert.AreEqual(1, Lua.lua_toboolean(L, -1));
		}

		[Test]
		public void lua_pushnil_PushesNil()
		{
			Lua.lua_pushnil(L);

			Assert.AreEqual(1, Lua.lua_gettop(L));
			Assert.AreEqual(1, Lua.lua_isnil(L, -1));
		}

		[Test]
		public void lua_newtable_CreatesTable()
		{
			Lua.lua_newtable(L);

			Assert.AreEqual(1, Lua.lua_gettop(L));
			Assert.AreEqual(1, Lua.lua_istable(L, -1));
		}

		[Test]
		public void lua_setfield_SetsTableField()
		{
			Lua.lua_newtable(L);
			Lua.lua_pushinteger(L, 100);
			Lua.lua_setfield(L, -2, "value");

			Lua.lua_getfield(L, -1, "value");
			Assert.AreEqual(100, Lua.lua_tointeger(L, -1));
		}

		[Test]
		public void lua_pop_RemovesFromStack()
		{
			Lua.lua_pushinteger(L, 1);
			Lua.lua_pushinteger(L, 2);
			Lua.lua_pushinteger(L, 3);

			Assert.AreEqual(3, Lua.lua_gettop(L));

			Lua.lua_pop(L, 2);

			Assert.AreEqual(1, Lua.lua_gettop(L));
			Assert.AreEqual(1, Lua.lua_tointeger(L, -1));
		}

		[Test]
		public void luaL_dostring_ExecutesCode()
		{
			Lua.luaL_openlibs(L);

			var result = Lua.luaL_dostring(L, "return 1 + 2");

			Assert.AreEqual(Lua.LUA_OK, result);
			Assert.AreEqual(3, Lua.lua_tointeger(L, -1));
		}

		[Test]
		public void luaL_dostring_ReturnsErrorOnInvalidCode()
		{
			Lua.luaL_openlibs(L);

			var result = Lua.luaL_dostring(L, "this is not valid lua code !!!!");

			Assert.AreNotEqual(Lua.LUA_OK, result);
		}

		[Test]
		public void luaL_ref_CreatesReference()
		{
			Lua.lua_newtable(L);
			var refId = Lua.luaL_ref(L, Lua.LUA_REGISTRYINDEX);

			Assert.AreNotEqual(Lua.LUA_NOREF, refId);
			Assert.AreEqual(0, Lua.lua_gettop(L)); // Table removed from stack
		}

		[Test]
		public void lua_rawgeti_RetrievesReference()
		{
			Lua.lua_newtable(L);
			Lua.lua_pushinteger(L, 999);
			Lua.lua_setfield(L, -2, "test");
			var refId = Lua.luaL_ref(L, Lua.LUA_REGISTRYINDEX);

			Lua.lua_rawgeti(L, Lua.LUA_REGISTRYINDEX, refId);
			Lua.lua_getfield(L, -1, "test");

			Assert.AreEqual(999, Lua.lua_tointeger(L, -1));

			Lua.luaL_unref(L, Lua.LUA_REGISTRYINDEX, refId);
		}
	}
}

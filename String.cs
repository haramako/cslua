using System;
namespace TLua.LuaLib
{
	public class StdString
	{
		public static void find(LuaState L)
		{
			L.PushResult(new LuaValue(true));
		}

		public static void gsub(LuaState L)
		{
			L.PushResult(new LuaValue(""));
		}

		public static void rep(LuaState L)
		{
			L.PushResult(new LuaValue(true));
		}

		public static void Bind(LuaState L)
		{
			var mod = new Table();
			mod["find"] = new LuaValue(find);
			mod["gsub"] = new LuaValue(gsub);
			mod["rep"] = new LuaValue(rep);
			L.Env["string"] = new LuaValue(mod);
		}
	}
}

using System;

namespace TLua.LuaLib
{
	public class StdMath
	{
		public static void type(LuaState L)
		{
			var v = L.GetArg(0);
			switch (v.ValueType) {
			case ValueType.Integer:
				L.PushResult(new LuaValue("integer"));
				break;
			case ValueType.Float:
				L.PushResult(new LuaValue("float"));
				break;
			default:
				L.PushResult(LuaValue.Nil);
				break;
			}
		}

		public static void Bind(LuaState L)
		{
			var mod = new Table();
			mod["type"] = new LuaValue(type);
			L.Env["math"] = new LuaValue(mod);
		}
	}
}

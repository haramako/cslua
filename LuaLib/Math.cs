using System;

namespace TLua.LuaLib
{
	public class StdMath
	{
		public static void max(LuaState L)
		{
			if (L.GetArgNum() <= 0) return;
				
		   double result = L.GetArg(0).ConvertToFloat();
			for (int i = 1; i < L.GetArgNum(); i++) {
				var cur = L.GetArg(i).ConvertToFloat();
				if (cur > result) result = cur;
			}
			L.PushResult(new LuaValue(result));
		}

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
			mod["max"] = new LuaValue(max);
			mod["type"] = new LuaValue(type);
			L.Env["math"] = new LuaValue(mod);
		}
	}
}

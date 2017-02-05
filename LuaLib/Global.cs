using System;
namespace TLua.LuaLib
{
	public class Global
	{
		public static void assert(LuaState L)
		{
			for (int i = 0; i < L.GetArgNum(); i++) {
				var v = L.GetArg(i);
				if (!v.ConvertToBool()) {
					throw new LuaException("assert failed");
				}
				L.PushResult(v);
			}
		}

		public static void dump(LuaState L)
		{
			for (int i = 0; i < L.GetArgNum(); i++) {
				L.PushResult(L.GetArg(i));
			}
			Console.WriteLine(L.dump());
		}

		public static void next(LuaState L)
		{
			// TODO: 仕様に則して作るべし
			var tbl = L.GetArg(0).AsTable;
			var luaIndex = L.GetArg(1);
			var index = luaIndex.IsNil ? 0 : luaIndex.ConvertToInt();
			if (tbl.ArraySize == 0) {
				// DO NOTHING
			} else if (index - 1 < tbl.Size) {
				index++;
				L.PushResult(new LuaValue(index));
				L.PushResult(new LuaValue(tbl.GetByLuaIdx(index)));
			} else {
				// DO NOTHING
			}
		}

		public static void print(LuaState L)
		{
			for (int i = 0; i < L.GetArgNum(); i++) {
				if (i != 0) System.Console.Write(" ");
				System.Console.Write(L.GetArg(i).ToString());
			}
			System.Console.WriteLine("");
		}

		public static void select(LuaState L)
		{
			var first = L.GetArg(0);
			if (first.ValueType == ValueType.String) {
				if (first.AsString == "#") {
					L.PushResult(new LuaValue(L.GetArgNum() - 1));
				}
			} else if (first.ValueType == ValueType.Integer) {
				var argnum = L.GetArgNum() - 1;
				var start = first.ConvertToInt();
				if (start < 0) {
					start = argnum + start;
				} else {
					start = start - 1;
				}
				if (start < 0) start = 0;
				for (var i = start; i < argnum; i++) {
					L.PushResult(L.GetArg(i + 1));
				}
			} else {
				// DO NOTHING
			}
		}

		public static void trace(LuaState L)
		{
			L.EnableTrace = true;
		}

		public static void Bind(LuaState L)
		{
			L.Env["assert"] = new LuaValue(assert);
			L.Env["dump"] = new LuaValue(dump);
			L.Env["next"] = new LuaValue(next);
			L.Env["print"] = new LuaValue(print);
			L.Env["select"] = new LuaValue(select);
			L.Env["trace"] = new LuaValue(trace);
		}
	}
}

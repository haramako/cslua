using System;
namespace TLua.LuaLib
{
	public class StdTable
	{
		public static void pack(LuaState L)
		{
			var tbl = new Table();
			tbl.Resize(L.GetArgNum());
			for (int i = 0; i < L.GetArgNum(); i++) {
				tbl[i] = L.GetArg(i);
			}
			L.PushResult(new LuaValue(tbl));
		}

		public static void unpack(LuaState L)
		{
			var tbl = L.GetArg(0).AsTable;
			var range = tbl.GetRange(L.GetArg(1), L.GetArg(2));
			if (range.Valid) {
				for (int i = range.Start; i < range.End; i++) {
					L.PushResult(new LuaValue(tbl[i]));
				}
			}
		}

		public static void Bind(LuaState L)
		{
			var mod = new Table();
			mod["pack"] = new LuaValue(pack);
			mod["unpack"] = new LuaValue(unpack);
			L.Env["table"] = new LuaValue(mod);
		}
	}
}

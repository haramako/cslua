using System;
using System.Linq;

namespace TLua.LuaLib
{
	public class T
	{
		public static void listk(LuaState L)
		{
			var closure = L.GetArg(0).AsClosure;
			var tbl = new Table();
			tbl.GetRawArray().AddRange(closure.Func.Consts);
			L.PushResult(new LuaValue(tbl));
		}
		
		public static void listcode(LuaState L)
		{
			var closure = L.GetArg(0).AsClosure;
			var tbl = new Table();
			tbl.GetRawArray().AddRange(closure.Func.Codes.Select(x => new LuaValue(x)));
			L.PushResult(new LuaValue(tbl));
		}

		public static void Bind(LuaState L)
		{
			var mod = new Table();
			mod["listk"] = new LuaValue(listk);
			mod["listcode"] = new LuaValue(listcode);
			L.Env["T"] = new LuaValue(mod);
		}
	}
}

using System;
namespace TLua.LuaLib
{
	public class Global
	{
		public static void print(LuaState L)
		{
			for (int i = 0; i < L.GetArgNum(); i++) {
				if (i != 0) System.Console.Write(" ");
				System.Console.Write(L.GetArg(i).ToString());
			}
			System.Console.WriteLine("");
		}

		public static void assert(LuaState L)
		{
			for (int i = 0; i < L.GetArgNum(); i++) {
				if (!L.GetArg(i).ConvertToBool()) {
					throw new LuaException("assert failed");
				}
			}
		}

		public static void trace(LuaState L)
		{
			L.EnableTrace = true;
		}

		public static void Bind(LuaState L)
		{
			L.Env["print"] = new LuaValue(print);
			L.Env["assert"] = new LuaValue(assert);
			L.Env["trace"] = new LuaValue(trace);
		}
	}
}

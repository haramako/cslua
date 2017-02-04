using System;
namespace TLua
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

		public static void Bind(LuaState L)
		{
			L.Env["print"] = new LuaValue(print);
		}
	}
}

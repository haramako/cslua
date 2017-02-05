using System;

namespace TLua
{
	class MainClass
	{
		public static void Main(string[] args)
		{
			if (args.Length == 0) {
				args = new string[] { "/Users/makoto/cslua/fib.lua" };
			}

			bool trace = false;
			foreach (var arg in args) {
				if (arg == "-t") {
					trace = true;
					continue;
				}
				var lua = new LuaState();
				lua.LoadFile(arg);
				lua.EnableTrace = trace;
				lua.Run();
			}
		}
	}
}

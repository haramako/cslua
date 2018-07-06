using System;

namespace TLua
{
	class MainClass
	{
        public static int fib(int n)
        {
            if( n <= 1)
            {
                return 1;
            }else
            {
                return fib(n - 1) + fib(n -2); 
            }
        }
		public static void Main(string[] args)
		{
            //Console.WriteLine("" + fib(32));
            //return;
			if (args.Length == 0) {
				args = new string[] { "\\Work\\cslua\\fib.lua" };
			}

			bool trace = false;
			foreach (var arg in args) {
				if (arg == "-t") {
					trace = true;
					continue;
				}
				var lua = new LuaState();

                lua.Parse(arg);
                break;

				lua.LoadFile(arg);
				lua.EnableTrace = trace;
				lua.Run();
			}
            Console.WriteLine("END");
            Console.ReadKey();
		}
	}
}

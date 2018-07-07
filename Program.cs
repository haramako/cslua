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

#if false
            var ops = Enum.GetNames(typeof(TLua.OpCode));
            foreach( var op in ops)
            {
                var v = (OpCode)Enum.Parse(typeof(TLua.OpCode), op);
                var db = OpDatabase.Data[(int)v];
                Console.WriteLine("{0,8} {1,8} {2,8}", op, db.Name, db.Type);
                if( op.ToString() != db.Name)
                {
                    throw new Exception();
                }
            }
            Console.ReadKey();
            Environment.Exit(0);
#endif

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

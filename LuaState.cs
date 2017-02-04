using System;
using System.IO;
using System.Diagnostics;

namespace TLua
{
	public class LuaException : Exception
	{
		public LuaException(string msg) : base(msg)
		{
		}
	}

	public class LuaState
	{
		public LuaState()
		{
		}

		public void LoadFile(string filename)
		{
			var p = Process.Start("luac5.3", "-o /tmp/luac.out -l "+ filename );
			p.WaitForExit();
			if (p.ExitCode != 0) {
				throw new Exception("exit code not 0");
			}
			var s = System.IO.File.OpenRead("/tmp/luac.out");
			var chunk = new Chunk(s, filename);
			Console.WriteLine(chunk.Main.Dump());

		}
	}
}

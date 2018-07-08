using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace TLua
{
	public struct UpvalTag
	{
        public string Name;
		public byte InStack;
		public byte Index;
	}

	public sealed class Function
	{
		public string Filename;
		public string Name;
		public int LineStart;
		public int LineEnd;
		public int ParamNum;
		public bool HasVarArg;
		public int MaxStackSize;

		//public uint[] Codes;
        public List<uint> Codes = new List<uint>();
        public LuaValue[] Consts = new LuaValue[0];
		// public UpvalTag[] Upvals; TODO: あとで作成時用のFunctionと利用時のFunctionにわける
        public List<UpvalTag> Upvals;
		// public Function[] Protos;
        public List<Function> Protos = new List<Function>();
		public uint[] DebugInfos;

        internal List<Parsing.Parser.LocVar> LocalVars = new List<Parsing.Parser.LocVar>();

		// Root用のダミー関数
		public Function()
		{
            //Upvals = new UpvalTag[0];
            Upvals = new List<UpvalTag>();
		}

		public Function(ZIO z, string filename)
		{
			Filename = filename;
			Name = z.ReadString();
			LineStart = z.ReadSize();
			LineEnd = z.ReadSize();
			ParamNum = z.ReadByte();
			HasVarArg = (z.ReadByte() != 0);
			MaxStackSize = z.ReadByte();

            //Console.WriteLine("****FUNC {0} {1}", filename, Name);

			var size = z.ReadSize();
            Codes = new List<uint>(size);// new uint[size];
			for (var i = 0; i < size; i++) {
                //Codes[i] = (uint)z.ReadInt();
                Codes.Add((uint)z.ReadInt());
			}

			size = z.ReadSize();
			Consts = new LuaValue[size];
			for (var i = 0; i < size; i++) {
				var type = (LoadType)z.ReadByte();
				switch (type) {
				case LoadType.Nil:
					Consts[i].Clear();
					break;
				case LoadType.Boolean:
					Consts[i].AsBool = (z.ReadByte() != 0);
					break;
				case LoadType.NumFloat:
					Consts[i].AsFloat = z.ReadDouble();
					break;
				case LoadType.NumInt:
					Consts[i].AsInt = (int)z.ReadInt64();
					break;
				case LoadType.ShortString:
				case LoadType.LongString:
					Consts[i].AsString = z.ReadString();
					break;
				default:
					throw new Exception("not implemented");
				}
                //Console.WriteLine("{0}", Consts[i]);
			}

			size = z.ReadSize();
            //Console.WriteLine("*upval {0}", size);
            //Upvals = new UpvalTag[size];
            Upvals = new List<UpvalTag>(size);
            for (var i = 0; i < size; i++) {
                Upvals.Add(new UpvalTag { InStack = z.ReadByte(), Index = z.ReadByte() });
				//Upvals[i].InStack = z.ReadByte();
				//Upvals[i].Index = z.ReadByte();
			}

			size = z.ReadSize();
            //Console.WriteLine("*proto {0}", size);
            //Protos = new Function[size];
            Protos = new List<Function>(size);
			for (var i = 0; i < size; i++) {
                Protos.Add(new Function(z, filename));
				//Protos[i] = new Function(z, Filename);
			}

            // read lineinfo
            size = z.ReadSize();
            //Console.WriteLine("*lineinfo {0}", size);
            for (var i = 0; i < size; i++)
            {
                z.ReadByte();
            }

            size = z.ReadSize();
            //Console.WriteLine("*absline {0}", size);
            for (var i = 0; i < size; i++)
            {
                z.ReadSize();
                z.ReadSize();
            }

			// local vars
			size = z.ReadSize();
            //Console.WriteLine("*localvar {0}", size);
            // DebugInfos = new uint[size];
            for (var i = 0; i < size; i++) {
				var s = z.ReadString();
				z.ReadSize();
				z.ReadSize();
			}

			// upval names
			size = z.ReadSize();
            //Console.WriteLine("*upvalnames {0}", size);
            //DebugInfos = new uint[size];
            for (var i = 0; i < size; i++) {
				z.ReadString();
			}

		}

		public string Dump()
		{
			var sb = new StringBuilder();
			foreach (var code in Codes) {
				sb.AppendLine(Inst.Inspect(code));
			}
			return sb.ToString();
		}

        // TODO: 配列を毎回作り直しているので、最適化する
        public int AddConst(LuaValue s)
        {
            Consts = Consts.Concat(new LuaValue[] { s }).ToArray();
            return Consts.Length-1;
        }

	}

	public sealed class Chunk
	{
		public string Filename;
		public Function Main;

        public Chunk()
        {

        }

		public Chunk(Stream s, string filename)
		{
			var z = new ZIO(s);

			z.ReadBytes(6);

			z.ReadBytes(6);

			z.ReadBytes(5);

			z.ReadInt64();
			z.ReadNumber();
			z.ReadByte();

			Main = new Function(z, filename);
		}
	}
}

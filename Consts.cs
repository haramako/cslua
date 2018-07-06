using System;
using System.Runtime.CompilerServices;

namespace TLua
{
	public enum LoadType
	{
		Nil = 0,
		Boolean = 1,
		LIghtUserData = 2,
		Number = 3,
		String = 4,
		Table = 5,
		Function = 6,
		UserData = 7,

		ShortString = (int)String | (0 << 4),
		LongString = (int)String | (1 << 4),
		NumFloat = (int)Number | (0 << 4),
		NumInt = (int)Number | (1 << 4),
	}

	public class Inst
	{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OpCode OpCode(uint code)
		{
			return (OpCode)(code & 0x3f);
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int A(uint code)
		{
			return ((int)code >> 6) & 0xff;
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Ax(uint code)
		{
			return -1 - ((int)code >> 6);
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int B(uint code)
		{
			var r = ((int)code >> 23) & 0x1ff;
			if (r >= 0x100) {
				return 0xff - r;
			} else {
				return r;
			}
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Bx(uint code)
		{
			return (int)(code >> 14);
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int sBx(uint code)
		{
			return (int)(code >> 14) - 0x1ffff;
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int C(uint code)
		{
			var r = ((int)code >> 14) & 0x1ff;
			if (r >= 0x100) {
				return 0xff - r;
			} else {
				return r;
			}
		}

		public static string Inspect(uint code)
		{
			var opcode = Inst.OpCode(code);
			var info = OpDatabase.Data[(int)opcode];
			switch (info.Type) {
			case OpType.A:
				return string.Format("{0,-8} {1}", opcode.ToString(), A(code));
			case OpType.AB:
				return string.Format("{0,-8} {1} {2}", opcode.ToString(), A(code), B(code));
			case OpType.ABx:
				return string.Format("{0,-8} {1} {2}", opcode.ToString(), A(code), Bx(code));
			case OpType.AsBx:
				return string.Format("{0,-8} {1} {2}", opcode.ToString(), A(code), sBx(code));
			case OpType.ABC:
				return string.Format("{0,-8} {1} {2} {3}", opcode.ToString(), A(code), B(code), C(code));
			case OpType.AC:
				return string.Format("{0,-8} {1} {2}", opcode.ToString(), A(code), C(code));
			default:
				throw new Exception("not implemented");
			}
		}

        public static uint SetOpCode(uint inst, OpCode op)
        {
            return inst;
        }

        public static uint SetB(uint inst, int v)
        {
            return inst;
        }

        public static uint SetC(uint inst, int v)
        {
            return inst;
        }

        public static uint SetBx(uint inst, int v)
        {
            return inst;
        }
    }

    public class OpInfo
	{
		public string Name;
		public OpType Type;
		public OpInfo(string name, OpType type)
		{
			Name = name;
			Type = type;
		}
	}

	public enum OpType
	{
		A,
		AB,
		ABx,
		AsBx,
		ABC,
		AC,
		Ax,
	}

	public enum OpCode
	{
		MOVE = 0,
		LOADK = 1,
		LOADKX = 2,
		LOADBOOL = 3,
		LOADNIL = 4,
		GETUPVAL = 5,
		GETTABUP = 6,
		GETTABLE = 7,
		SETTABUP = 8,
		SETUPVAL = 9,
		SETTABLE = 10,
		NEWTABLE = 11,
		SELF = 12,
		ADD = 13,
		SUB = 14,
		MUL = 15,
		MOD = 16,
		POW = 17,
		DIV = 18,
		IDIV = 19,
		BAND = 20,
		BOR = 21,
		BXOR = 22,
		SHL = 23,
		SHR = 24,
		UNM = 25,
		BNOT = 26,
		NOT = 27,
		LEN = 28,
		CONCAT = 29,
        CLOSE = 47, // TODO: あとで番号を整える
		JMP = 30,
		EQ = 31,
		LT = 32,
		LE = 33,
		TEST = 34,
		TESTSET = 35,
		CALL = 36,
		TAILCALL = 37,
		RETURN = 38,
		FORLOOP = 39,
		FORPREP = 40,
		TFORCALL = 41,
		TFORLOOP = 42,
		SETLIST = 43,
		CLOSURE = 44,
		VARARG = 45,
		EXTRAARG = 46,

        // TODO: 追加分、あとで順番を整える
        PREPVARARG,
        FORPREP1,
        FORLOOP1,
    }

	public class OpDatabase
	{
		public static readonly OpInfo[] Data = new OpInfo[]{
			new OpInfo("MOVE", OpType.AB),
			new OpInfo("LOADK", OpType.ABx),
			new OpInfo("LOADKX", OpType.A),
			new OpInfo("LOADBOOL", OpType.ABC),
			new OpInfo("LOADNIL", OpType.AB),
			new OpInfo("GETUPVAL", OpType.AB),
			new OpInfo("GETTABUP", OpType.ABC),
			new OpInfo("GETTABLE", OpType.ABC),
			new OpInfo("SETTABUP", OpType.ABC),
			new OpInfo("SETUPVAL", OpType.AB),
			new OpInfo("SETTABLE", OpType.ABC),
			new OpInfo("NEWTABLE", OpType.AB),
			new OpInfo("SELF", OpType.ABC),
			new OpInfo("ADD", OpType.ABC),
			new OpInfo("SUB", OpType.ABC),
			new OpInfo("MUL", OpType.ABC),
			new OpInfo("MOD", OpType.ABC),
			new OpInfo("POW", OpType.ABC),
			new OpInfo("DIV", OpType.ABC),
			new OpInfo("IDIV", OpType.ABC),
			new OpInfo("BAND", OpType.ABC),
			new OpInfo("BOR", OpType.ABC),
			new OpInfo("BXOR", OpType.ABC),
			new OpInfo("SHL", OpType.ABC),
			new OpInfo("SHR", OpType.ABC),
			new OpInfo("UNM", OpType.AB),
			new OpInfo("BNOT", OpType.AB),
			new OpInfo("NOT", OpType.AB),
			new OpInfo("LEN", OpType.AB),
			new OpInfo("CONCAT", OpType.ABC),
			new OpInfo("JMP", OpType.AsBx),
			new OpInfo("EQ", OpType.ABC),
			new OpInfo("LT", OpType.ABC),
			new OpInfo("LE", OpType.ABC),
			new OpInfo("TEST", OpType.AC),
			new OpInfo("TESTSET", OpType.ABC),
			new OpInfo("CALL", OpType.ABC),
			new OpInfo("TAILCALL", OpType.ABC),
			new OpInfo("RETURN", OpType.AB),
			new OpInfo("FORLOOP", OpType.AsBx),
			new OpInfo("FORPREP", OpType.AsBx),
			new OpInfo("TFORCALL", OpType.AC),
			new OpInfo("TFORLOOP", OpType.AsBx),
			new OpInfo("SETLIST", OpType.ABC),
			new OpInfo("CLOSURE", OpType.ABx),
			new OpInfo("VARARG", OpType.AB),
			new OpInfo("EXTRAARG", OpType.Ax),
		};
	}
}

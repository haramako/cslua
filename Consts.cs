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
			return (OpCode)(code & 0x7f);
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int A(uint code)
		{
			return ((int)code >> 7) & 0xff;
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Ax(uint code)
		{
			return -1 - ((int)code >> 7);
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int B(uint code)
		{
			return ((int)code >> 16) & 0xff;
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int Bx(uint code)
		{
			return (int)(code >> 15);
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int sBx(uint code)
		{
			return (int)(code >> 15) - Parsing.Code.OffsetSbx;
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static int C(uint code)
		{
			return ((int)code >> 24) & 0x0ff;
		}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool K(uint code)
        {
            return ((code >> 15) & 1) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int sJ(uint code)
        {
            return (int)(code >> 8) - Parsing.Code.OffsetSj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool M(uint code)
        {
            return ((code >> 7) & 1) != 0;
        }

        public static uint CreateAx(OpCode op, int ax)
        {
            return (uint)ax << 7 | (uint)op;
        }

        public static uint CreateABCk(OpCode op, int a, int b, int c, bool k)
        {
            return (uint)c << 24 | (uint)b << 16 | (uint)(k ? 1 : 0) << 8 | (uint)a << 7 | (uint)op;
        }

        public static uint CreateABx(OpCode op, int a, int b)
        {
            return (uint)(b - Parsing.Code.OffsetSbx) << 16 | (uint)a << 7 | (uint)op;
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

        public static uint SetA(uint inst, int v)
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

        public static uint SetSj(uint inst, int v)
        {
            return inst;
        }

        public static uint SetM(uint inst, bool v)
        {
            return inst;
        }

        public static uint SetK(uint inst, bool v)
        {
            return inst;
        }

        public static bool IsTMode(OpCode op)
        {
            return (OpDatabase.Data[(int)op].Mode & OpMode.T) != 0; // TODO: testTMode
        }
    }

    public class OpInfo
	{
		public string Name;
		public OpType Type;
        public OpMode Mode;
		public OpInfo(string name, OpType type, OpMode mode = OpMode.None)
		{
			Name = name;
			Type = type;
            Mode = mode;
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
        J,
	}

    [Flags]
    public enum OpMode
    {
        None,
        T,
    }

	public enum OpCode
	{
		MOVE,
        LOADI,
		LOADF,
        LOADK,
        LOADKX,
		LOADBOOL,
		LOADNIL,
		GETUPVAL,
        SETUPVAL,
        GETTABUP,
		GETTABLE,
        GETI,
        GETFIELD,
		SETTABUP,
		SETTABLE,
        SETI,
        SETFIELD,
		NEWTABLE,
		SELF,

        ADDI,
        SUBI,
        MULI,
        MODI,
        POWI,
        DIVI,
        IDIVI,

        BANDK,
        BORK,
        BXORK,

        SHRI,
        SHLI,

        ADD,
		SUB,
		MUL,
		MOD,
		POW,
		DIV,
		IDIV,
		BAND,
		BOR,
		BXOR,
		SHL,
		SHR,
		UNM,
		BNOT,
		NOT,
		LEN,

		CONCAT,

        CLOSE,
		JMP,
		EQ,
		LT,
		LE,

        EQK,
        EQI,
        LTI,
        LEI,
        GTI,
        GEI,

		TEST,
		TESTSET,

        UNDEF,
        ISDEF,

		CALL,
		TAILCALL,

		RETURN,
		RETURN0,
        RETURN1,

        FORLOOP1,
        FORPREP1,

        FORLOOP,
		FORPREP,

		TFORCALL,
		TFORLOOP,
		SETLIST,
		CLOSURE,
		VARARG,
        PREPVARARG,
        EXTRAARG,
    }

    public class OpDatabase
	{
		public static readonly OpInfo[] Data = new OpInfo[]{
			new OpInfo("MOVE", OpType.AB),
            new OpInfo("LOADI", OpType.AsBx),
            new OpInfo("LOADF", OpType.AsBx),
            new OpInfo("LOADK", OpType.ABx),
			new OpInfo("LOADKX", OpType.A),
			new OpInfo("LOADBOOL", OpType.ABC),
			new OpInfo("LOADNIL", OpType.AB),
			new OpInfo("GETUPVAL", OpType.AB),
            new OpInfo("SETUPVAL", OpType.AB),
            new OpInfo("GETTABUP", OpType.ABC),
			new OpInfo("GETTABLE", OpType.ABC),
            new OpInfo("GETI", OpType.ABC),
            new OpInfo("GETFIELD", OpType.ABC),
            new OpInfo("SETTABUP", OpType.ABC),
			new OpInfo("SETTABLE", OpType.ABC),
            new OpInfo("SETI", OpType.ABC),
            new OpInfo("SETFIELD", OpType.ABC),
            new OpInfo("NEWTABLE", OpType.AB),
			new OpInfo("SELF", OpType.ABC),
            new OpInfo("ADDI", OpType.ABC),
            new OpInfo("SUBI", OpType.ABC),
            new OpInfo("MULI", OpType.ABC),
            new OpInfo("MODI", OpType.ABC),
            new OpInfo("POWI", OpType.ABC),
            new OpInfo("DIVI", OpType.ABC),
            new OpInfo("IDIVI", OpType.ABC),
            new OpInfo("BANDK", OpType.ABC),
            new OpInfo("BORK", OpType.ABC),
            new OpInfo("BXORK", OpType.ABC),
            new OpInfo("SHRI", OpType.ABC),
            new OpInfo("SHLI", OpType.ABC),
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
            new OpInfo("CLOSE", OpType.ABC),
            new OpInfo("JMP", OpType.AsBx),
			new OpInfo("EQ", OpType.ABC, OpMode.T),
			new OpInfo("LT", OpType.ABC, OpMode.T),
			new OpInfo("LE", OpType.ABC, OpMode.T),
            new OpInfo("EQK", OpType.AB, OpMode.T),
            new OpInfo("EQI", OpType.AsBx, OpMode.T),
            new OpInfo("LTI", OpType.AsBx, OpMode.T),
            new OpInfo("LEI", OpType.AsBx, OpMode.T),
            new OpInfo("GTI", OpType.AsBx, OpMode.T),
            new OpInfo("GEI", OpType.AsBx, OpMode.T),
            new OpInfo("TEST", OpType.AC, OpMode.T),
			new OpInfo("TESTSET", OpType.ABC, OpMode.T),
			new OpInfo("UNDEF", OpType.AB),
            new OpInfo("ISDEF", OpType.ABC),
            new OpInfo("CALL", OpType.ABC),
            new OpInfo("TAILCALL", OpType.ABC),
			new OpInfo("RETURN", OpType.ABC),
            new OpInfo("RETURN0", OpType.A),
            new OpInfo("RETURN1", OpType.A),
            new OpInfo("FORLOOP1", OpType.ABx),
            new OpInfo("FORPREP1", OpType.AsBx),
            new OpInfo("FORLOOP", OpType.AsBx),
			new OpInfo("FORPREP", OpType.AsBx),
			new OpInfo("TFORCALL", OpType.AC),
			new OpInfo("TFORLOOP", OpType.AsBx),
			new OpInfo("SETLIST", OpType.ABC),
			new OpInfo("CLOSURE", OpType.ABx),
			new OpInfo("VARARG", OpType.AB),
            new OpInfo("PREPVARARG", OpType.A),
            new OpInfo("EXTRAARG", OpType.Ax),
		};
	}
}

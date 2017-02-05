using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace TLua
{
	public class LuaException : Exception
	{
		public LuaException(string msg) : base(msg)
		{
		}
	}

	public sealed class CallInfo
	{
		public int Func;
		public int Result;
		public int Base;
		public int SavedPc;
		public int Wanted;
		public CallInfo Prev;
	}

	public class Upval
	{
		public LuaValue Val;
		public int Index;
		public bool IsOpen;
	}

	public sealed class Closure {
		public Function Func;
		public Upval[] Upvals;
		public Closure(Function func)
		{
			Func = func;
			Upvals = new Upval[Func.Upvals.Length];
		}
	}

	public delegate void LuaApi(LuaState L);

	public class LuaState
	{
		LuaValue[] stack_;
		int pc_;
		int top_;
		int base_;
		int apiArgNum_; // APIでのみ使用される,引数の数
		CallInfo ci_;
		Closure cl_;
		Function func_;
		List<Upval> openUpvals_;

		Table env_;

		public bool EnableTrace;

		public Table Env {
			get {
				return env_;
			}
		}

		public LuaState()
		{
			stack_ = new LuaValue[10000];
			pc_ = 0;
			top_ = 0;
			openUpvals_ = new List<Upval>();
			env_ = new Table();

			LuaLib.Global.Bind(this);
			LuaLib.T.Bind(this);
			LuaLib.StdMath.Bind(this);
			LuaLib.StdString.Bind(this);
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

			var rootCi = new CallInfo() { 
				Func = 0, 
				Result = 0, 
				Base = 0, 
				Prev = null,
				Wanted = 1, 
			};
			var cl = new Closure(chunk.Main);
			cl.Upvals[0] = new Upval() { Val = new LuaValue(env_), IsOpen = false };
			ci_ = new CallInfo() {
				Func = 2,
				Result = 2,
				Base = 3,
				Prev = rootCi,
				Wanted = 0,
				SavedPc = -1,
			};

			stack_[0] = new LuaValue(new Closure(new Function()));
			stack_[1] = new LuaValue(env_);
			stack_[2] = new LuaValue(cl);
			cl_ = cl;
			func_ = cl.Func;
			pc_ = 0;
			base_ = 3;
			top_ = 3;

		}

		//===========================================
		// DEBUGビルド時のみ有効なチェック関数
		//===========================================

		[Conditional("DEBUG")]
		void trace(params object[] args)
		{
			if (EnableTrace) {
				Console.Write("TRACE: ");
				foreach (var arg in args) {
					Console.Write(arg);
					Console.Write(", ");
				}
				Console.WriteLine("");
			}
		}

		[Conditional("DEBUG")]
		void check(bool b)
		{
			if (!b) {
				throw new Exception("check failed!");
			}
		}

		string dump()
		{
			var sb = new StringBuilder();
			sb.AppendLine("********DUMP********");
			sb.AppendFormat("PC: {0}\n", pc_);
			sb.AppendLine("********STACK********");
			for (int i = 0; i < base_ + func_.MaxStackSize; i++) {
				string mark = "";
				if (i == base_) {
					mark = "base->";
				} else if (i == ci_.Func) {
					mark = "func->";
				} else if (i == top_) {
					mark = " top->";
				}
				sb.AppendFormat("{0,-6} {1,4} {2}\n", mark, i, stack_[i].ToString());
			}
			return sb.ToString();
		}

		//===========================================
		// スタック操作
		//===========================================

		public void SetStack(int idx, ref LuaValue val)
		{
			stack_[idx] = val;
		}

		public void GetStack(int idx, out LuaValue val)
		{
			val = stack_[idx];
		}

		public LuaValue GetStack(int idx)
		{
			return stack_[idx];
		}

		LuaValue[] temp = new LuaValue[16];

		void copyAndFill(int fromIdx, int fromLen, int toIdx, int toLen)
		{
			if (fromIdx != toIdx) {
				var len = Math.Min(fromLen, toLen);
				Array.Copy(stack_, fromIdx, stack_, toIdx, len);
			}
			for (int n = fromLen; n < toLen; n++) {
				stack_[toIdx + n].Clear();
			}
		}

		void clear(int idx, int len)
		{
			for (int n = 0; n < len; n++) {
				stack_[idx + n].Clear();
			}
		}

		LuaValue r(int idx)
		{
			return stack_[base_ + idx];
		}

		LuaValue rk(int idx)
		{
			if (idx >= 0) {
				return stack_[base_ + idx];
			} else {
				return func_.Consts[-1 - idx];
			}
		}

		LuaValue kst(int idx)
		{
			return func_.Consts[idx];
		}

		LuaValue rb(uint code)
		{
			return r(Inst.B(code));
		}

		LuaValue rc(uint code)
		{
			return r(Inst.C(code));
		}

		LuaValue rkb(uint code)
		{
			return rk(Inst.B(code));
		}

		LuaValue rkc(uint code)
		{
			return rk(Inst.C(code));
		}

		//===========================================
		// Closure/Upval の操作
		//===========================================

		Closure newClosure(Function func, int baseIdx, Closure enc)
		{
			var cl = new Closure(func);
			for (int i = 0; i < func.Upvals.Length; i++) {
				var u = func.Upvals[i];
				if (u.InStack != 0) {
					cl.Upvals[i] = findUpval(baseIdx + u.Index);
				} else {
					cl.Upvals[i] = enc.Upvals[i];
				}
				trace("make upval", i, u.InStack, u.Index);
			}
			trace("open upvals", openUpvals_);
			return cl;
		}

		Upval findUpval(int idx)
		{
			for (int i = 0; i < openUpvals_.Count; i++) {
				if (openUpvals_[i].Index == idx) {
					return openUpvals_[i];
				}
			}
			var newUpval = new Upval() { Index = idx, IsOpen = true };
			openUpvals_.Add(newUpval);
			return newUpval;
		}

		LuaValue getUpval(int upvalIdx)
		{
			var cl = stack_[ci_.Func].AsClosure;
			trace("get upval", upvalIdx, cl.Upvals[upvalIdx]);
			check(upvalIdx >= 0 && upvalIdx < cl.Upvals.Length);
			var upval = cl.Upvals[upvalIdx];
			if (upval.IsOpen) {
				return stack_[upval.Index];
			} else {
				return upval.Val;
			}
		}

		void setUpval(int upvalIdx, LuaValue val)
		{
			var cl = stack_[ci_.Func].AsClosure;
			trace("set upval", upvalIdx, cl.Upvals[upvalIdx]);
			check(upvalIdx >= 0 && upvalIdx < cl.Upvals.Length);
			var upval = cl.Upvals[upvalIdx];
			if (upval.IsOpen) {
				stack_[upval.Index] = val;
			} else {
				upval.Val = val;
			}
		}

		// nargs 可変引数の数
		// wanted 想定される返り値の数
		void precall(bool isTailcall, int func, int nargs, int result, int wanted)
		{
			var f = stack_[func];
			var ftype = f.ValueType;
			switch (ftype) {
			case ValueType.Closure: {
					var cl = f.AsClosure;
					if (cl != null) {
						var fixedNum = cl.Func.ParamNum; // 固定引数の数
						if (cl.Func.HasVarArg) {
							var varargNum = nargs - fixedNum; // 今回の可変の数
							if (varargNum > 0) {
								Array.Copy(stack_, func + 1, temp, 0, fixedNum);
								copyAndFill(func + 1, varargNum, func + 1 + fixedNum, varargNum);
								Array.Copy(temp, 0, stack_, func + 1 + varargNum, fixedNum);
								clear(func + 1 + nargs, fixedNum - nargs);
								base_ = func + 1 + varargNum;
							} else {
								clear(func + 1 + nargs, fixedNum - nargs);
								base_ = func + 1;
							}
						} else {
							clear(func + 1 + nargs, fixedNum - nargs);
							base_ = func + 1;
						}

						//
						top_ = result;
						cl_ = cl;
						func_ = cl.Func;
						ci_ = new CallInfo() {
							Func = func,
							Result = result,
							Base = base_,
							Prev = ci_,
							Wanted = wanted,
							SavedPc = pc_,
						};
						pc_ = 0;
						return;
					}
				}
				break;
			case ValueType.LuaApi: {
					var api = f.AsLuaApi;
					if (api != null) {
						var oldBase_ = base_;
						base_ = func+1;
						apiArgNum_ = nargs;
						top_ = result;
						try {
							api(this);
						} finally {
							base_ = oldBase_;
						}
						if (wanted > 0) {
							clear(top_, top_ - (result + wanted));
						}
					}
				}
				break;
			default:
				throw new Exception("invalid function type " + f.GetType());
			}
		}

		void doReturn(int result, int num)
		{
			if (ci_.Prev == null) {
				throw new Exception("exit!");
			} else {
				if (num < 0) {
					num = top_ - result;
				}
				check(num >= 0);
				copyAndFill(result, num, ci_.Result, ci_.Wanted);
				top_ = ci_.Result + num;

				pc_ = ci_.SavedPc;
				ci_ = ci_.Prev;
				base_ = ci_.Base;
				func_ = stack_[ci_.Func].AsClosure.Func;
			}
		}

		//===========================================
		// Apiインターフェース の操作
		//===========================================

		public int GetArgNum()
		{
			return apiArgNum_;
		}

		public LuaValue GetArg(int idx)
		{
			if (idx < apiArgNum_) {
				return stack_[base_ + idx];
			} else {
				return LuaValue.Nil;
			}
		}

		public void PushResult(LuaValue val)
		{
			stack_[top_] = val;
			top_++;
		}

		//===========================================
		// Closure/Upval の操作
		//===========================================

		public void Run()
		{
			step(-1);
		}

		void step(int count)
		{
			try {
				int b = 0, c = 0, nargs = 0, wanted = 0;
				LuaValue v;
				StringBuilder sb = null;

				while (count != 0) {
					if (pc_ == -1) break;

					var i = func_.Codes[pc_];
					var opcode = Inst.OpCode(i);
					var a = Inst.A(i);
					var ra = base_ + a;
					if (EnableTrace) {
						Console.WriteLine(string.Format("* {0,3}[{1,3}] {2}", pc_, func_.DebugInfos[pc_], Inst.Inspect(i)));
					}
					pc_++;

					switch (opcode) {
					case OpCode.MOVE:
						stack_[ra] = rb(i);
						break;
					case OpCode.LOADK:
						stack_[ra] = kst(Inst.Bx(i));
						break;
					case OpCode.LOADKX:
						check(false);
						break;
					case OpCode.LOADBOOL:
						stack_[ra].AsBool = (Inst.B(i) != 0);
						if( Inst.C(i) != 0 ) pc_++;
						break;
					case OpCode.LOADNIL:
						b = Inst.B(i);
						for (int n = 0; n <= b; n++) {
							stack_[ra + n].Clear();
						}
						break;
					case OpCode.GETUPVAL:
						stack_[ra] = getUpval(Inst.B(i));
						break;
					case OpCode.GETTABUP:
						v = getUpval(Inst.B(i));
						stack_[ra] = v.AsTable[rkc(i)];
						break;
					case OpCode.GETTABLE:
						stack_[ra] = rkb(i).AsTable[rkc(i)];
						break;
					case OpCode.SETTABUP:
						v = getUpval(Inst.A(i));
						v.AsTable[rkb(i)] = rkc(i);
						break;
					case OpCode.SETUPVAL:
						setUpval(Inst.B(i), r(a));
						break;
					case OpCode.SETTABLE:
						r(a).AsTable[rkb(i)] = rkc(i);
						break;
					case OpCode.NEWTABLE:
						stack_[ra].AsTable = new Table();
						break;
					case OpCode.SELF: {
							var tbl = rb(i).AsTable;
							stack_[ra + 1].AsTable = tbl;
							stack_[ra] = tbl[rkc(i)];
						}
						break;
					case OpCode.ADD:
					case OpCode.SUB:
					case OpCode.MUL:
					case OpCode.MOD:
					case OpCode.POW:
					case OpCode.DIV:
					case OpCode.IDIV:
					case OpCode.BAND:
					case OpCode.BOR:
					case OpCode.BXOR:
					case OpCode.SHL:
					case OpCode.SHR:
						stack_[ra] = LuaValue.BinOp(opcode, rkb(i), rkc(i));
						break;
					case OpCode.UNM:
					case OpCode.BNOT:
					case OpCode.NOT:
						stack_[ra] = LuaValue.UnaryOp(opcode, rkb(i));
						break;
					case OpCode.LEN:
						stack_[ra].AsInt = rb(i).Len();
						break;
					case OpCode.CONCAT:
						b = Inst.B(i);
						c = Inst.C(i);
						if (sb == null) sb = new StringBuilder();
						for (int n = b; n <= c; n++) {
							sb.Append(stack_[base_ + n]);
						}
						stack_[ra].AsString = sb.ToString();
						sb.Clear();
						break;
					case OpCode.JMP:
						pc_ += Inst.sBx(i);
						break;
					case OpCode.EQ:
					case OpCode.LT:
					case OpCode.LE: {
							var cond = LuaValue.CompOp(opcode, rkb(i), rkc(i));
							if (cond != (Inst.A(i) != 0)) {
								pc_++;
							}
						}
						break;
					case OpCode.TEST:
						if (r(a).ConvertToBool() != (Inst.C(i) != 0)) {
							pc_++;
						}
						break;
					case OpCode.TESTSET:
						v = rb(i);
						if (v.ConvertToBool() == (Inst.C(i) != 0)) {
							stack_[ra] = v;
						}else{
							pc_++;
						}
						break;
					case OpCode.CALL:
						b = Inst.B(i);
						wanted = Inst.C(i) - 1;
						if (b == 0) {
							nargs = top_ - ra - 1;
						} else {
							nargs = b - 1;
						}
						
						precall(false, ra, nargs, ra, wanted);
						break;
					case OpCode.RETURN:
						b = Inst.B(i);
						doReturn(ra, b - 1);
						break;
					case OpCode.FORLOOP: {
							int stp = r(a + 2).AsInt;
							int cnt = r(a).AsInt + stp;
							int end = r(a + 1).AsInt;
							stack_[ra].AsInt = cnt;
							if ((stp > 0 && cnt <= end) || (stp < 0 && cnt >= end)) {
								pc_ += Inst.sBx(i);
								stack_[ra + 3].AsInt = cnt;
							}
						}
						break;
					case OpCode.FORPREP:
						stack_[ra].AsInt = r(a).AsInt - r(a + 2).AsInt;
						pc_ += Inst.sBx(i);
						break;
					case OpCode.TFORCALL:
						c = Inst.C(i);
						precall(false, ra, 2, ra + 3, c - 1);
						break;
					case OpCode.TFORLOOP: {
							var iter = r(a + 1);
							if (!iter.IsNil) {
								stack_[ra] = iter;
								pc_ += Inst.sBx(i);
							}
						}
						break;
					case OpCode.SETLIST: {
							var tbl = r(a).AsTable;
							c = Inst.C(i);
							var size = Inst.B(i) - 1;
							if (size == -1) {
								size = top_ - ra - 1;
							}
							tbl.Resize(c + size);
							for (int n = 0; n < size; n++) {
								tbl[c + n] = r(a+1+n);
							}
						}
						break;
					case OpCode.CLOSURE: {
							var proto = func_.Protos[Inst.Bx(i)];
							stack_[ra].AsClosure = newClosure(proto, base_, cl_);
						}
						break;
					case OpCode.VARARG: {
							b = Inst.B(i);
							var fromSize = base_ - ci_.Func - 1;
							int toSize;
							if (b == 0) {
								toSize = fromSize;
							} else {
								toSize = b - 1;
							}
							copyAndFill(ra, fromSize, ci_.Func + 1, toSize);
							top_ = ra + toSize;
						}
						break;
					default:
						throw new Exception("invalid opcode " + opcode);
					}
					count--;
				}
			} catch( Exception ) {
				Console.WriteLine(dump());
				throw;
			}
		}


	}
}

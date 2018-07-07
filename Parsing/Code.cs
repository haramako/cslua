using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TLua;

namespace TLua.Parsing
{
    using Proto = Function;

    internal static class Code
    {
        const int NO_REG = MaxArgA;
        internal const int MaxArgA = (1 << 8) - 1;
        internal const int MaxArgAx = (1 << 16) - 1;
        internal const int MaxArgB = (1 << 8) - 1;
        internal const int MaxArgBx = (1 << 16) - 1;
        internal const int MaxArgC = (1 << 8) - 1;
        internal const int MaxIndexRk = MaxArgB;
        internal const int OffsetSbx = MaxArgBx >> 1;
        internal const int OffsetSc = MaxArgC >> 1;


        /* semantic error */
        internal static void semerror(Lexer ls, string msg)
        {
            //ls.t.token = 0;  /* remove "near <token>" from final message */
            ls.syntaxerror(msg);
        }

        static bool hasjumps(ExpDesc e)
        {
            return e.t != e.f;
        }

        /*
        ** Check whether expression 'e' is a small literal string
        */
        internal static bool isKstr(FuncState fs, ExpDesc e)
        {
            return (e.k == ExpKind.Const) && !hasjumps(e) && (e.info <= MaxArgB) && fs.f.Consts[e.info].IsString;
        }

        /*
        ** Check whether expression 'e' is a literal integer.
        */
        internal static bool isKint(ExpDesc e)
        {
            return e.k == ExpKind.Int && !hasjumps(e);
        }


        /*
        ** Check whether expression 'e' is a literal integer in
        ** proper range to fit in register C
        */
        internal static bool isCint(ExpDesc e)
        {
            return isKint(e) && (e.ival <= MaxArgC);
        }

        /*
        ** converts an integer to a "floating point byte", represented as
        ** (eeeeexxx), where the real value is (1xxx) * 2^(eeeee - 1) if
        ** eeeee != 0 and (xxx) otherwise.
        */
        internal static int int2fb(uint x)
        {
            int e = 0;  /* exponent */
            if (x < 8) return (int)x;
            while (x >= (8 << 4))
            {  /* coarse steps */
                x = (x + 0xf) >> 4;  /* x = ceil(x / 16) */
                e += 4;
            }
            while (x >= (8 << 1))
            {  /* fine steps */
                x = (x + 1) >> 1;  /* x = ceil(x / 2) */
                e++;
            }
            return ((e + 1) << 3) | ((int)x - 8);
        }


        internal static void fixline(FuncState fs, int line)
        {
#if false
            Proto f = fs.f;
            if (f.lineinfo[fs.pc - 1] == ABSLINEINFO)
            {
                Lexer.assert(f.abslineinfo[fs.nabslineinfo - 1].pc == fs.pc - 1);
                f.abslineinfo[fs.nabslineinfo - 1].line = line;
                fs.previousline = line;
            }
            else
            {
                fs.previousline -= f.lineinfo[fs->pc - 1];  /* undo previous info. */
                savelineinfo(fs, f, fs.pc - 1, line);  /* redo it */
            }
#endif
        }


        /*
        ** Free register 'reg', if it is neither a constant index nor
        ** a local variable.
        )
        */
        static void freereg(FuncState fs, int reg)
        {
            if (reg >= fs.nactvar)
            {
                fs.freereg--;
                Lexer.assert(reg == fs.freereg);
            }
        }

        /*
        ** Free two registers in proper order
        */
        static void freeregs(FuncState fs, int r1, int r2)
        {
            if (r1 > r2)
            {
                freereg(fs, r1);
                freereg(fs, r2);
            }
            else
            {
                freereg(fs, r2);
                freereg(fs, r1);
            }
        }

        /*
        ** Ensure that expression 'e' is not a variable.
        ** (Expression still may have jump lists.)
        */
        internal static void dischargevars(FuncState fs, ExpDesc e)
        {
            switch (e.k)
            {
                case ExpKind.Local:
                    {  /* already in a register */
                        e.k = ExpKind.NonReloc;  /* becomes a non-relocatable value */
                        break;
                    }
                case ExpKind.Undef:
                    {  /* not a real expression */
                        semerror(fs.ls, "'undef' is not a value!!");
                        break;
                    }
                case ExpKind.Upval:
                    {  /* move value to some (pending) register */
                        e.info = codeABC(fs, OpCode.GETUPVAL, 0, e.info, 0);
                        e.k = ExpKind.Reloc;
                        break;
                    }
                case ExpKind.IndexUp:
                    {
                        e.info = codeABC(fs, OpCode.GETTABUP, 0, e.indT, e.indIdx);
                        e.k = ExpKind.Reloc;
                        break;
                    }
                case ExpKind.IndexInt:
                    {
                        freereg(fs, e.indT);
                        e.info = codeABC(fs, OpCode.GETI, 0, e.indT, e.indIdx);
                        e.k = ExpKind.Reloc;
                        break;
                    }
                case ExpKind.IndexString:
                    {
                        freereg(fs, e.indT);
                        e.info = codeABC(fs, OpCode.GETFIELD, 0, e.indT, e.indIdx);
                        e.k = ExpKind.Reloc;
                        break;
                    }
                case ExpKind.Indexed:
                    {
                        freeregs(fs, e.indT, e.indIdx);
                        e.info = codeABC(fs, OpCode.GETTABLE, 0, e.indT, e.indIdx);
                        e.k = ExpKind.Reloc;
                        break;
                    }
                case ExpKind.VarArg:
                case ExpKind.Call:
                    {
                        setoneret(fs, e);
                        break;
                    }
                default: break;  /* there is one value available (somewhere) */
            }
        }


        /*
        ** Ensures final expression result (which includes results from its
        ** jump ** lists) is in register 'reg'.
        ** If expression has jumps, need to patch these jumps either to
        ** its final position or to "load" instructions (for those tests
        ** that do not produce values).
        */
        static void exp2reg(FuncState fs, ExpDesc e, int reg)
        {
            discharge2reg(fs, e, reg);
            if (e.k == ExpKind.Jump)
            {
                /* expression itself is a test? */
                concat(fs, ref e.t, e.info);  /* put this jump in 't' list */
            }
            if (hasjumps(e))
            {
                int final;  /* position after whole expression */
                int p_f = Parser.NoJump;  /* position of an eventual LOAD false */
                int p_t = Parser.NoJump;  /* position of an eventual LOAD true */
                if (need_value(fs, e.t) || need_value(fs, e.f))
                {
                    int fj = (e.k == ExpKind.Jump) ? Parser.NoJump : jump(fs);
                    p_f = code_loadbool(fs, reg, 0, 1);
                    p_t = code_loadbool(fs, reg, 1, 0);
                    patchtohere(fs, fj);
                }
                final = getlabel(fs);
                patchlistaux(fs, e.f, final, reg, p_f);
                patchlistaux(fs, e.t, final, reg, p_t);
            }
            e.f = e.t = Parser.NoJump;
            e.info = reg;
            e.k = ExpKind.NonReloc;
        }

        /*
        ** check whether list has any jump that do not produce a value
        ** or produce an inverted value
        */
        static bool need_value(FuncState fs, int list)
        {
            for (; list != Parser.NoJump; list = getjump(fs, list))
            {
                uint i = fs.f.Codes[getjumpcontrol(fs, list)];
                if (Inst.OpCode(i) != OpCode.TESTSET) return true;
            }
            return false;  /* not found */
        }

        /*
        ** Gets the destination address of a jump instruction. Used to traverse
        ** a list of jumps.
        */
        static int getjump(FuncState fs, int pc)
        {
            int offset = Inst.sJ(fs.f.Codes[pc]);
            if (offset == Parser.NoJump)
            {
                /* point to itself represents end of list */
                return Parser.NoJump;  /* end of list */
            }
            else
            {
                return (pc + 1) + offset;  /* turn offset into absolute position */
            }
        }

        /*
        ** Returns the position of the instruction "controlling" a given
        ** jump (that is, its condition), or the jump itself if it is
        ** unconditional.
        */
        static int getjumpcontrol(FuncState fs, int pc)
        {
            uint pi = fs.f.Codes[pc];
            if (pc >= 1)
            {
                uint prev = fs.f.Codes[pc - 1];
                if (Inst.IsTMode(Inst.OpCode(prev)))
                {
                    return pc - 1;
                }
            }
            return pc;
        }

        // MEMO: getjumpcontrolと対で使う(ポインタがないため)
        static void setjumpcontrol(FuncState fs, int pc, uint inst)
        {
            fs.f.Codes[pc] = inst;
        }

        /*
        ** Ensures expression value is in register 'reg' (and therefore
        ** 'e' will become a non-relocatable expression).
        ** (Expression still may have jump lists.)
        */
        static void discharge2reg(FuncState fs, ExpDesc e, int reg)
        {
            dischargevars(fs, e);
            switch (e.k)
            {
                case ExpKind.Nil:
                    {
                        nil(fs, reg, 1);
                        break;
                    }
                case ExpKind.False:
                case ExpKind.True:
                    {
                        codeABC(fs, OpCode.LOADBOOL, reg, (e.k == ExpKind.True)?1:0, 0);
                        break;
                    }
                case ExpKind.Const:
                    {
                        codeK(fs, reg, e.info);
                        break;
                    }
                case ExpKind.Float:
                    {
                        floatnum(fs, reg, e.nval);
                        break;
                    }
                case ExpKind.Int:
                    {
                        integer(fs, reg, e.ival);
                        break;
                    }
                case ExpKind.Reloc:
                    {
                        uint pc = Parser.getinstruction(fs, e);
                        pc = Inst.SetA(pc, reg);  /* instruction will put result in 'reg' */
                        Parser.setinstruction(fs, e, pc);
                        break;
                    }
                case ExpKind.NonReloc:
                    {
                        if (reg != e.info)
                        {
                            codeABC(fs, OpCode.MOVE, reg, e.info, 0);
                        }
                        break;
                    }
                default:
                    {
                        Lexer.assert(e.k == ExpKind.Jump);
                        return;  /* nothing to do... */
                    }
            }
            e.info = reg;
            e.k = ExpKind.NonReloc;
        }

        /*
        ** Ensures final expression result is in some (any) register
        ** and return that register.
        */
        internal static int exp2anyreg(FuncState fs, ExpDesc e)
        {
            dischargevars(fs, e);
            if (e.k == ExpKind.NonReloc)
            {  /* expression already has a register? */
                if (!hasjumps(e))
                {
                    /* no jumps? */
                    return e.info;  /* result is already in a register */
                }
                if (e.info >= fs.nactvar)
                {
                    /* reg. is not a local? */
                    exp2reg(fs, e, e.info);  /* put final result in it */
                    return e.info;
                }
            }
            exp2nextreg(fs, e);  /* otherwise, use next available register */
            return e.info;
        }

        /*
        ** Create expression 't[k]'. 't' must have its final result already in a
        ** register or upvalue. Upvalues can only be indexed by literal strings.
        ** Keys can be literal strings in the constant table or arbitrary
        ** values in registers.
        */
        internal static void indexed(FuncState fs, ExpDesc t, ExpDesc k)
        {
            // Lexer.assert(!hasjumps(t) && (vkisinreg(t->k) || t->k == VUPVAL));
            if (t.k == ExpKind.Upval && !isKstr(fs, k))
            {
                /* upvalue indexed by non string? */
                exp2anyreg(fs, t);  /* put it in a register */
            }
            t.indT = t.info;  /* register or upvalue index */
            if (t.k == ExpKind.Upval)
            {
                t.indIdx = k.info;  /* literal string */
                t.k = ExpKind.IndexUp;
            }
            else if (isKstr(fs, k))
            {
                t.indIdx = k.info;  /* literal string */
                t.k = ExpKind.IndexString;
            }
            else if (isCint(k))
            {
                t.indIdx = k.ival;  /* integer constant in proper range */
                t.k = ExpKind.IndexInt;
            }
            else
            {
                t.indIdx = exp2anyreg(fs, k);  /* register */
                t.k = ExpKind.Indexed;
            }
        }

        internal static void setmultret(FuncState fs, ExpDesc e)
        {
            setreturns(fs, e, Parser.LUA_MULTRET);
        }

        /*
        ** Fix an expression to return the number of results 'nresults'.
        ** Either 'e' is a multi-ret expression (function call or vararg)
        ** or 'nresults' is LUA_MULTRET (as any expression can satisfy that).
        */
        internal static void setreturns(FuncState fs, ExpDesc e, int nresults)
        {
            uint pc = Parser.getinstruction(fs, e);
            if (e.k == ExpKind.Call)
            {
                /* expression is an open function call? */
                pc = Inst.SetC(pc, nresults + 1);
            }
            else if (e.k == ExpKind.VarArg)
            {
                pc = Inst.SetC(pc, nresults + 1);
                pc = Inst.SetA(pc, fs.freereg);
                reserveregs(fs, 1);
            }
            else
            {
                Lexer.assert(nresults == Parser.LUA_MULTRET);
            }
        }

        internal static void reserveregs(FuncState fs, int n)
        {
            checkstack(fs, n);
            fs.freereg += n;
        }

        /*
        ** Ensures final expression result is in some (any) register
        ** and return that register.
        */
        internal static void exp2nextreg(FuncState fs, ExpDesc e)
        {
            dischargevars(fs, e);
            freeexp(fs, e);
            reserveregs(fs, 1);
            exp2reg(fs, e, fs.freereg - 1);
        }

        /*
        ** Free register used by expression 'e' (if any)
        */
        static void freeexp(FuncState fs, ExpDesc e)
        {
            if (e.k == ExpKind.NonReloc)
            {
                freereg(fs, e.info);
            }
        }

        /*
        ** Ensures final expression result is either in a register
        ** or in an upvalue.
        */
        internal static void exp2anyregup(FuncState fs, ExpDesc e)
        {
            if (e.k != ExpKind.Upval || hasjumps(e))
            {
                exp2anyreg(fs, e);
            }
        }


        /*
        ** Ensures final expression result is either in a register
        ** or it is a constant.
        */
        internal static void exp2val(FuncState fs, ExpDesc e)
        {
            if (hasjumps(e))
            {
                exp2anyreg(fs, e);
            }
            else
            {
                dischargevars(fs, e);
            }
        }

        /*
        ** Generate code to store result of expression 'ex' into variable 'var'.
        */
        internal static void storevar(FuncState fs, ExpDesc var, ExpDesc ex)
        {
            switch (var.k)
            {
                case ExpKind.Local:
                    {
                        freeexp(fs, ex);
                        exp2reg(fs, ex, var.info);  /* compute 'ex' into proper place */
                        return;
                    }
                case ExpKind.Upval:
                    {
                        int e = exp2anyreg(fs, ex);
                        codeABC(fs, OpCode.SETUPVAL, e, var.info, 0);
                        break;
                    }
                case ExpKind.IndexUp:
                    {
                        codeABRK(fs, OpCode.SETTABUP, var.indT, var.indIdx, ex);
                        break;
                    }
                case ExpKind.IndexInt:
                    {
                        codeABRK(fs, OpCode.SETI, var.indT, var.indIdx, ex);
                        break;
                    }
                case ExpKind.IndexString:
                    {
                        codeABRK(fs, OpCode.SETFIELD, var.indT, var.indIdx, ex);
                        break;
                    }
                case ExpKind.Indexed:
                    {
                        codeABRK(fs, OpCode.SETTABLE, var.indT, var.indIdx, ex);
                        break;
                    }
                default:
                    {
                        Lexer.assert(false);  /* invalid var kind to store */
                        break;
                    }
            }
            freeexp(fs, ex);
        }

        /*
        ** Emit a SETLIST instruction.
        ** 'base' is register that keeps table;
        ** 'nelems' is #table plus those to be stored now;
        ** 'tostore' is number of values (in registers 'base + 1',...) to add to
        ** table (or LUA_MULTRET to add up to stack top).
        */
        internal static void setlist(FuncState fs, int base_, int nelems, int tostore)
        {
            int c = (nelems - 1) / Parser.LFIELDS_PER_FLUSH + 1;
            int b = (tostore == Parser.LUA_MULTRET) ? 0 : tostore;
            Lexer.assert(tostore != 0 && tostore <= Parser.LFIELDS_PER_FLUSH);
            if (c <= MaxArgC)
                codeABC(fs, OpCode.SETLIST, base_, b, c);
            else if (c <= MaxArgAx)
            {
                codeABC(fs, OpCode.SETLIST, base_, b, 0);
                codeextraarg(fs, c);
            }
            else
            {
                fs.ls.syntaxerror("constructor too long");
            }
            fs.freereg = base_ + 1;  /* free registers with list values */
        }

        /*
        ** Emit an "extra argument" instruction (format 'iAx')
        */
        static int codeextraarg(FuncState fs, int a)
        {
            Lexer.assert(a <= MaxArgAx);
            return code(fs, Inst.CreateAx(OpCode.EXTRAARG, a));
        }


        /*
        ** Correct a jump list to jump to 'target'. If 'hasclose' is true,
        ** 'target' contains an OP_CLOSE instruction (see first assert).
        ** Only the jumps with ('m' == true) need that close; other jumps
        ** avoid it jumping to the next instruction.
        */
        internal static void patchgoto(FuncState fs, int list, int target, bool hasclose)
        {
            Lexer.assert(!hasclose || Inst.OpCode(fs.f.Codes[target]) == OpCode.CLOSE);
            while (list != Parser.NoJump)
            {
                int next = getjump(fs, list);
                Lexer.assert(Inst.M(fs.f.Codes[list]) || hasclose);
                patchtestreg(fs, list, NO_REG);  /* do not generate values */
                if (!hasclose || Inst.M(fs.f.Codes[list]))
                {
                    fixjump(fs, list, target);
                }
                else
                {
                    /* there is a CLOSE instruction but jump does not need it */
                    fixjump(fs, list, target + 1);  /* avoid CLOSE instruction */
                }
                list = next;
            }
        }

        /*
        ** Mark (using the 'm' arg) all jumps in 'list' to close upvalues. Mark
        ** will instruct 'luaK_patchgoto' to make these jumps go to OP_CLOSE
        ** instructions.
        */
        internal static void patchclose(FuncState fs, int list)
        {
            for (; list != Parser.NoJump; list = getjump(fs, list))
            {
                Lexer.assert(Inst.OpCode(fs.f.Codes[list]) == OpCode.JMP);
                fs.f.Codes[list] = Inst.SetM(fs.f.Codes[list], true);
            }
        }

        internal static void concat(FuncState fs, ref int l1, int l2)
        {

        }

        /*
        ** Emit instruction 'i', checking for array sizes and saving also its
        ** line information. Return 'i' position.
        */
        static int code(FuncState fs, uint i)
        {
            Proto f = fs.f;
            f.Codes.Add(i);
            //savelineinfo(fs, f, fs.pc, fs.ls.lastline);
            return fs.pc++;
        }

        internal static int codeK(FuncState fs, int reg, int k)
        {
            return 0; // TODO
        }

        internal static int codeABCk(FuncState fs, OpCode o, int a, int b, int c, bool k)
        {
            return 0; // TODO
        }

        internal static int codeABC(FuncState fs, OpCode o, int a, int b, int c)
        {
            return codeABCk(fs, o, a, b, c, false);
        }

        internal static int codeABsC(FuncState fs, OpCode o, int a, int b, int sc, bool k)
        {
            return 0;
        }

        internal static int codeABRK(FuncState fs, OpCode o, int a, int b, ExpDesc rk)
        {
            return 0;
        }

        internal static int codeABx(FuncState fs, OpCode o, int a, int bc)
        {
            return codeABCk(fs, o, a, bc, 0, false);
        }

        internal static int code_loadbool(FuncState fs, int A, int b, int jump)
        {
            getlabel(fs);  /* those instructions may be jump targets */
            return codeABC(fs, OpCode.LOADBOOL, A, b, jump);
        }

        /*
        ** Return the previous instruction of the current code. If there
        ** may be a jump target between the current instruction and the
        ** previous one, return an invalid instruction (to avoid wrong
        ** optimizations).
        */
        static int previousinstruction(FuncState fs)
        {
            if (fs.pc > fs.lasttarget) {
                return fs.pc - 1;  /* previous instruction */
            }
            else {
                return -1;
            }
        }

        /*
        ** Create a OP_LOADNIL instruction, but try to optimize: if the previous
        ** instruction is also OP_LOADNIL and ranges are compatible, adjust
        ** range of previous instruction instead of emitting a new one. (For
        ** instance, 'local a; local b' will generate a single opcode.)
        */
        internal static void nil(FuncState fs, int from, int n)
        {
            int l = from + n - 1;  /* last register to set nil */
            int previousPos = previousinstruction(fs);
            if (previousPos >= 0)
            {
                uint previous = fs.f.Codes[previousPos];
                if ( Inst.OpCode(previous) == OpCode.LOADNIL)
                {  /* previous is LOADNIL? */
                    int pfrom = Inst.A(previous);  /* get previous range */
                    int pl = pfrom + Inst.B(previous);
                    if ((pfrom <= from && from <= pl + 1) ||
                        (from <= pfrom && pfrom <= l + 1))
                    {  /* can connect both? */
                        if (pfrom < from) from = pfrom;  /* from = min(from, pfrom) */
                        if (pl > l) l = pl;  /* l = max(l, pl) */
                        previous = Inst.SetA(previous, from);
                        previous = Inst.SetB(previous, l - from);
                        fs.f.Codes[previousPos] = previous;
                        return;
                    }  /* else go through */
                }
            }
            codeABC(fs, OpCode.LOADNIL, from, n - 1, 0);  /* else no optimization */
        }

        /*
        ** Code a 'return' instruction
        */
        internal static void ret(FuncState fs, int first, int nret)
        {
            OpCode op;
            switch (nret)
            {
                case 0: op = OpCode.RETURN0; break;
                case 1: op = OpCode.RETURN1; break;
                default: op = OpCode.RETURN; break;
            }
            codeABC(fs, op, first, nret + 1, 0);
        }

        /*
        ** Do a final pass over the code of a function, doing small peephole
        ** optimizations and adjustments.
        */
        internal static void finish(FuncState fs)
        {
            int i;
            Proto p = fs.f;
            for (i = 0; i < fs.pc; i++)
            {
                uint pc = p.Codes[i];
                //Lexer.assert(i == 0 || isOT(*(pc - 1)) == isIT(*pc));
                switch (Inst.OpCode(pc))
                {
                    case OpCode.RETURN0:
                    case OpCode.RETURN1:
                        {
                            if (p.Protos.Count == 0 && !p.HasVarArg)
                            {
                                break;  /* no extra work */
                                        /* else use OP_RETURN to do the extra work */
                            }
                            pc = Inst.SetOpCode(pc, OpCode.RETURN);

                            /* FALLTHROUGH */
                            pc = Inst.SetC(pc, p.HasVarArg ? p.ParamNum + 1 : 0);
                            pc = Inst.SetK(pc, true);  /* signal that there is extra work */

                            p.Codes[i] = pc;
                            break;
                        }
                    case OpCode.RETURN:
                    case OpCode.TAILCALL:
                        {
                            if (p.Protos.Count > 0 || p.HasVarArg)
                            {
                                pc = Inst.SetC(pc, p.HasVarArg ? p.ParamNum + 1 : 0);
                                pc = Inst.SetK(pc, true);  /* signal that there is extra work */
                                p.Codes[i] = pc;
                            }
                            break;
                        }
                    case OpCode.JMP:
                        {
                            int target = finaltarget(p.Codes, i);
                            fixjump(fs, i, target);
                            break;
                        }
                    default:
                        break;
                }
            }
        }

        /*
        ** return the final target of a jump (skipping jumps to jumps)
        */
        static int finaltarget(List<uint> code, int i)
        {
            int count;
            for (count = 0; count < 100; count++)
            {  /* avoid infinite loops */
                uint pc = code[i];
                if (Inst.OpCode(pc) != OpCode.JMP)
                {
                    break;
                }
                else
                {
                    i += Inst.sJ(pc) + 1;
                }
            }
            return i;
        }

        /*
        ** Emit SELF instruction (convert expression 'e' into 'e:key(e,').
        */
        internal static void self(FuncState fs, ExpDesc e, ExpDesc key)
        {
            int ereg;
            exp2anyreg(fs, e);
            ereg = e.info;  /* register where 'e' was placed */
            freeexp(fs, e);
            e.info = fs.freereg;  /* base register for op_self */
            e.k = ExpKind.NonReloc;  /* self expression has a fixed register */
            reserveregs(fs, 2);  /* function and 'self' produced by op_self */
            codeABRK(fs, OpCode.SELF, e.info, ereg, key);
            freeexp(fs, key);
        }


        /*
        ** Apply prefix operation 'op' to expression 'e'.
        */
        internal static void prefix(FuncState fs, UnOpr op, ExpDesc e, int line)
        {
            //static const expdesc ef = { VKINT, { 0 }, NO_JUMP, NO_JUMP };
            switch (op)
            {
                case UnOpr.MINUS:
                case UnOpr.BNOT:  /* use 'ef' as fake 2nd operand */
                    /*
                     TODO
                    if (constfolding(fs, op + LUA_OPUNM, e, &ef))
                    {
                        break;
                    }
                    */
                /* FALLTHROUGH */
                case UnOpr.LEN:
                    codeunexpval(fs, (OpCode)((int)op + (int)OpCode.UNM), e, line);
                    break;
                case UnOpr.NOT:
                    codenot(fs, e);
                    break;
                default:
                    Lexer.assert(false);
                    break;
            }
        }

        /*
        ** Code 'not e', doing constant folding.
        */
        static void codenot(FuncState fs, ExpDesc e)
        {
            dischargevars(fs, e);
            switch (e.k)
            {
                case ExpKind.Nil:
                case ExpKind.False:
                    {
                        e.k = ExpKind.True;  /* true == not nil == not false */
                        break;
                    }
                case ExpKind.Const:
                case ExpKind.Float:
                case ExpKind.Int:
                case ExpKind.True:
                    {
                        e.k = ExpKind.False;  /* false == not "x" == not 0.5 == not 1 == not true */
                        break;
                    }
                case ExpKind.Jump:
                    {
                        negatecondition(fs, e);
                        break;
                    }
                case ExpKind.Reloc:
                case ExpKind.NonReloc:
                    {
                        discharge2anyreg(fs, e);
                        freeexp(fs, e);
                        e.info = codeABC(fs, OpCode.NOT, 0, e.info, 0);
                        e.k = ExpKind.Reloc;
                        break;
                    }
                default:
                    Lexer.assert(false);  /* cannot happen */
                    break;
            }
            /* interchange true and false lists */
            { int temp = e.f; e.f = e.t; e.t = temp; }
            removevalues(fs, e.f);  /* values are useless when negated */
            removevalues(fs, e.t);
        }

        /*
        ** Traverse a list of tests ensuring no one produces a value
        */
        static void removevalues(FuncState fs, int list)
        {
            for (; list != Parser.NoJump; list = getjump(fs, list))
            {
                patchtestreg(fs, list, NO_REG);
            }
        }

        /*
        ** Negate condition 'e' (where 'e' is a comparison).
        */
        static void negatecondition(FuncState fs, ExpDesc e)
        {
            int pcPos = getjumpcontrol(fs, e.info);
            uint pc = fs.f.Codes[pcPos];
            Lexer.assert(Inst.IsTMode(Inst.OpCode(pc)) && Inst.OpCode(pc) != OpCode.TESTSET &&
                                                     Inst.OpCode(pc) != OpCode.TEST);
            pc = Inst.SetK(pc, !Inst.K(pc));
        }

        /*
        ** Ensures expression value is in any register.
        ** (Expression still may have jump lists.)
        */
        static void discharge2anyreg(FuncState fs, ExpDesc e)
        {
            if (e.k != ExpKind.NonReloc)
            {  /* no fixed register yet? */
                reserveregs(fs, 1);  /* get a register */
                discharge2reg(fs, e, fs.freereg - 1);  /* put value there */
            }
        }

        /*
        ** Emit code for unary expressions that "produce values"
        ** (everything but 'not').
        ** Expression to produce final result will be encoded in 'e'.
        */
        static void codeunexpval(FuncState fs, OpCode op, ExpDesc e, int line)
        {
            int r = exp2anyreg(fs, e);  /* opcodes operate only on registers */
            freeexp(fs, e);
            e.info = codeABC(fs, op, 0, r, 0);  /* generate opcode */
            e.k = ExpKind.Reloc;  /* all those operations are relocatable */
            fixline(fs, line);
        }

        /*
        ** Process 1st operand 'v' of binary operation 'op' before reading
        ** 2nd operand.
        */
        internal static void infix(FuncState fs, BinOpr op, ExpDesc v)
        {
            switch (op)
            {
                case BinOpr.AND:
                    {
                        goiftrue(fs, v);  /* go ahead only if 'v' is true */
                        break;
                    }
                case BinOpr.OR:
                    {
                        goiffalse(fs, v);  /* go ahead only if 'v' is false */
                        break;
                    }
                case BinOpr.CONCAT:
                    {
                        exp2nextreg(fs, v);  /* operand must be on the stack */
                        break;
                    }
                case BinOpr.ADD:
                case BinOpr.SUB:
                case BinOpr.MUL:
                case BinOpr.DIV:
                case BinOpr.IDIV:
                case BinOpr.MOD:
                case BinOpr.POW:
                case BinOpr.BAND:
                case BinOpr.BOR:
                case BinOpr.BXOR:
                case BinOpr.SHL:
                case BinOpr.SHR:
                    {
                        if (!tonumeral(v))
                        {
                            exp2anyreg(fs, v);
                        }
                        /* else keep numeral, which may be folded with 2nd operand */
                        break;
                    }
                case BinOpr.EQ:
                case BinOpr.NE:
                    {
                        if (!tonumeral(v) && fs.ls.Tk.token != TokenKind.Undef)
                        {
                            exp2RK(fs, v);
                        }
                        /* else keep numeral, which may be an immediate operand */
                        break;
                    }
                case BinOpr.LT:
                case BinOpr.LE:
                case BinOpr.GT:
                case BinOpr.GE:
                    {
                        int dummy = 0;
                        if (!isSCnumber(v, ref dummy))
                        {
                            exp2anyreg(fs, v);
                        }
                        /* else keep numeral, which may be an immediate operand */
                        break;
                    }
                default:
                    Lexer.assert(false);
                    break;
            }
        }

        /*
        ** Ensures final expression result is in a valid R/K index
        ** (that is, it is either in a register or in 'k' with an index
        ** in the range of R/K indices).
        ** Returns 1 if expression is K, 0 otherwise.
        */
        static bool exp2RK(FuncState fs, ExpDesc e)
        {
            exp2val(fs, e);
            switch (e.k)
            {  /* move constants to 'k' */
                case ExpKind.True: e.info = boolK(fs, true); goto vk;
                case ExpKind.False: e.info = boolK(fs, false); goto vk;
                case ExpKind.Nil: e.info = nilK(fs); goto vk;
                case ExpKind.Int: e.info = luaK_intK(fs, e.ival); goto vk;
                case ExpKind.Float: e.info = luaK_numberK(fs, e.nval); goto vk;
                case ExpKind.Const:
                    vk:
                    e.k = ExpKind.Const;
                    if (e.info <= MaxIndexRk)
                    {
                        /* constant fits in 'argC'? */
                        return true;
                    }
                    else
                    {
                        break;
                    }
                default:
                    break;
            }
            /* not a constant in the right range: put it in a register */
            exp2anyreg(fs, e);
            return false;
        }


        /*
        ** Add an integer to list of constants and return its index.
        ** Integers use userdata as keys to avoid collision with floats with
        ** same value; conversion to 'void*' is used only for hashing, so there
        ** are no "precision" problems.
        */
        static int luaK_intK(FuncState fs, int n)
        {
            LuaValue o = new LuaValue(n);
            return addk(fs, o);
        }

        /*
        ** Add a float to list of constants and return its index.
        */
        static int luaK_numberK(FuncState fs, double r)
        {
            var o = new LuaValue(r);
            return addk(fs, o);  /* use number itself as key */
        }


        /*
        ** Add a boolean to list of constants and return its index.
        */
        static int boolK(FuncState fs, bool b)
        {
            var o = new LuaValue(b);
            return addk(fs, o);  /* use boolean itself as key */
        }


        /*
        ** Add nil to list of constants and return its index.
        */
        static int nilK(FuncState fs)
        {
            var o = LuaValue.Nil;
            return addk(fs, o);
        }

        /*
        ** Check whether 'i' can be stored in an 'sC' operand.
        ** Equivalent to (0 <= i + OFFSET_sC && i + OFFSET_sC <= MAXARG_C)
        ** but without risk of overflows in the addition.
        */
        static bool fitsC(int i)
        {
            return (-OffsetSc <= i && i <= MaxArgC - OffsetSc);
        }

        /*
        ** Check whether expression 'e' is a literal integer or float in
        ** proper range to fit in register sC
        */
        static bool isSCnumber(ExpDesc e, ref int i)
        {
            if (e.k == ExpKind.Int)
            {
                i = e.ival;
            }
            else if (!(e.k == ExpKind.Float && floatI(e.nval, ref i)))
            {
                return false;  /* not a number */
            }
            if (!hasjumps(e) && fitsC(i))
            {
                i += OffsetSc;
                return true;
            }
            else
            {
                return false;
            }
        }

        static bool floatI(double f, ref int fi)
        {
            fi = fi + 0;
            return (Math.Floor(f) == f && fitsBx(ref fi));
        }

        /*
        ** Check whether 'i' can be stored in an 'sBx' operand.
        */
        static bool fitsBx(ref int i)
        {
            return (-OffsetSbx <= i && i <= MaxArgBx - OffsetSbx);
        }

        /*
        ** Add constant 'v' to prototype's list of constants (field 'k').
        ** Use scanner's table to cache position of constants in constant list
        ** and try to reuse constants. Because some values should not be used
        ** as keys (nil cannot be a key, integer keys can collapse with float
        ** keys), the caller must provide a useful 'key' for indexing the cache.
        */
        static int addk(FuncState fs, LuaValue v)
        {
            Proto f = fs.f;
            for( int i=0; i< f.Consts.Length; i++)
            {
                if( f.Consts[i] == v)
                {
                    return i;
                }
            }
            /* constant not found; create a new entry */
            /* numerical value does not need GC barrier;
               table has no metatable, so it does not need to invalidate cache */
            f.AddConst(v);
            fs.nk++;
            return f.Consts.Length-1;
        }

        /*
        ** If expression is a numeric constant, fills 'v' with its value
        ** and returns 1. Otherwise, returns 0.
        */
        static bool tonumeral(ExpDesc e)
        {
            if (hasjumps(e))
            {
                return false;  /* not a numeral */
            }
            switch (e.k)
            {
                case ExpKind.Int:
                case ExpKind.Float:
                    return true;
                default:
                    return false;
            }
        }

        /*
        ** If expression is a numeric constant, fills 'v' with its value
        ** and returns 1. Otherwise, returns 0.
        */
        static bool tonumeral(ExpDesc e, ref LuaValue v)
        {
            if (hasjumps(e))
            {
                return false;  /* not a numeral */
            }
            switch (e.k)
            {
                case ExpKind.Int:
                    v = new LuaValue(e.ival);
                    return true;
                case ExpKind.Float:
                    v = new LuaValue(e.nval);
                    return true;
                default:
                    return false;
            }
        }

        static void copyExpDesc(ExpDesc a, ExpDesc b)
        {

            a.k = b.k;
            a.ival = b.ival;
            a.nval = b.nval;
            a.info = b.info;
            a.indIdx = b.indIdx;
            a.indT = b.indT;
            a.t = b.t;
            a.f = b.f;
        }

        /*
        ** Finalize code for binary operation, after reading 2nd operand.
        */
        internal static void posfix(FuncState fs, BinOpr opr, ExpDesc e1, ExpDesc e2, int line)
        {
            switch (opr)
            {
                case BinOpr.AND:
                    {
                        Lexer.assert(e1.t == Parser.NoJump);  /* list closed by 'luK_infix' */
                        dischargevars(fs, e2);
                        concat(fs, ref e2.f, e1.f);
                        copyExpDesc(e1, e2);
                        break;
                    }
                case BinOpr.OR:
                    {
                        Lexer.assert(e1.f == Parser.NoJump);  /* list closed by 'luK_infix' */
                        dischargevars(fs, e2);
                        concat(fs, ref e2.t, e1.t);
                        copyExpDesc(e1, e2);
                        break;
                    }
                case BinOpr.CONCAT:
                    {  /* e1 .. e2 */
                        exp2nextreg(fs, e2);
                        codeconcat(fs, e1, e2, line);
                        break;
                    }
                case BinOpr.ADD:
                case BinOpr.MUL:
                    {
                        if (!constfolding(fs, (OpCode)((int)opr + (int)OpCode.ADD), e1, e2))
                        {
                            codecommutative(fs, (OpCode)((int)opr + (int)OpCode.ADD), e1, e2, line);
                        }
                        break;
                    }
                case BinOpr.SUB:
                case BinOpr.DIV:
                case BinOpr.IDIV:
                case BinOpr.MOD:
                case BinOpr.POW:
                    {
                        if (!constfolding(fs, (OpCode)((int)opr + (int)OpCode.ADD), e1, e2))
                        {
                            codearith(fs, (OpCode)((int)opr + (int)OpCode.ADD), e1, e2, false, line);
                        }
                        break;
                    }
                case BinOpr.BAND:
                case BinOpr.BOR:
                case BinOpr.BXOR:
                    {
                        if (!constfolding(fs, (OpCode)((int)opr + (int)OpCode.ADD), e1, e2))
                        {
                            codebitwise(fs, opr, e1, e2, line);
                        }
                        break;
                    }
                case BinOpr.SHL:
                    {
                        if (!constfolding(fs, OpCode.SHL, e1, e2))
                        {
                            if (isSCint(e1))
                            {
                                swapexps(e1, e2);
                                codebini(fs, OpCode.SHLI, e1, e2, true, line);
                            }
                            else
                                codeshift(fs, OpCode.SHL, e1, e2, line);
                        }
                        break;
                    }
                case BinOpr.SHR:
                    {
                        if (!constfolding(fs, OpCode.SHR, e1, e2))
                        {
                            codeshift(fs, OpCode.SHR, e1, e2, line);
                        }
                        break;
                    }
                case BinOpr.EQ:
                case BinOpr.NE:
                    {
                        if (e2.k == ExpKind.Undef)
                        {
                            codeisdef(fs, opr == BinOpr.NE, e1);
                        }
                        else
                        {
                            codeeq(fs, opr, e1, e2);
                        }
                        break;
                    }
                case BinOpr.LT:
                case BinOpr.LE:
                    {
                        OpCode op = (OpCode)((opr - BinOpr.EQ) + OpCode.EQ);
                        codeorder(fs, op, e1, e2);
                        break;
                    }
                case BinOpr.GT:
                case BinOpr.GE:
                    {
                        /* '(a > b)' <=> '(b < a)';  '(a >= b)' <=> '(b <= a)' */
                        OpCode op = (OpCode)((opr - BinOpr.NE) + OpCode.EQ);
                        swapexps(e1, e2);
                        codeorder(fs, op, e1, e2);
                        break;
                    }
                default:
                    Lexer.assert(false);
                    break;
            }
        }

        /*
        ** Emit code for order comparisons.
        ** When the first operand A is an integral value in the proper range,
        ** change (A < B) to (B > A) and (A <= B) to (B >= A) so that
        ** it can use an immediate operand.
        */
        static void codeorder(FuncState fs, OpCode op, ExpDesc e1, ExpDesc e2)
        {
            int r1, r2;
            int im = 0;
            if (isSCnumber(e2, ref im))
            {
                /* use immediate operand */
                r1 = exp2anyreg(fs, e1);
                r2 = im;
                op = (OpCode)(op - OpCode.LT + OpCode.LTI);
            }
            else if (isSCnumber(e1, ref im))
            {
                /* transform (A < B) to (B > A) and (A <= B) to (B >= A) */
                r1 = exp2anyreg(fs, e2);
                r2 = im;
                op = (op == OpCode.LT) ? OpCode.GTI : OpCode.GEI;
            }
            else
            {  /* regular case, compare two registers */
                r1 = exp2anyreg(fs, e1);
                r2 = exp2anyreg(fs, e2);
            }
            freeexps(fs, e1, e2);
            e1.info = condjump(fs, op, r1, r2, true);
            e1.k = ExpKind.Jump;
        }

        /*
        ** Emit code for equality comparisons ('==', '~=').
        ** 'e1' was already put as RK by 'luaK_infix'.
        */
        static void codeeq(FuncState fs, BinOpr opr, ExpDesc e1, ExpDesc e2)
        {
            int r1, r2;
            int im = 0;
            OpCode op;
            if (e1.k != ExpKind.NonReloc)
            {
                Lexer.assert(e1.k == ExpKind.Const || e1.k == ExpKind.Int || e1.k == ExpKind.Float);
                swapexps(e1, e2);
            }
            r1 = exp2anyreg(fs, e1);  /* 1nd expression must be in register */
            if (isSCnumber(e2, ref im))
            {
                op = OpCode.EQI;
                r2 = im;  /* immediate operand */
            }
            else if (exp2RK(fs, e2))
            {  /* 1st expression is constant? */
                op = OpCode.EQK;
                r2 = e2.info;  /* constant index */
            }
            else
            {
                op = OpCode.EQ;  /* will compare two registers */
                r2 = exp2anyreg(fs, e2);
            }
            freeexps(fs, e1, e2);
            e1.info = condjump(fs, op, r1, r2, (opr == BinOpr.EQ));
            e1.k = ExpKind.Jump;
        }

        /*
        ** Code a "conditional jump", that is, a test or comparison opcode
        ** followed by a jump. Return jump position.
        */
        static int condjump(FuncState fs, OpCode op, int A, int B, bool k)
        {
            codeABCk(fs, op, A, B, 0, k);
            return jump(fs);
        }


        static void codeisdef(FuncState fs, bool eq, ExpDesc v)
        {
            normalizeindexed(fs, v);
            v.info = codeABCk(fs, OpCode.ISDEF, 0, v.indT, v.indIdx, eq);
            v.k = ExpKind.Reloc;
        }

        static void normalizeindexed(FuncState fs, ExpDesc v)
        {
            if (v.k != ExpKind.Indexed)
            {  /* not in proper form? */
                int key = fs.freereg;  /* register with key value */
                reserveregs(fs, 1);
                switch (v.k)
                {
                    case ExpKind.IndexInt:
                        integer(fs, key, v.indIdx);
                        break;
                    case ExpKind.IndexString:
                        codeK(fs, key, v.indIdx);
                        break;
                    case ExpKind.IndexUp:
                        codeK(fs, key, v.indIdx);
                        codeABC(fs, OpCode.GETUPVAL, fs.freereg, v.indT, 0);
                        v.indT = fs.freereg;
                        reserveregs(fs, 1);  /* one more register for the upvalue */
                        break;
                    default:
                        semerror(fs.ls, "'undef' is not a value!!");
                        break;
                }
                v.indIdx = key;
                v.k = ExpKind.Indexed;
            }
            freeregs(fs, v.indT, v.indIdx);
        }
        
        /*
        ** Code shift operators. If second operand is constant, use immediate
        ** operand (negating it if shift is in the other direction).
        */
        static void codeshift(FuncState fs, OpCode op, ExpDesc e1, ExpDesc e2, int line)
        {
            if (isSCint(e2))
            {
                bool changedir = false;
                if (op == OpCode.SHL)
                {
                    changedir = true;
                    e2.ival = -(e2.ival);
                }
                codebini(fs, OpCode.SHRI, e1, e2, changedir, line);
            }
            else
            {
                codebinexpval(fs, op, e1, e2, line);
            }
        }

        /*
        ** Code bitwise operations; they are all associative, so the function
        ** tries to put an integer constant as the 2nd operand (a K operand).
        */
        static void codebitwise(FuncState fs, BinOpr opr, ExpDesc e1, ExpDesc e2, int line)
        {
            bool inv = false;
            int v1, v2, pc;
            OpCode op;
            if (e1.k == ExpKind.Int && exp2RK(fs, e1))
            {
                swapexps(e1, e2);  /* 'e2' will be the constant operand */
                inv = true;
            }
            else if (!(e2.k == ExpKind.Int && exp2RK(fs, e2)))
            {  /* no constants? */
                op = (OpCode)(opr - BinOpr.BAND + OpCode.BAND);
                codebinexpval(fs, op, e1, e2, line);  /* all-register opcodes */
                return;
            }
            v1 = exp2anyreg(fs, e1);
            v2 = e2.info;  /* index in K array */
            op = (OpCode)(opr - BinOpr.BAND + OpCode.BANDK);
            Lexer.assert(fs.f.Consts[v2].IsInteger);
            pc = codeABCk(fs, op, 0, v1, v2, inv);
            finishbinexpval(fs, e1, e2, pc, line);
        }

        
        /*
        ** Try to "constant-fold" an operation; return 1 iff successful.
        ** (In this case, 'e1' has the final result.)
        */
        static bool constfolding(FuncState fs, OpCode op, ExpDesc e1, ExpDesc e2)
        {
            LuaValue v1 = new LuaValue();
            LuaValue v2 = new LuaValue();
            if (!tonumeral(e1, ref v1) || !tonumeral(e2, ref v2) || !validop(op, ref v1, ref v2))
            {
                return false;  /* non-numeric operands or not safe to fold */
            }
            LuaValue res = LuaValue.BinOp(op, v1, v2);
            if (res.IsInteger) {
                e1.k = ExpKind.Int;
                e1.ival = res.AsInt;
            }
            else {  /* folds neither NaN nor 0.0 (to avoid problems with -0.0) */
                e1.k = ExpKind.Float;
                e1.nval = res.AsFloat;
            }
            return true;
        }

        /*
        ** Return false if folding can raise an error.
        ** Bitwise operations need operands convertible to integers; division
        ** operations cannot have 0 as divisor.
        */
        static bool validop(OpCode op, ref LuaValue v1, ref LuaValue v2)
        {
            switch (op)
            {
                case OpCode.AND:
                case OpCode.BOR:
                case OpCode.BXOR:
                case OpCode.SHL:
                case OpCode.SHR:
                case OpCode.BNOT:
                    {  /* conversion errors */
                        int i = 0;
                        return (tointegerns(v1, ref i) && tointegerns(v2, ref i));
                    }
                case OpCode.DIV:
                case OpCode.IDIV:
                case OpCode.MOD:  /* division by 0 */
                    return nvalue(v2);
                default:
                    return true;  /* everything else is valid */
            }
        }

        /* convert an object to an integer (without string coercion) */
        static bool tointegerns(LuaValue o, ref int i)
        {
            if (o.IsInteger) {
                i = o.AsInt;
                return true;
            }
            else if (o.IsFloat) {
                i = (int)o.AsFloat;
                return true;
            }
            else
            {
                return false;
            }
        }

        /* convert an object to an integer (without string coercion) */
        static bool nvalue(LuaValue o)
        {
            if (o.IsInteger)
            {
                return true;
            }
            else if (o.IsFloat)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /*
        ** Create code for '(e1 .. e2)'.
        ** For '(e1 .. e2.1 .. e2.2)' (which is '(e1 .. (e2.1 .. e2.2))',
        ** because concatenation is right associative), merge both CONCATs.
        */
        static void codeconcat(FuncState fs, ExpDesc e1, ExpDesc e2, int line)
        {
            int ie2Pos = previousinstruction(fs);
            uint ie2 = fs.f.Codes[ie2Pos];
            if (Inst.OpCode(ie2) == OpCode.CONCAT)
            {  /* is 'e2' a concatenation? */
                int n = Inst.B(ie2);  /* # of elements concatenated in 'e2' */
                Lexer.assert(e1.info + 1 == Inst.A(ie2));
                freeexp(fs, e2);
                ie2 = Inst.SetA(ie2, e1.info);  /* correct first element ('e1') */
                ie2 = Inst.SetB(ie2, n + 1);  /* will concatenate one more element */
                fs.f.Codes[ie2Pos] = ie2;
            }
            else
            {  /* 'e2' is not a concatenation */
                codeABC(fs, OpCode.CONCAT, e1.info, 2, 0);  /* new concat opcode */
                freeexp(fs, e2);
                fixline(fs, line);
            }
        }

        /*
        ** Code commutative operators ('+', '*'). If first operand is a
        ** constant, change order of operands to use immediate operator.
        */
        static void codecommutative(FuncState fs, OpCode op, ExpDesc e1, ExpDesc e2, int line)
        {
            bool flip = false;
            if (isSCint(e1))
            {
                swapexps(e1, e2);
                flip = true;
            }
            codearith(fs, op, e1, e2, flip, line);
        }

        /*
        ** Check whether expression 'e' is a literal integer in
        ** proper range to fit in register sC
        */
        static bool isSCint(ExpDesc e)
        {
            return isKint(e) && fitsC(e.ival);
        }

        static void swapexps(ExpDesc e1, ExpDesc e2)
        {
            ExpDesc temp = new ExpDesc();
            copyExpDesc(temp, e1);
            copyExpDesc(e1, e2);
            copyExpDesc(e2, temp);
        }

        /*
        ** Code arithmetic operators ('+', '-', ...). If second operand is a
        ** constant in the proper range, use variant opcodes with immediate
        ** operands.
        */
        static void codearith(FuncState fs, OpCode op, ExpDesc e1, ExpDesc e2, bool flip, int line)
        {
            if (!isSCint(e2))
            {
                codebinexpval(fs, op, e1, e2, line);  /* use standard operators */
            }
            else
            {
                /* use immediate operators */
                codebini(fs, (OpCode)(op - (int)BinOpr.ADD + (int)OpCode.ADDI), e1, e2, flip, line);
            }
        }

        /*
        ** Code binary operators ('+', '-', ...) with immediate operands.
        */
        static void codebini(FuncState fs, OpCode op, ExpDesc e1, ExpDesc e2, bool k, int line)
        {
            int v2 = e2.ival;  /* immediate operand */
            int v1 = exp2anyreg(fs, e1);
            int pc = codeABsC(fs, op, 0, v1, v2, k);  /* generate opcode */
            finishbinexpval(fs, e1, e2, pc, line);
        }

        /*
        ** Emit code for binary expressions that "produce values"
        ** (everything but logical operators 'and'/'or' and comparison
        ** operators).
        ** Expression to produce final result will be encoded in 'e1'.
        ** Because 'luaK_exp2anyreg' can free registers, its calls must be
        ** in "stack order" (that is, first on 'e2', which may have more
        ** recent registers to be released).
        */
        static void codebinexpval(FuncState fs, OpCode op, ExpDesc e1, ExpDesc e2, int line)
        {
            int v2 = exp2anyreg(fs, e2);  /* both operands are in registers */
            int v1 = exp2anyreg(fs, e1);
            int pc = codeABC(fs, op, 0, v1, v2);  /* generate opcode */
            finishbinexpval(fs, e1, e2, pc, line);
        }

        static void finishbinexpval(FuncState fs, ExpDesc e1, ExpDesc e2, int pc, int line)
        {
            freeexps(fs, e1, e2);
            e1.info = pc;
            e1.k = ExpKind.Reloc;  /* all those operations are relocatable */
            fixline(fs, line);
        }


        /*
        ** Free registers used by expressions 'e1' and 'e2' (if any) in proper
        ** order.
        */
        static void freeexps(FuncState fs, ExpDesc e1, ExpDesc e2)
        {
            int r1 = (e1.k == ExpKind.NonReloc) ? e1.info : -1;
            int r2 = (e2.k == ExpKind.NonReloc) ? e2.info : -1;
            freeregs(fs, r1, r2);
        }


        internal static void codeundef(FuncState fs, ExpDesc v)
        {

        }

        internal static void setoneret(FuncState fs, ExpDesc e)
        {

        }

        internal static void goiftrue(FuncState fs, ExpDesc e)
        {

        }

        internal static void goiffalse(FuncState fs, ExpDesc e)
        {

        }

        internal static int getlabel(FuncState fs)
        {
            return 0;
        }

        internal static void patchtohere(FuncState fs, int list)
        {

        }

        internal static void patchtohere(FuncState fs, ExpDesc v)
        {

        }

        internal static int jump(FuncState fs)
        {
            return 0;
        }

        /*
        ** Traverse a list of tests, patching their destination address and
        ** registers: tests producing values jump to 'vtarget' (and put their
        ** values in 'reg'), other tests jump to 'dtarget'.
        */
        static void patchlistaux(FuncState fs, int list, int vtarget, int reg, int dtarget)
        {
            while (list != Parser.NoJump)
            {
                int next = getjump(fs, list);
                if (patchtestreg(fs, list, reg))
                    fixjump(fs, list, vtarget);
                else
                    fixjump(fs, list, dtarget);  /* jump to default target */
                list = next;
            }
        }

        /*
        ** Patch destination register for a TESTSET instruction.
        ** If instruction in position 'node' is not a TESTSET, return 0 ("fails").
        ** Otherwise, if 'reg' is not 'NO_REG', set it as the destination
        ** register. Otherwise, change instruction to a simple 'TEST' (produces
        ** no register value)
        */
        static bool patchtestreg(FuncState fs, int node, int reg)
        {
            int pc = getjumpcontrol(fs, node);
            uint i = fs.f.Codes[pc];
            if (Inst.OpCode(i) != OpCode.TESTSET)
            {
                return false;  /* cannot patch other instructions */
            }
            if (reg != NO_REG && reg != Inst.B(i))
            {
                setjumpcontrol(fs, pc, Inst.SetA(i, reg));
            }
            else
            {
                /* no register to put value or register already has the value;
                   change instruction to simple test */
                uint inst = Inst.CreateABCk(OpCode.TEST, Inst.B(i), 0, 0, Inst.K(i));
                setjumpcontrol(fs, pc, inst);
            }
            return true;
        }

        /*
        ** Fix jump instruction at position 'pc' to jump to 'dest'.
        ** (Jump addresses are relative in Lua)
        */
        static void fixjump(FuncState fs, int pc, int dest)
        {
            uint jmp = fs.f.Codes[pc];
            int offset = dest - (pc + 1);
            Lexer.assert(dest != Parser.NoJump);
            /*
            if (!(-OFFSET_sJ <= offset && offset <= MAXARG_sJ - OFFSET_sJ))
                luaX_syntaxerror(fs->ls, "control structure too long");
                */
            Lexer.assert(Inst.OpCode(jmp) == OpCode.JMP);
            fs.f.Codes[pc] = Inst.SetSj(jmp, offset);
        }

        internal static void patchlist(FuncState fs, int list, int target)
        {

        }

        internal static void integer(FuncState fs, int reg, int i)
        {

        }

        internal static void floatnum(FuncState fs, int reg, double i)
        {

        }

        internal static void checkstack(FuncState fs, int n)
        {

        }

        internal static void jumpto(FuncState fs, int target)
        {
            patchlist(fs, jump(fs), target);
        }

    }
}

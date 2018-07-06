using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TLua
{
    internal static class Code
    {
        internal const int MaxArgB = (1 << 8) - 1;
        internal const int MaxArgBx = (1 << 16) - 1;
        internal const int MaxArgC = (1 << 8) - 1;

        /* semantic error */
        internal static void semerror(Lexer ls, string msg)
        {
            //ls.t.token = 0;  /* remove "near <token>" from final message */
            ls.syntaxerror(msg);
        }

        static bool hasjumps(Parser.ExpDesc e)
        {
            return e.t != e.f;
        }

        /*
        ** Check whether expression 'e' is a small literal string
        */
        internal static bool isKstr(Parser.FuncState fs, Parser.ExpDesc e)
        {
            return (e.k == Parser.ExpKind.Const) && !hasjumps(e) && (e.info <= MaxArgB) && fs.f.Consts[e.info].IsString;
        }

        /*
        ** Check whether expression 'e' is a literal integer.
        */
        internal static bool isKint(Parser.ExpDesc e)
        {
            return e.k == Parser.ExpKind.Int && !hasjumps(e);
        }


        /*
        ** Check whether expression 'e' is a literal integer in
        ** proper range to fit in register C
        */
        internal static bool isCint(Parser.ExpDesc e)
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

        /*
        ** Ensures final expression result is in some (any) register
        ** and return that register.
        */
        static int luaK_exp2anyreg(Parser.FuncState fs, Parser.ExpDesc e)
        {
            luaK_dischargevars(fs, e);
            if (e.k == Parser.ExpKind.NonReloc)
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
            luaK_exp2nextreg(fs, e);  /* otherwise, use next available register */
            return e.info;
        }

        /*
        ** Create expression 't[k]'. 't' must have its final result already in a
        ** register or upvalue. Upvalues can only be indexed by literal strings.
        ** Keys can be literal strings in the constant table or arbitrary
        ** values in registers.
        */
        internal static void indexed(Parser.FuncState fs, Parser.ExpDesc t, Parser.ExpDesc k)
        {
            // Lexer.assert(!hasjumps(t) && (vkisinreg(t->k) || t->k == VUPVAL));
            if (t.k == Parser.ExpKind.Upval && !isKstr(fs, k))
            {
                /* upvalue indexed by non string? */
                luaK_exp2anyreg(fs, t);  /* put it in a register */
            }
            t.idxT = t.info;  /* register or upvalue index */
            if (t.k == Parser.ExpKind.Upval)
            {
                t.idxIdx = k.info;  /* literal string */
                t.k = Parser.ExpKind.IndexUp;
            }
            else if (isKstr(fs, k))
            {
                t.idxIdx = k.info;  /* literal string */
                t.k = Parser.ExpKind.IndexString;
            }
            else if (isCint(k))
            {
                t.idxIdx = k.ival;  /* integer constant in proper range */
                t.k = Parser.ExpKind.IndexInt;
            }
            else
            {
                t.idxIdx = luaK_exp2anyreg(fs, k);  /* register */
                t.k = Parser.ExpKind.Indexed;
            }
        }

        internal static void setmultret(Parser.FuncState fs, Parser.ExpDesc e)
        {
            setreturns(fs, e, Parser.LUA_MULTRET);
        }

        internal static void setreturns(Parser.FuncState fs, Parser.ExpDesc e, int extra)
        {

        }

        internal static void reserveregs(Parser.FuncState fs, int n)
        {

        }

        internal static void exp2nextreg(Parser.FuncState fs, Parser.ExpDesc e)
        {

        }

        internal static void exp2anyregup(Parser.FuncState fs, Parser.ExpDesc e)
        {
        }

        internal static void exp2val(Parser.FuncState fs, Parser.ExpDesc e)
        {
        }

        internal static void storevar(Parser.FuncState fs, Parser.ExpDesc var, Parser.ExpDesc ex)
        {
        }

        internal static void setlist(Parser.FuncState fs, int base_, int nelems, int tostore)
        {
        }

        internal static void patchgoto(Parser.FuncState fs, int list, int target, bool hasclose)
        {


        }


        internal static void patchclose(Parser.FuncState fs, int list)
        {


        }

        internal static void concat(Parser.FuncState fs, ref int l1, int l2)
        {

        }

        internal static int codeABCk(Parser.FuncState fs, OpCode o, int a, int b, int c, int k)
        {
            return 0; // TODO
        }

        internal static int codeABC(Parser.FuncState fs, OpCode o, int a, int b, int c)
        {
            return codeABCk(fs, o, a, b, c, 0);
        }

        internal static int codeABx(Parser.FuncState fs, OpCode o, int a, int bc)
        {
            return codeABCk(fs, o, a, bc, 0, 0);
        }

        internal static void nil(Parser.FuncState fs, int from, int n)
        {

        }

        internal static void ret(Parser.FuncState fs, int first, int nret)
        {
        }

        internal static void finish(Parser.FuncState fs)
        {
        }
    }
}

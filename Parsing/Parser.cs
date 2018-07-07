using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using TLua;
using System.Diagnostics;

namespace TLua.Parsing
{
    using Proto = Function;

    internal enum ExpKind
    {
        Void,
        Nil,
        True,
        False,  /* constant false */
        Const,  /* constant in 'k'; info = index of constant in 'k' */
        Float,  /* floating constant; nval = numerical float value */
        Int,  /* integer constant; nval = numerical integer value */
        NonReloc,  /* expression has its value in a fixed register;
                 info = result register */
        Local,  /* local variable; info = local register */
        Upval,  /* upvalue variable; info = index of upvalue in 'upvalues' */
        Indexed,  /* indexed variable;
                ind.t = table register;
                ind.idx = key's R index */
        IndexUp,  /* indexed upvalue;
                ind.t = table upvalue;
                ind.idx = key's K index */
        IndexInt, /* indexed variable with constant integer;
                ind.t = table register;
                ind.idx = key's value */
        IndexString, /* indexed variable with literal string;
                ind.t = table register;
                ind.idx = key's K index */
        Jump,  /* expression is a test/comparison;
            info = pc of corresponding jump instruction */
        Reloc,  /* expression can put result in any register;
              info = instruction pc */
        Call,  /* expression is a function call; info = instruction pc */
        VarArg,  /* vararg expression; info = instruction pc */
        Undef  /* the 'undef' "expression" */
    }

    internal class ExpDesc
    {
        internal ExpKind k;
        internal int ival;
        internal double nval;
        internal int info;  /* for generic use */
        internal int indIdx; /* index (R or "long" K) */
        internal int indT; /* table (register or upvalue) */
        internal int t;  /* patch list of 'exit when true' */
        internal int f;  /* patch list of 'exit when false' */
    }

    /* description of active local variable */
    internal struct VarDesc
    {
        internal short idx;  /* variable index in stack */
    }


    /* description of pending goto statements and label statements */
    internal class LabelDesc
    {
        internal string name;  /* label identifier */
        internal int pc;  /* position in code */
        internal int line;  /* line where it appeared */
        internal int nactvar;  /* local level where it appears in current block */
    }

    /* list of labels or gotos */
    internal class LabelList
    {
        internal List<LabelDesc> arr = new List<LabelDesc>();  /* array */
        internal int n;  /* number of entries in use */
        internal int size;  /* array size */
    }


    /* dynamic structures used by the parser */
    internal class DynData
    {
        internal List<VarDesc> arr = new List<VarDesc>();
        internal int n;
        internal int size;
        internal LabelList gt = new LabelList();  /* list of pending gotos */
        internal LabelList label = new LabelList();   /* list of active labels */
    }

    /* control of blocks */
    //struct BlockCnt;  /* defined in lparser.c */


    /* state needed to generate code for a given function */
    internal class FuncState
    {
        internal Function f;  /* current function header */
        internal FuncState prev;  /* enclosing function */
        internal Lexer ls;  /* lexical state */
        internal BlockCnt bl;  /* chain of current blocks */
        internal int pc;  /* next position to code (equivalent to 'ncode') */
        internal int lasttarget;   /* 'label' of last 'jump label' */
        internal int previousline;  /* last line that was saved in 'lineinfo' */
        internal int nk;  /* number of elements in 'k' */
        internal int np;  /* number of elements in 'p' */
        internal int nabslineinfo;  /* number of elements in 'abslineinfo' */
        internal int firstlocal;  /* index of first local var (in Dyndata array) */
        internal short nlocvars;  /* number of elements in 'f.locvars' */
        internal int nactvar;  /* number of active local variables */
        internal byte nups;  /* number of upvalues */
        internal int freereg;  /* first free register */
        internal byte iwthabs;  /* instructions issued since last absolute line info */
    }

    /*
    ** nodes for block list (list of active blocks)
    */
    internal class BlockCnt
    {
        internal BlockCnt previous;  /* chain */
        internal int firstlabel;  /* index of first label in this block */
        internal int firstgoto;  /* index of first pending goto in this block */
        internal int brks;  /* list of break jumps in this block */
        internal bool brkcls;  /* true if some 'break' needs to close upvalues */
        internal int nactvar;  /* # active locals outside the block */
        internal bool upval;  /* true if some variable in the block is an upvalue */
        internal bool isloop;  /* true if 'block' is a loop */
    }

    internal enum UnOpr
    {
        MINUS, BNOT, NOT, LEN, NOUNOPR
    }

    internal enum BinOpr
    {
        ADD, SUB, MUL, MOD, POW,
        DIV,
        IDIV,
        BAND, BOR, BXOR,
        SHL, SHR,
        CONCAT,
        EQ, LT, LE,
        NE, GT, GE,
        AND, OR,
        NOBINOPR
    }

    public class Parser
    {
        //#define vkisvar(k)	(VLOCAL <= (k) && (k) <= VINDEXSTR)
        //#define vkisindexed(k)	(VINDEXED <= (k) && (k) <= VINDEXSTR)
        //#define vkisinreg(k)	((k) == VNONRELOC || (k) == VLOCAL)


        public Parser()
        {
        }


        /* maximum number of local variables per function (must be smaller
           than 250, due to the bytecode format) */
        const int MaxVars = 200;


        static bool hasmultret(ExpKind k)
        {
            return k == ExpKind.Call || k == ExpKind.VarArg;
        }


        /* because all strings are unified by the scanner, the parser
           can use pointer equality for string equality */
        static bool eqstr(string a, string b) {
            return a == b;
        }


        /*
        ** prototypes for recursive non-terminal functions
        */
        //static void statement (Lexer ls);
        //static void expr (Lexer ls, ExpDesc v);


        void error_expected(Lexer ls, TokenKind token) {
            ls.syntaxerror(string.Format("{0} expected", Lexer.token2str(token)));
        }


        void errorlimit(FuncState fs, int limit, string what)
        {
            int line = fs.f.LineStart;
            string where = (line == 0) ? "main function" : string.Format("function at line {0}", line);
            var msg = string.Format("too many {0} (limit is {1}) in {2}", what, limit, where);
            fs.ls.syntaxerror(msg);
        }


        void checklimit(FuncState fs, int v, int l, string what)
        {
            if (v > l)
            {
                errorlimit(fs, l, what);
            }
        }


        bool testnext(Lexer ls, TokenKind c) {
            if (ls.Tk.token == c)
            {
                ls.ReadNext();
                return true;
            }
            else
            {
                return false;
            }
        }

        bool testnext(Lexer ls, char c)
        {
            return testnext(ls, (TokenKind)c);
        }

        void check(Lexer ls, TokenKind c)
        {
            if (ls.Tk.token != c)
            {
                error_expected(ls, c);
            }
        }


        void checknext(Lexer ls, char c)
        {
            checknext(ls, (TokenKind)c);
        }

        void checknext(Lexer ls, TokenKind c)
        {
            check(ls, c);
            ls.ReadNext();
        }


        void check_condition(Lexer ls, bool c, string msg)
        {
            if (!c) {
                ls.syntaxerror(msg);
            }
        }



        void check_match(Lexer ls, TokenKind what, TokenKind who, int where)
        {
            if (!testnext(ls, what)) {
                if (where == ls.linenumber)
                    error_expected(ls, what);
                else {
                    ls.syntaxerror(string.Format("{0} expected (to close {1} at line {2})", Lexer.token2str(what), Lexer.token2str(who), where));
                }
            }
        }
        void check_match(Lexer ls, char what, char who, int where)
        {
            check_match(ls, (TokenKind)what, (TokenKind)who, where);
        }

        static bool EnableTrace = true;

        [Conditional("DEBUG")]
        internal static void trace(params object[] args)
        {
            if (EnableTrace)
            {
                Console.Write("TRACE: ");
                foreach (var arg in args)
                {
                    Console.Write(arg);
                    Console.Write(", ");
                }
                Console.WriteLine("");
            }
        }

        string str_checkname(Lexer ls) {
            check(ls, TokenKind.Name);
            var ts = ls.Tk.ts;
            ls.ReadNext();
            return ts;
        }

        internal const int NoJump = -1;

        void init_exp(ExpDesc e, ExpKind k, int i)
        {
            e.f = NoJump;
            e.t = NoJump;
            e.k = k;
            e.info = i;
            /*
            return new ExpDesc {
                f = NoJump,
                t = NoJump,
                k = k,
                info = i,
            };
            */
        }


        void codestring(Lexer ls, ExpDesc e, string s)
        {
            init_exp(e, ExpKind.Const, ls.fs.f.AddConst(new LuaValue(s)));
        }


        void checkname(Lexer ls, ExpDesc e)
        {
            codestring(ls, e, str_checkname(ls));
        }


        int registerlocalvar(Lexer ls, string varname)
        {
            FuncState fs = ls.fs;
            Function f = fs.f;
            int oldsize = f.MaxStackSize;
            f.LocalVars.Add(new LocVar { varname = varname });
            return f.LocalVars.Count-1;
        }


        void new_localvar(Lexer ls, string name) {
            FuncState fs = ls.fs;
            DynData dyd = ls.dyd;
            int reg = registerlocalvar(ls, name);
            checklimit(fs, dyd.n + 1 - fs.firstlocal, MaxVars, "local variables");
            dyd.arr.Add(new VarDesc { idx = (short)reg });
        }


        void new_localvarliteral_(Lexer ls, string name)
        {
            new_localvar(ls, name);
        }

        void new_localvarliteral(Lexer ls, string v)
        {
            new_localvarliteral_(ls, v);
        }

        internal class LocVar
        {
            internal string varname;
            internal int startpc;  /* first point where variable is active */
            internal int endpc;    /* first point where variable is dead */
        }

        LocVar getlocvar(FuncState fs, int i)
        {
            int idx = fs.ls.dyd.arr[fs.firstlocal + i].idx;
            Lexer.assert(idx < fs.f.LocalVars.Count);
            return fs.f.LocalVars[idx];
        }


        void adjustlocalvars(Lexer ls, int nvars)
        {
            FuncState fs = ls.fs;
            fs.nactvar = fs.nactvar + nvars;
            for (; nvars != 0; nvars--) {
                getlocvar(fs, fs.nactvar - nvars).startpc = fs.pc;
            }
        }


        void removevars(FuncState fs, int tolevel)
        {
            fs.ls.dyd.n -= (fs.nactvar - tolevel);
            while (fs.nactvar > tolevel)
            {
                getlocvar(fs, --fs.nactvar).endpc = fs.pc;
            }
        }


        int searchupvalue(FuncState fs, string name)
        {
            int i;
            var up = fs.f.Upvals;
            for (i = 0; i < fs.nups; i++) {
                if (up[i].Name == name)
                {
                    return i;
                }
            }
            return -1;  /* not found */
        }

        const int MaxUpval = 255;

        int newupvalue(FuncState fs, string name, ExpDesc v) {
            var f = fs.f;
            int oldsize = f.Upvals.Count;
            checklimit(fs, fs.nups + 1, MaxUpval, "upvalues");
            f.Upvals.Add(new UpvalTag
            {
                Name = name,
                InStack = (v.k == ExpKind.Local) ? (byte)1 : (byte)0,
                Index = (byte)v.info,
            });
            return fs.nups++;
        }


        int searchvar(FuncState fs, string n)
        {
            for (var i = fs.nactvar - 1; i >= 0; i--) {
                if (n == getlocvar(fs, i).varname)
                {
                    return i;
                }
            }
            return -1;  /* not found */
        }


        /*
          Mark block where variable at given level was defined
          (to emit close instructions later).
        */
        void markupval(FuncState fs, int level)
        {
            BlockCnt bl = fs.bl;
            while (bl.nactvar > level)
            {
                bl = bl.previous;
            }
            bl.upval = true;
        }


        /*
          Find variable with given name 'n'. If it is an upvalue, add this
          upvalue into all intermediate functions.
        */
        void singlevaraux(FuncState fs, string n, ExpDesc var, bool base_) {
            if (fs == null)
            {
                /* no more levels? */
                init_exp(var, ExpKind.Void, 0);  /* default is global */
            }
            else
            {
                int v = searchvar(fs, n);  /* look up locals at current level */
                if (v >= 0)
                {  /* found? */
                    init_exp(var, ExpKind.Local, v);  /* variable is local */
                    if (!base_)
                    {
                        markupval(fs, v);  /* local will be used as an upval */
                    }
                }
                else
                {  /* not found as local at current level; try upvalues */
                    int idx = searchupvalue(fs, n);  /* try existing upvalues */
                    if (idx < 0)
                    {
                        /* not found? */
                        singlevaraux(fs.prev, n, var, false);  /* try upper levels */
                        if (var.k == ExpKind.Void)
                        {
                            /* not found? */
                            return;  /* it is a global */
                        }
                        /* else was LOCAL or UPVAL */
                        idx = newupvalue(fs, n, var);  /* will be a new upvalue */
                    }
                    init_exp(var, ExpKind.Upval, idx);  /* new or old upvalue */
                }
            }
        }

        void singlevar(Lexer ls, ExpDesc var)
        {
            var varname = str_checkname(ls);
            FuncState fs = ls.fs;
            singlevaraux(fs, varname, var, true);
            if (var.k == ExpKind.Void) {  /* global name? */
                ExpDesc key = new ExpDesc();
                singlevaraux(fs, ls.envn, var, true);  /* get environment variable */
                Lexer.assert(var.k != ExpKind.Void);  /* this one must exist */
                codestring(ls, key, varname);  /* key is variable name */
                Code.indexed(fs, var, key);  /* env[varname] */
            }
        }

        void adjust_assign(Lexer ls, int nvars, int nexps, ExpDesc e) {
            FuncState fs = ls.fs;
            int extra = nvars - nexps;
            if (hasmultret(e.k)) {
                extra++;  /* includes call itself */
                if (extra < 0) extra = 0;
                Code.setreturns(fs, e, extra);  /* last exp. provides the difference */
                if (extra > 1) Code.reserveregs(fs, extra - 1);
            }
            else {
                if (e.k != ExpKind.Void) Code.exp2nextreg(fs, e);  /* close last expression */
                if (extra > 0) {
                    int reg = fs.freereg;
                    Code.reserveregs(fs, extra);
                    Code.nil(fs, reg, extra);
                }
            }
            if (nexps > nvars)
            {
                ls.fs.freereg -= nexps - nvars;  /* remove extra values */
            }
        }


        static void enterlevel(Lexer ls)
        {
            //luaE_incCcalls((ls).L);
        }


        static void leavelevel(Lexer ls)
        {
            //((ls).L.nCcalls--);
        }

        void closegoto(Lexer ls, int g, LabelDesc label)
        {
            int i;
            FuncState fs = ls.fs;
            LabelList gl = ls.dyd.gt;
            LabelDesc gt = gl.arr[g];
            Lexer.assert(gt.name == label.name);
            if (gt.nactvar < label.nactvar) {
                var vname = getlocvar(fs, gt.nactvar).varname;
                var msg = string.Format("<goto {0}> at line {1} jumps into the scope of local '{2}'", gt.name, gt.line, vname);
                Code.semerror(ls, msg);
            }
            Code.patchgoto(fs, gt.pc, label.pc, true);
            /* remove goto from pending list */
            for (i = g; i < gl.n - 1; i++)
                gl.arr[i] = gl.arr[i + 1];
            gl.n--;
        }


        /*
        ** try to close a goto with existing labels; this solves backward jumps
        */
        bool solvelabel(Lexer ls, int g) {
            int i;
            BlockCnt bl = ls.fs.bl;
            DynData dyd = ls.dyd;
            LabelDesc gt = dyd.gt.arr[g];
            /* check labels in current block for a match */
            for (i = bl.firstlabel; i < dyd.label.n; i++) {
                LabelDesc lb = dyd.label.arr[i];
                if (eqstr(lb.name, gt.name)) {  /* correct label? */
                    if (gt.nactvar > lb.nactvar &&
                        (bl.upval || dyd.label.n > bl.firstlabel))
                    {
                        Code.patchclose(ls.fs, gt.pc);
                    }
                    closegoto(ls, g, lb);  /* close it */
                    return true;
                }
            }
            return false;  /* label not found; cannot close goto */
        }


        int newlabelentry(Lexer ls, LabelList l, string name, int line, int pc)
        {
            l.arr.Add(new LabelDesc
            {
                name = name,
                line = line,
                nactvar = ls.fs.nactvar,
                pc = pc,
            });
            return l.arr.Count - 1;
        }


        /*
        ** check whether new label 'lb' matches any pending gotos in current
        ** block; solves forward jumps
        */
        void solvegotos(Lexer ls, LabelDesc lb) {
            LabelList gl = ls.dyd.gt;
            int i = ls.fs.bl.firstgoto;
            while (i < gl.n) {
                if (eqstr(gl.arr[i].name, lb.name))
                    closegoto(ls, i, lb);  /* will remove 'i' from the list */
                else
                    i++;
            }
        }


        /*
        ** export pending gotos to outer level, to check them against
        ** outer labels; if the block being exited has upvalues, and
        ** the goto exits the scope of any variable (which can be the
        ** upvalue), close those variables being exited. Also export
        ** break list.
        */
        void movegotosout(FuncState fs, BlockCnt bl) {
            int i = bl.firstgoto;
            LabelList gl = fs.ls.dyd.gt;
            /* correct pending gotos to current block and try to close it
               with visible labels */
            while (i < gl.n) {  /* for each pending goto */
                LabelDesc gt = gl.arr[i];
                if (gt.nactvar > bl.nactvar) {  /* leaving a variable scope? */
                    if (bl.upval)
                    { /* variable may be an upvalue? */
                        Code.patchclose(fs, gt.pc);  /* jump will need a close */
                    }
                    gt.nactvar = bl.nactvar;  /* update goto level */
                }
                if (!solvelabel(fs.ls, i))
                {
                    i++;  /* move to next one */
                          /* else, 'solvelabel' removed current goto from the list
                             and 'i' now points to next one */
                }
            }
            /* handles break list */
            if (bl.upval)
            { /* exiting the scope of an upvalue? */
                Code.patchclose(fs, bl.brks);  /* breaks will need OpCode.CLOSE */
                                               /* move breaks to outer block */
            }
            Code.concat(fs, ref bl.previous.brks, bl.brks);
            bl.previous.brkcls |= bl.brkcls;
        }


        void enterblock(FuncState fs, BlockCnt bl, bool isloop)
        {
            bl.isloop = isloop;
            bl.nactvar = fs.nactvar;
            bl.firstlabel = fs.ls.dyd.label.n;
            bl.firstgoto = fs.ls.dyd.gt.n;
            bl.brks = NoJump;
            bl.brkcls = false;
            bl.upval = false;
            bl.previous = fs.bl;
            fs.bl = bl;
            Lexer.assert(fs.freereg == fs.nactvar);
        }


        /*
        ** Fix all breaks in block 'bl' to jump to the end of the block.
        */
        void fixbreaks(FuncState fs, BlockCnt bl) {
            int target = fs.pc;
            if (bl.brkcls)
            {
                /* does the block need to close upvalues? */
                Code.codeABC(fs, OpCode.CLOSE, bl.nactvar, 0, 0);
            }
            Code.patchgoto(fs, bl.brks, target, bl.brkcls);
            bl.brks = NoJump;  /* no more breaks to fix */
            bl.brkcls = false;  /* no more need to close upvalues */
            Lexer.assert(!bl.upval);  /* loop body cannot have local variables */
        }


        /*
        ** generates an error for an undefined 'goto'.
        */
        static void undefgoto(Lexer ls, LabelDesc gt)
        {
            var msg = string.Format("no visible label '{0}' for <goto> at line {1}", gt.name, gt.line);
            Code.semerror(ls, msg);
        }


        void leaveblock(FuncState fs) {
            BlockCnt bl = fs.bl;
            Lexer ls = fs.ls;
            if (bl.upval && bl.brks != NoJump)  /* breaks in upvalue scopes? */
                bl.brkcls = true;  /* these breaks must close the upvalues */
            if (bl.isloop)
                fixbreaks(fs, bl);  /* fix pending breaks */
            if (bl.previous != null && bl.upval)
            {
                Code.codeABC(fs, OpCode.CLOSE, bl.nactvar, 0, 0);
            }
            fs.bl = bl.previous;
            removevars(fs, bl.nactvar);
            Lexer.assert(bl.nactvar == fs.nactvar);
            fs.freereg = fs.nactvar;  /* free registers */
            ls.dyd.label.n = bl.firstlabel;  /* remove local labels */
            if (bl.previous != null)
            { /* inner block? */
                movegotosout(fs, bl);  /* update pending gotos to outer block */
            }
            else
            {
                Lexer.assert(bl.brks == NoJump);  /* no pending breaks */
                if (bl.firstgoto < ls.dyd.gt.n)  /* pending gotos in outer block? */
                    undefgoto(ls, ls.dyd.gt.arr[bl.firstgoto]);  /* error */
            }
        }


        /*
        ** adds a new prototype into list of prototypes
        */
        Function addprototype(Lexer ls)
        {
            Function clp = new Function();
            ls.fs.f.Protos.Add(clp);
            return clp;
        }


        /*
        ** codes instruction to create new closure in parent function.
        ** The OpCode.CLOSURE instruction must use the last available register,
        ** so that, if it invokes the GC, the GC knows which registers
        ** are in use at that time.
        */
        void codeclosure(Lexer ls, ExpDesc v) {
            FuncState fs = ls.fs.prev;
            init_exp(v, ExpKind.Reloc, Code.codeABx(fs, OpCode.CLOSURE, 0, fs.np - 1));
            Code.exp2nextreg(fs, v);  /* fix it at the last register */
        }


        void open_func(Lexer ls, FuncState fs, BlockCnt bl)
        {
            Proto f = fs.f;
            fs.prev = ls.fs;  /* linked list of funcstates */
            fs.ls = ls;
            ls.fs = fs;
            fs.pc = 0;
            //fs.previousline = f.linedefined;
            fs.iwthabs = 0;
            fs.lasttarget = 0;
            fs.freereg = 0;
            fs.nk = 0;
            fs.nabslineinfo = 0;
            fs.np = 0;
            fs.nups = 0;
            fs.nlocvars = 0;
            fs.nactvar = 0;
            fs.firstlocal = ls.dyd.n;
            fs.bl = null;
            //f.source = ls.source;
            //f.maxstacksize = 2;  /* registers 0/1 are always valid */
            enterblock(fs, bl, false);
        }


        void close_func(Lexer ls)
        {
            FuncState fs = ls.fs;
            Proto f = fs.f;
            Code.ret(fs, 0, 0);  /* final return */
            leaveblock(fs);
            Lexer.assert(fs.bl == null);
            Code.finish(fs);
            // TODO
            /*
            luaM_shrinkvector(L, f.code, f.sizecode, fs.pc, Instruction);
            luaM_shrinkvector(L, f.lineinfo, f.sizelineinfo, fs.pc, ls_byte);
            luaM_shrinkvector(L, f.abslineinfo, f.sizeabslineinfo,
                                 fs.nabslineinfo, AbsLineInfo);
            luaM_shrinkvector(L, f.k, f.sizek, fs.nk, TValue);
            luaM_shrinkvector(L, f.p, f.sizep, fs.np, Proto *);
            luaM_shrinkvector(L, f.locvars, f.sizelocvars, fs.nlocvars, LocVar);
            luaM_shrinkvector(L, f.upvalues, f.sizeupvalues, fs.nups, Upvaldesc);
            */
            ls.fs = fs.prev;
        }



        /*============================================================*/
        /* GRAMMAR RULES */
        /*============================================================*/


        /*
        ** check whether current token is in the follow set of a block.
        ** 'until' closes syntactical blocks, but do not close scope,
        ** so it is handled in separate.
        */
        static bool block_follow(Lexer ls, bool withuntil) {
            switch (ls.Tk.token) {
                case TokenKind.Else:
                case TokenKind.ElseIf:
                case TokenKind.End:
                case TokenKind.Eos:
                    return true;
                case TokenKind.Until:
                    return withuntil;
                default:
                    return false;
            }
        }


        void statlist(Lexer ls) {
            /* statlist . { stat [';'] } */
            while (!block_follow(ls, true)) {
                if (ls.Tk.token == TokenKind.Return) {
                    statement(ls);
                    return;  /* 'return' must be last statement */
                }
                statement(ls);
            }
        }


        void fieldsel(Lexer ls, ExpDesc v)
        {
            /* fieldsel . ['.' | ':'] NAME */
            FuncState fs = ls.fs;
            ExpDesc key = new ExpDesc();
            Code.exp2anyregup(fs, v);
            ls.ReadNext();  /* skip the dot or colon */
            checkname(ls, key);
            Code.indexed(fs, v, key);
        }


        void yindex(Lexer ls, ExpDesc v)
        {
            /* index . '[' expr ']' */
            ls.ReadNext();  /* skip the '[' */
            expr(ls, v);
            Code.exp2val(ls.fs, v);
            checknext(ls, (TokenKind)']');
        }


        /*
        ** {======================================================================
        ** Rules for Constructors
        ** =======================================================================
        */


        internal struct ConsControl {
            internal ExpDesc v;  /* last list item read */
            internal ExpDesc t;  /* table descriptor */
            internal int nh;  /* total number of 'record' elements */
            internal int na;  /* total number of array elements */
            internal int tostore;  /* number of array elements pending to be stored */
        }


        void recfield(Lexer ls, ConsControl cc)
        {
            /* recfield . (NAME | '['exp']') = exp */
            FuncState fs = ls.fs;
            int reg = ls.fs.freereg;
            ExpDesc tab;
            ExpDesc key = new ExpDesc();
            ExpDesc val = new ExpDesc();
            if (ls.Tk.token == TokenKind.Name)
            {
                checklimit(fs, cc.nh, Int32.MaxValue, "items in a constructor");
                checkname(ls, key);
            }
            else
            {  /* ls.t.token == '[' */
                yindex(ls, key);
            }
            cc.nh++;
            checknext(ls, '=');
            tab = cc.t;
            Code.indexed(fs, tab, key);
            expr(ls, val);
            Code.storevar(fs, tab, val);
            fs.freereg = reg;  /* free registers */
        }


        internal const int LFIELDS_PER_FLUSH = 50;

        void closelistfield(FuncState fs, ConsControl cc)
        {
            if (cc.v.k == ExpKind.Void)
            {
                return;  /* there is no list item */
            }
            Code.exp2nextreg(fs, cc.v);
            cc.v.k = ExpKind.Void;
            if (cc.tostore == LFIELDS_PER_FLUSH) {
                Code.setlist(fs, cc.t.info, cc.na, cc.tostore);  /* flush */
                cc.tostore = 0;  /* no more items pending */
            }
        }

        internal const int LUA_MULTRET = -1;

        void lastlistfield(FuncState fs, ConsControl cc)
        {
            if (cc.tostore == 0)
            {
                return;
            }
            if (hasmultret(cc.v.k)) {
                Code.setmultret(fs, cc.v);
                Code.setlist(fs, cc.t.info, cc.na, LUA_MULTRET);
                cc.na--;  /* do not count last expression (unknown number of elements) */
            }
            else
            {
                if (cc.v.k != ExpKind.Void)
                {
                    Code.exp2nextreg(fs, cc.v);
                }
                Code.setlist(fs, cc.t.info, cc.na, cc.tostore);
            }
        }


        void listfield(Lexer ls, ConsControl cc)
        {
            /* listfield . exp */
            expr(ls, cc.v);
            checklimit(ls.fs, cc.na, Int32.MaxValue, "items in a constructor");
            cc.na++;
            cc.tostore++;
        }


        void field(Lexer ls, ConsControl cc) {
            /* field . listfield | recfield */
            switch (ls.Tk.token) {
                case TokenKind.Name: {  /* may be 'listfield' or 'recfield' */
                        if (ls.ReadLookahead() != (TokenKind)'=')
                        {
                            /* expression? */
                            listfield(ls, cc);
                        }
                        else
                        {
                            recfield(ls, cc);
                        }
                        break;
                    }
                case (TokenKind)'[':
                    {
                        recfield(ls, cc);
                        break;
                    }
                default:
                    {
                        listfield(ls, cc);
                        break;
                    }
            }
        }


        void constructor(Lexer ls, ExpDesc t)
        {
            /* constructor . '{' [ field { sep field } [sep] ] '}'
               sep . ',' | ';' */
            FuncState fs = ls.fs;
            int line = ls.linenumber;
            int pc = Code.codeABC(fs, OpCode.NEWTABLE, 0, 0, 0);
            ConsControl cc = new ConsControl();
            cc.na = cc.nh = cc.tostore = 0;
            cc.t = t;
            init_exp(t, ExpKind.Reloc, pc);
            init_exp(cc.v, ExpKind.Void, 0);  /* no value (yet) */
            Code.exp2nextreg(ls.fs, t);  /* fix it at stack top */
            checknext(ls, '{');
            do {
                Lexer.assert(cc.v.k == ExpKind.Void || cc.tostore > 0);
                if (ls.Tk.token.c() == '}') break;
                closelistfield(fs, cc);
                field(ls, cc);
            } while (testnext(ls, ',') || testnext(ls, ';'));
            check_match(ls, '}', '{', line);
            lastlistfield(fs, cc);
            var inst = fs.f.Codes[pc];
            inst = Inst.SetB(inst, Code.int2fb((uint)cc.na));
            inst = Inst.SetC(inst, Code.int2fb((uint)cc.nh));
        }

        /* }====================================================================== */


        void setvararg(FuncState fs, int nparams)
        {
            fs.f.HasVarArg = true;
            Code.codeABC(fs, OpCode.PREPVARARG, nparams, 0, 0);
        }


        void parlist(Lexer ls)
        {
            /* parlist . [ param { ',' param } ] */
            FuncState fs = ls.fs;
            Proto f = fs.f;
            int nparams = 0;
            bool isvararg = false;
            if (ls.Tk.token != (TokenKind)')') {  /* is 'parlist' not empty? */
                do {
                    switch (ls.Tk.token) {
                        case TokenKind.Name: {  /* param . NAME */
                                new_localvar(ls, str_checkname(ls));
                                nparams++;
                                break;
                            }
                        case TokenKind.Dots: {  /* param . '...' */
                                ls.ReadNext();
                                isvararg = true;
                                break;
                            }
                        default:
                            ls.syntaxerror("<name> or '...' expected");
                            break;
                    }
                } while (!isvararg && testnext(ls, ','));
            }
            adjustlocalvars(ls, nparams);
            f.ParamNum = (byte)fs.nactvar;
            if (isvararg)
            {
                setvararg(fs, f.ParamNum);  /* declared vararg */
            }
            Code.reserveregs(fs, fs.nactvar);  /* reserve registers for parameters */
        }


        void body(Lexer ls, ExpDesc e, bool ismethod, int line) {
            /* body .  '(' parlist ')' block END */
            FuncState new_fs = new FuncState();
            BlockCnt bl = new BlockCnt();
            new_fs.f = addprototype(ls);
            new_fs.f.LineStart = line;
            open_func(ls, new_fs, bl);
            checknext(ls, '(');
            if (ismethod) {
                new_localvarliteral(ls, "self");  /* create 'self' parameter */
                adjustlocalvars(ls, 1);
            }
            parlist(ls);
            checknext(ls, ')');
            statlist(ls);
            new_fs.f.LineEnd = ls.linenumber;
            check_match(ls, TokenKind.End, TokenKind.Function, line);
            codeclosure(ls, e);
            close_func(ls);
        }


        int explist(Lexer ls, ExpDesc v)
        {
            /* explist . expr { ',' expr } */
            int n = 1;  /* at least one expression */
            expr(ls, v);
            while (testnext(ls, ',')) {
                Code.exp2nextreg(ls.fs, v);
                expr(ls, v);
                n++;
            }
            return n;
        }


        void funcargs(Lexer ls, ExpDesc f, int line)
        {
            FuncState fs = ls.fs;
            ExpDesc args = new ExpDesc();
            int nparams;
            switch (ls.Tk.token) {
                case (TokenKind)'(': {  /* funcargs . '(' [ explist ] ')' */
                        ls.ReadNext();
                        if (ls.Tk.token == (TokenKind)')')  /* arg list is empty? */
                            args.k = ExpKind.Void;
                        else {
                            explist(ls, args);
                            Code.setmultret(fs, args);
                        }
                        check_match(ls, ')', '(', line);
                        break;
                    }
                case (TokenKind)'{': {  /* funcargs . constructor */
                        constructor(ls, args);
                        break;
                    }
                case TokenKind.String: {  /* funcargs . STRING */
                        codestring(ls, args, ls.Tk.ts);
                        ls.ReadNext();  /* must use 'seminfo' before 'next' */
                        break;
                    }
                default:
                    {
                        ls.syntaxerror("function arguments expected");
                        break;
                    }
            }
            Lexer.assert(f.k == ExpKind.NonReloc);
            var base_ = f.info;  /* base register for call */
            if (hasmultret(args.k))
            {
                nparams = LUA_MULTRET;  /* open call */
            }
            else
            {
                if (args.k != ExpKind.Void)
                {
                    Code.exp2nextreg(fs, args);  /* close last argument */
                }
                nparams = fs.freereg - (base_ + 1);
            }
            init_exp(f, ExpKind.Call, Code.codeABC(fs, OpCode.CALL, base_, nparams + 1, 2));
            Code.fixline(fs, line);
            fs.freereg = base_ + 1;  /* call remove function and arguments and leaves
                            (unless changed) one result */
        }

        /*
        ** {======================================================================
        ** Expression parsing
        ** =======================================================================
        */

        void primaryexp(Lexer ls, ExpDesc v)
        {
            /* primaryexp . NAME | '(' expr ')' */
            switch (ls.Tk.token) {
                case (TokenKind)'(': {
                        int line = ls.linenumber;
                        ls.ReadNext();
                        expr(ls, v);
                        check_match(ls, ')', '(', line);
                        Code.dischargevars(ls.fs, v);
                        return;
                    }
                case TokenKind.Name: {
                        singlevar(ls, v);
                        return;
                    }
                case TokenKind.Undef: {
                        ls.ReadNext();
                        init_exp(v, ExpKind.Undef, 0);
                        return;
                    }
                default: 
                    {
                        ls.syntaxerror("unexpected symbol");
                        break;
                    }
            }
        }


        void suffixedexp(Lexer ls, ExpDesc v) {
            /* suffixedexp .
                 primaryexp { '.' NAME | '[' exp ']' | ':' NAME funcargs | funcargs } */
            FuncState fs = ls.fs;
            int line = ls.linenumber;
            primaryexp(ls, v);
            for (;;) {
                switch (ls.Tk.token) {
                    case (TokenKind)'.': {  /* fieldsel */
                            fieldsel(ls, v);
                            break;
                        }
                    case (TokenKind)'[': {  /* '[' exp ']' */
                            Code.exp2anyregup(fs, v);
                            ExpDesc key = new ExpDesc();
                            yindex(ls, key);
                            Code.indexed(fs, v, key);
                            break;
                        }
                    case (TokenKind)':': {  /* ':' NAME funcargs */
                            ExpDesc key = new ExpDesc();
                            ls.ReadNext();
                            checkname(ls, key);
                            Code.self(fs, v, key);
                            funcargs(ls, v, line);
                            break;
                        }
                    case (TokenKind)'(':
                    case TokenKind.String:
                    case (TokenKind)'{':
                        {  /* funcargs */
                            Code.exp2nextreg(fs, v);
                            funcargs(ls, v, line);
                            break;
                        }
                    default:
                        return; 
                }
            }
        }


        void simpleexp(Lexer ls, ExpDesc v) {
            /* simpleexp . FLT | INT | STRING | NIL | TRUE | FALSE | ... |
                            constructor | FUNCTION body | suffixedexp */
            switch (ls.Tk.token) {
                case TokenKind.Float: {
                        init_exp(v, ExpKind.Float, 0);
                        v.nval = ls.Tk.r;
                        break;
                    }
                case TokenKind.Int: {
                        init_exp(v, ExpKind.Int, 0);
                        v.ival = ls.Tk.i;
                        break;
                    }
                case TokenKind.String: {
                        codestring(ls, v, ls.Tk.ts);
                        break;
                    }
                case TokenKind.Nil: {
                        init_exp(v, ExpKind.Nil, 0);
                        break;
                    }
                case TokenKind.True: {
                        init_exp(v, ExpKind.True, 0);
                        break;
                    }
                case TokenKind.False: {
                        init_exp(v, ExpKind.False, 0);
                        break;
                    }
                case TokenKind.Dots: {  /* vararg */
                        FuncState fs = ls.fs;
                        check_condition(ls, fs.f.HasVarArg, "cannot use '...' outside a vararg function");
                        init_exp(v, ExpKind.VarArg, Code.codeABC(fs, OpCode.VARARG, 0, 0, 1));
                        break;
                    }
                case (TokenKind)'{': {  /* constructor */
                        constructor(ls, v);
                        return;
                    }
                case TokenKind.Function: {
                        ls.ReadNext();
                        body(ls, v, false, ls.linenumber);
                        return;
                    }
                default: {
                        suffixedexp(ls, v);
                        return;
                    }
            }
            ls.ReadNext();
        }

        UnOpr getunopr(TokenKind op)
        {
            switch (op) {
                case TokenKind.Not:
                    return UnOpr.NOT;
                case (TokenKind)'-':
                    return UnOpr.MINUS;
                case (TokenKind)'~':
                    return UnOpr.BNOT;
                case (TokenKind)'#':
                    return UnOpr.LEN;
                default:
                    return UnOpr.NOUNOPR;
            }
        }

        BinOpr getbinopr(TokenKind op) {
            switch (op) {
                case (TokenKind)'+': return BinOpr.ADD;
                case (TokenKind)'-': return BinOpr.SUB;
                case (TokenKind)'*': return BinOpr.MUL;
                case (TokenKind)'%': return BinOpr.MOD;
                case (TokenKind)'^': return BinOpr.POW;
                case (TokenKind)'/': return BinOpr.DIV;
                case TokenKind.IDiv: return BinOpr.IDIV;
                case (TokenKind)'&': return BinOpr.BAND;
                case (TokenKind)'|': return BinOpr.BOR;
                case (TokenKind)'~': return BinOpr.BXOR;
                case TokenKind.Shl: return BinOpr.SHL;
                case TokenKind.Shr: return BinOpr.SHR;
                case TokenKind.Concat: return BinOpr.CONCAT;
                case TokenKind.Ne: return BinOpr.NE;
                case TokenKind.Eq: return BinOpr.EQ;
                case (TokenKind)'<': return BinOpr.LT;
                case TokenKind.Le: return BinOpr.LE;
                case (TokenKind)'>': return BinOpr.GT;
                case TokenKind.Ge: return BinOpr.GE;
                case TokenKind.And: return BinOpr.AND;
                case TokenKind.Or: return BinOpr.OR;
                default: return BinOpr.NOBINOPR;
            }
        }


        internal struct Priority
        {
            internal int left;
            internal int right;
            internal Priority(int l, int r)
            {
                left = l;
                right = r;
            }
        }

        static Priority[] priority = new Priority[]{  /* ORDER OPR */
           new Priority(10, 10), new Priority(10, 10),           /* '+' '-' */
           new Priority(11, 11), new Priority(11, 11),           /* '*' '%' */
           new Priority(14, 13),                  /* '^' (right associative) */
           new Priority(11, 11), new Priority(11, 11),           /* '/' '//' */
           new Priority(6, 6), new Priority(4, 4), new Priority(5, 5),   /* '&' '|' '~' */
           new Priority(7, 7), new Priority(7, 7),           /* '<<' '>>' */
           new Priority(9, 8),                   /* '..' (right associative) */
           new Priority(3, 3), new Priority(3, 3), new Priority(3, 3),   /* ==, <, <= */
           new Priority(3, 3), new Priority(3, 3), new Priority(3, 3),   /* ~=, >, >= */
           new Priority(2, 2), new Priority(1, 1)            /* and, or */
        };

        const int UNARY_PRIORITY = 12;  /* priority for unary operators */


        /*
        ** subexpr . (simpleexp | unop subexpr) { binop subexpr }
        ** where 'binop' is any binary operator with a priority higher than 'limit'
        */
        BinOpr subexpr(Lexer ls, ExpDesc v, int limit) {
            BinOpr op;
            UnOpr uop;
            enterlevel(ls);
            uop = getunopr(ls.Tk.token);
            if (uop != UnOpr.NOUNOPR) {
                int line = ls.linenumber;
                ls.ReadNext();
                subexpr(ls, v, UNARY_PRIORITY);
                Code.prefix(ls.fs, uop, v, line);
            }
            else simpleexp(ls, v);
            /* expand while operators have priorities higher than 'limit' */
            op = getbinopr(ls.Tk.token);
            while (op != BinOpr.NOBINOPR && priority[(int)op].left > limit) {
                ExpDesc v2 = new ExpDesc();
                BinOpr nextop;
                int line = ls.linenumber;
                ls.ReadNext();
                Code.infix(ls.fs, op, v);
                /* read sub-expression with higher priority */
                nextop = subexpr(ls, v2, priority[(int)op].right);
                Code.posfix(ls.fs, op, v, v2, line);
                op = nextop;
            }
            leavelevel(ls);
            return op;  /* return first untreated operator */
        }


        void expr(Lexer ls, ExpDesc v)
        {
            subexpr(ls, v, 0);
        }

        /* }==================================================================== */



        /*
        ** {======================================================================
        ** Rules for Statements
        ** =======================================================================
        */


        void block(Lexer ls) {
            /* block . statlist */
            FuncState fs = ls.fs;
            BlockCnt bl = new BlockCnt();
            enterblock(fs, bl, false);
            statlist(ls);
            leaveblock(fs);
        }


        /*
        ** structure to chain all variables in the left-hand side of an
        ** assignment
        */
        internal class LHS_assign
        {
            internal LHS_assign prev;
            internal ExpDesc v;  /* variable (global, local, upvalue, or indexed) */
        }


        static bool vkisindexed(ExpKind k)
        {
            return (ExpKind.Indexed <= (k) && (k) <= ExpKind.IndexString);
        }

        static bool vkisvar(ExpKind k)
        {
            return (ExpKind.Local <= (k) && (k) <= ExpKind.IndexString);
        }

        static bool vkisinreg(ExpKind k)
        {
            return (ExpKind.Reloc <= (k) && (k) <= ExpKind.NonReloc);
        }

        /*
        ** check whether, in an assignment to an upvalue/local variable, the
        ** upvalue/local variable is begin used in a previous assignment to a
        ** table. If so, save original upvalue/local value in a safe place and
        ** use this safe copy in the previous assignment.
        */
        void check_conflict(Lexer ls, LHS_assign lh, ExpDesc v) {
            FuncState fs = ls.fs;
            int extra = fs.freereg;  /* eventual position to save local variable */
            bool conflict = false;
            for (; lh != null; lh = lh.prev) {  /* check all previous assignments */
                if (vkisindexed(lh.v.k)) {  /* assignment to table field? */
                    if (lh.v.k == ExpKind.IndexUp) {  /* is table an upvalue? */
                        if (v.k == ExpKind.Upval && lh.v.indT == v.info) {
                            conflict = true;  /* table is the upvalue being assigned now */
                            lh.v.k = ExpKind.IndexString;
                            lh.v.indT = extra;  /* assignment will use safe copy */
                        }
                    }
                    else {  /* table is a register */
                        if (v.k == ExpKind.Local && lh.v.indT == v.info) {
                            conflict = true;  /* table is the local being assigned now */
                            lh.v.indT = extra;  /* assignment will use safe copy */
                        }
                        /* is index the local being assigned? */
                        if (lh.v.k == ExpKind.Indexed && v.k == ExpKind.Local &&
                            lh.v.indIdx == v.info) {
                            conflict = true;
                            lh.v.indIdx = extra;  /* previous assignment will use safe copy */
                        }
                    }
                }
            }
            if (conflict) {
                /* copy upvalue/local value to a temporary (in position 'extra') */
                OpCode op = (v.k == ExpKind.Local) ? OpCode.MOVE : OpCode.GETUPVAL;
                Code.codeABC(fs, op, extra, v.info, 0);
                Code.reserveregs(fs, 1);
            }
        }


        void assignment(Lexer ls, LHS_assign lh, int nvars) {
            ExpDesc e = new ExpDesc();
            check_condition(ls, vkisvar(lh.v.k), "syntax error");
            if (testnext(ls, ',')) {  /* assignment . ',' suffixedexp assignment */
                LHS_assign nv = new LHS_assign();
                nv.prev = lh;
                suffixedexp(ls, nv.v);
                if (!vkisindexed(nv.v.k))
                {
                    check_conflict(ls, lh, nv.v);
                }
                //luaE_incCcalls(ls.L);  /* control recursion depth */
                assignment(ls, nv, nvars + 1);
                //ls.L.nCcalls--;
            }
            else {  /* assignment . '=' explist */
                int nexps;
                checknext(ls, '=');
                if (nvars == 1 && testnext(ls, TokenKind.Undef)) {
                    Code.codeundef(ls.fs, lh.v);
                    return;
                }
                nexps = explist(ls, e);
                if (nexps != nvars)
                {
                    adjust_assign(ls, nvars, nexps, e);
                }
                else
                {
                    Code.setoneret(ls.fs, e);  /* close last expression */
                    Code.storevar(ls.fs, lh.v, e);
                    return;  /* avoid default */
                }
            }
            init_exp(e, ExpKind.NonReloc, ls.fs.freereg - 1);  /* default assignment */
            Code.storevar(ls.fs, lh.v, e);
        }


        int cond(Lexer ls) {
            /* cond . exp */
            ExpDesc v = new ExpDesc();
            expr(ls, v);  /* read condition */
            if (v.k == ExpKind.Nil) v.k = ExpKind.False;  /* 'falses' are all equal here */
            Code.goiftrue(ls.fs, v);
            return v.f;
        }


        void gotostat(Lexer ls, int pc)
        {
            int line = ls.linenumber;
            int g;
            ls.ReadNext();  /* skip 'goto' */
            g = newlabelentry(ls, ls.dyd.gt, str_checkname(ls), line, pc);
            solvelabel(ls, g);  /* close it if label already defined */
        }


        void breakstat(Lexer ls, int pc) {
            FuncState fs = ls.fs;
            BlockCnt bl = fs.bl;
            ls.ReadNext();  /* skip break */
            while (bl != null && !bl.isloop) { bl = bl.previous; }
            if (bl == null)
            {
                ls.syntaxerror("no loop to break");
            }
            Code.concat(fs, ref fs.bl.brks, pc);
        }


        /* check for repeated labels on the same block */
        void checkrepeated(FuncState fs, LabelList ll, string label) {
            int i;
            for (i = fs.bl.firstlabel; i < ll.n; i++) {
                if (eqstr(label, ll.arr[i].name)) {
                    string msg = string.Format("label '{0}' already defined on line {1}", label, ll.arr[i].line);
                    Code.semerror(fs.ls, msg);
                }
            }
        }


        /* skip no-op statements */
        void skipnoopstat(Lexer ls)
        {
            while (ls.Tk.token == (TokenKind)';' || ls.Tk.token == TokenKind.DbColon)
            {
                statement(ls);
            }
        }


        void labelstat(Lexer ls, string label, int line) {
            /* label . '::' NAME '::' */
            FuncState fs = ls.fs;
            LabelList ll = ls.dyd.label;
            //int l;  /* index of new label being created */
            checkrepeated(fs, ll, label);  /* check for repeated labels */
            checknext(ls, TokenKind.DbColon);  /* skip double colon */
                                        /* create new entry for this label */
            int l = newlabelentry(ls, ll, label, line, Code.getlabel(fs));
            Code.codeABC(fs, OpCode.CLOSE, fs.nactvar, 0, 0);
            skipnoopstat(ls);  /* skip other no-op statements */
            if (block_follow(ls, false)) {  /* label is last no-op statement in the block? */
                                        /* assume that locals are already out of scope */
                ll.arr[l].nactvar = fs.bl.nactvar;
            }
            solvegotos(ls, ll.arr[l]);
        }


        void whilestat(Lexer ls, int line) {
            /* whilestat . WHILE cond DO block END */
            FuncState fs = ls.fs;
            int whileinit;
            int condexit;
            BlockCnt bl = new BlockCnt();
            ls.ReadNext();  /* skip WHILE */
            whileinit = Code.getlabel(fs);
            condexit = cond(ls);
            enterblock(fs, bl, true);
            checknext(ls, TokenKind.Do);
            block(ls);
            Code.jumpto(fs, whileinit);
            check_match(ls, TokenKind.End, TokenKind.While, line);
            leaveblock(fs);
            Code.patchtohere(fs, condexit);  /* false conditions finish the loop */
        }


        void repeatstat(Lexer ls, int line) {
            /* repeatstat . REPEAT block UNTIL cond */
            int condexit;
            FuncState fs = ls.fs;
            int repeat_init = Code.getlabel(fs);
            BlockCnt bl1 = new BlockCnt();
            BlockCnt bl2 = new BlockCnt();
            enterblock(fs, bl1, true);  /* loop block */
            enterblock(fs, bl2, false);  /* scope block */
            ls.ReadNext();  /* skip REPEAT */
            statlist(ls);
            check_match(ls, TokenKind.Until, TokenKind.Repeat, line);
            condexit = cond(ls);  /* read condition (inside scope block) */
            if (bl2.upval)
            {
                /* upvalues? */
                Code.patchclose(fs, condexit);
            }
            leaveblock(fs);  /* finish scope */
            if (bl2.upval) {  /* upvalues? */
                int exit = Code.jump(fs);  /* normal exit must jump over fix */
                Code.patchtohere(fs, condexit);  /* repetition must close upvalues */
                Code.codeABC(fs, OpCode.CLOSE, bl2.nactvar, 0, 0);
                condexit = Code.jump(fs);  /* repeat after closing upvalues */
                Code.patchtohere(fs, exit);  /* normal exit comes to here */
            }
            Code.patchlist(fs, condexit, repeat_init);  /* close the loop */
            leaveblock(fs);  /* finish loop */
        }


        /*
        ** Read an expression and generate code to put its results in next
        ** stack slot. Return true if expression is a constant integer and,
        ** if 'i' is not-zero, its value is equal to 'i'.
        **
        */
        bool exp1(Lexer ls, int i) {
            ExpDesc e = new ExpDesc();
            expr(ls, e);
            var res = Code.isKint(e) && ((i == 0) || (i == e.ival));
            Code.exp2nextreg(ls.fs, e);
            Lexer.assert(e.k == ExpKind.NonReloc);
            return res;
        }


        /*
        ** Fix for instruction at position 'pc' to jump to 'dest'.
        ** (Jump addresses are relative in Lua). 'back' true means
        ** a back jump.
        */
        void fixforjump(FuncState fs, int pc, int dest, bool back) {
            uint jmp = fs.f.Codes[pc];
            int offset = dest - (pc + 1);
            if (back)
            {
                offset = -offset;
            }
            if (offset > Code.MaxArgBx)
            {
                fs.ls.syntaxerror("control structure too long");
            }
            jmp = Inst.SetBx(jmp, offset);
            fs.f.Codes[pc] = jmp;
        }


        /*
        ** Generate code for a 'for' loop. 'kind' can be zero (a common for
        ** loop), one (a basic for loop, with integer values and increment of
        ** 1), or two (a generic for loop).
        */
        void forbody(Lexer ls, int base_, int line, int nvars, int kind) {
            /* forbody . DO block */
            BlockCnt bl = new BlockCnt();
            FuncState fs = ls.fs;
            int prep, endfor;
            adjustlocalvars(ls, 3);  /* control variables */
            checknext(ls, TokenKind.Do);
            prep = (kind == 0) ? Code.codeABx(fs, OpCode.FORPREP, base_, 0)
                 : (kind == 1) ? Code.codeABx(fs, OpCode.FORPREP1, base_, 0)
                 : Code.jump(fs);
            enterblock(fs, bl, false);  /* scope for declared variables */
            adjustlocalvars(ls, nvars);
            Code.reserveregs(fs, nvars);
            block(ls);
            leaveblock(fs);  /* end of scope for declared variables */
            if (kind == 2) {  /* generic for? */
                Code.patchtohere(fs, prep);
                Code.codeABC(fs, OpCode.TFORCALL, base_, 0, nvars);
                Code.fixline(fs, line);
                endfor = Code.codeABx(fs, OpCode.TFORLOOP, base_ + 2, 0);
            }
            else {
                fixforjump(fs, prep, Code.getlabel(fs), false);
                endfor = (kind == 0) ? Code.codeABx(fs, OpCode.FORLOOP, base_, 0)
                                     : Code.codeABx(fs, OpCode.FORLOOP1, base_, 0);
            }
            fixforjump(fs, endfor, prep + 1, true);
            Code.fixline(fs, line);
        }


        void fornum(Lexer ls, string varname, int line) {
            /* fornum . NAME = exp,exp[,exp] forbody */
            FuncState fs = ls.fs;
            int base_ = fs.freereg;
            int basicfor = 1;  /* true if it is a "basic" 'for' (integer + 1) */
            new_localvarliteral(ls, "(for index)");
            new_localvarliteral(ls, "(for limit)");
            new_localvarliteral(ls, "(for step)");
            new_localvar(ls, varname);
            checknext(ls, '=');
            if (!exp1(ls, 0))  /* initial value not an integer? */
                basicfor = 0;  /* not a basic 'for' */
            checknext(ls, ',');
            exp1(ls, 0);  /* limit */
            if (testnext(ls, ',')) {
                if (!exp1(ls, 1))  /* optional step not 1? */
                    basicfor = 0;  /* not a basic 'for' */
            }
            else {  /* default step = 1 */
                Code.integer(fs, fs.freereg, 1);
                Code.reserveregs(fs, 1);
            }
            forbody(ls, base_, line, 1, basicfor);
        }


        void forlist(Lexer ls, string indexname) {
            /* forlist . NAME {,NAME} IN explist forbody */
            FuncState fs = ls.fs;
            ExpDesc e = new ExpDesc();
            int nvars = 4;  /* gen, state, control, plus at least one declared var */
            int line;
            int base_ = fs.freereg;
            /* create control variables */
            new_localvarliteral(ls, "(for generator)");
            new_localvarliteral(ls, "(for state)");
            new_localvarliteral(ls, "(for control)");
            /* create declared variables */
            new_localvar(ls, indexname);
            while (testnext(ls, ',')) {
                new_localvar(ls, str_checkname(ls));
                nvars++;
            }
            checknext(ls, TokenKind.In);
            line = ls.linenumber;
            adjust_assign(ls, 3, explist(ls, e), e);
            Code.checkstack(fs, 3);  /* extra space to call generator */
            forbody(ls, base_, line, nvars - 3, 2);
        }


        void forstat(Lexer ls, int line) {
            /* forstat . FOR (fornum | forlist) END */
            FuncState fs = ls.fs;
            string varname;
            BlockCnt bl = new BlockCnt();
            enterblock(fs, bl, true);  /* scope for loop and control variables */
            ls.ReadNext();  /* skip 'for' */
            varname = str_checkname(ls);  /* first variable name */
            switch (ls.Tk.token) {
                case (TokenKind)'=':
                    fornum(ls, varname, line);
                    break;
                case (TokenKind)',':
                case TokenKind.In:
                    forlist(ls, varname);
                    break;
                default:
                    ls.syntaxerror("'=' or 'in' expected");
                    break;
            }
            check_match(ls, TokenKind.End, TokenKind.For, line);
            leaveblock(fs);  /* loop scope ('break' jumps to this point) */
        }


        void test_then_block(Lexer ls, ref int escapelist) {
            /* test_then_block . [IF | ELSEIF] cond THEN block */
            BlockCnt bl = new BlockCnt();
            FuncState fs = ls.fs;
            ExpDesc v = new ExpDesc();
            int jf;  /* instruction to skip 'then' code (if condition is false) */
            ls.ReadNext();  /* skip IF or ELSEIF */
            expr(ls, v);  /* read condition */
            checknext(ls, TokenKind.Then);
            if (ls.Tk.token == TokenKind.Goto || ls.Tk.token == TokenKind.Break) {
                Code.goiffalse(ls.fs, v);  /* will jump to label if condition is true */
                enterblock(fs, bl, false);  /* must enter block before 'goto' */
                if (ls.Tk.token == TokenKind.Goto)
                    gotostat(ls, v.t);  /* handle goto */
                else
                    breakstat(ls, v.t);  /* handle break */
                while (testnext(ls, ';')) { }  /* skip semicolons */
                if (block_follow(ls, false)) {  /* 'goto'/'break' is the entire block? */
                    leaveblock(fs);
                    return;  /* and that is it */
                }
                else  /* must skip over 'then' part if condition is false */
                    jf = Code.jump(fs);
            }
            else {  /* regular case (not goto/break) */
                Code.goiftrue(ls.fs, v);  /* skip over block if condition is false */
                enterblock(fs, bl, false);
                jf = v.f;
            }
            statlist(ls);  /* 'then' part */
            leaveblock(fs);
            if (ls.Tk.token == TokenKind.Else ||
                ls.Tk.token == TokenKind.ElseIf)  /* followed by 'else'/'elseif'? */
                Code.concat(fs, ref escapelist, Code.jump(fs));  /* must jump over it */
            Code.patchtohere(fs, jf);
        }


        void ifstat(Lexer ls, int line) {
            /* ifstat . IF cond THEN block {ELSEIF cond THEN block} [ELSE block] END */
            FuncState fs = ls.fs;
            int escapelist = NoJump;  /* exit list for finished parts */
            test_then_block(ls, ref escapelist);  /* IF cond THEN block */
            while (ls.Tk.token == TokenKind.ElseIf)
            {
                test_then_block(ls, ref escapelist);  /* ELSEIF cond THEN block */
            }
            if (testnext(ls, TokenKind.Else))
            {
                block(ls);  /* 'else' part */
            }
            check_match(ls, TokenKind.End, TokenKind.If, line);
            Code.patchtohere(fs, escapelist);  /* patch escape list to 'if' end */
        }


        void localfunc(Lexer ls) {
            ExpDesc b = new ExpDesc();
            FuncState fs = ls.fs;
            new_localvar(ls, str_checkname(ls));  /* new local variable */
            adjustlocalvars(ls, 1);  /* enter its scope */
            body(ls, b, false, ls.linenumber);  /* function created in next register */
                                             /* debug information will only see the variable after this point! */
            getlocvar(fs, b.info).startpc = fs.pc;
        }


        void localstat(Lexer ls) {
            /* stat . LOCAL NAME {',' NAME} ['=' explist] */
            int nvars = 0;
            int nexps;
            ExpDesc e = new ExpDesc();
            do {
                new_localvar(ls, str_checkname(ls));
                nvars++;
            } while (testnext(ls, ','));
            if (testnext(ls, '='))
                nexps = explist(ls, e);
            else {
                e.k = ExpKind.Void;
                nexps = 0;
            }
            adjust_assign(ls, nvars, nexps, e);
            adjustlocalvars(ls, nvars);
        }


        bool funcname(Lexer ls, ExpDesc v) {
            /* funcname . NAME {fieldsel} [':' NAME] */
            bool ismethod = false;
            singlevar(ls, v);
            while (ls.Tk.token == (TokenKind)'.')
                fieldsel(ls, v);
            if (ls.Tk.token == (TokenKind)':') {
                ismethod = true;
                fieldsel(ls, v);
            }
            return ismethod;
        }


        void funcstat(Lexer ls, int line) {
            /* funcstat . FUNCTION funcname body */
            ExpDesc v = new ExpDesc();
            ExpDesc b = new ExpDesc();
            ls.ReadNext();  /* skip FUNCTION */
            var ismethod = funcname(ls, v);
            body(ls, b, ismethod, line);
            Code.storevar(ls.fs, v, b);
            Code.fixline(ls.fs, line);  /* definition "happens" in the first line */
        }


        internal static uint getinstruction(FuncState fs, ExpDesc e)
        {
            return (fs.f.Codes[e.info]);
        }

        internal static void setinstruction(FuncState fs, ExpDesc e, uint inst)
        {
            fs.f.Codes[e.info] = inst;
        }

        void exprstat(Lexer ls) {
            /* stat . func | assignment */
            FuncState fs = ls.fs;
            LHS_assign v = new LHS_assign();
            suffixedexp(ls, v.v);
            if (ls.Tk.token == (TokenKind)'=' || ls.Tk.token == (TokenKind)',') { /* stat . assignment ? */
                v.prev = null;
                assignment(ls, v, 1);
            }
            else {  /* stat . func */
                uint inst = getinstruction(fs, v.v);
                check_condition(ls, v.v.k == ExpKind.Call, "syntax error");
                inst = Inst.SetC(inst, 1);  /* call statement uses no results */

                // setinstruction(fs, v.v, inst); TODO
            }
        }


        void retstat(Lexer ls)
        {
            /* stat . RETURN [explist] [';'] */
            FuncState fs = ls.fs;
            ExpDesc e = new ExpDesc();
            int first, nret;  /* registers with returned values */
            if (block_follow(ls, true) || ls.Tk.token == (TokenKind)';')
                first = nret = 0;  /* return no values */
            else {
                nret = explist(ls, e);  /* optional return values */
                if (hasmultret(e.k)) {
                    Code.setmultret(fs, e);
                    if (e.k == ExpKind.Call && nret == 1) {  /* tail call? */
                        setinstruction(fs,e, Inst.SetOpCode(getinstruction(fs, e), OpCode.TAILCALL));
                        Lexer.assert(Inst.A(getinstruction(fs, e)) == fs.nactvar);
                    }
                    first = fs.nactvar;
                    nret = LUA_MULTRET;  /* return all values */
                }
                else {
                    if (nret == 1)  /* only one single value? */
                        first = Code.exp2anyreg(fs, e);
                    else {
                        Code.exp2nextreg(fs, e);  /* values must go to the stack */
                        first = fs.nactvar;  /* return all active values */
                        Lexer.assert(nret == fs.freereg - first);
                    }
                }
            }
            Code.ret(fs, first, nret);
            testnext(ls, ';');  /* skip optional semicolon */
        }


        void statement(Lexer ls) {
            int line = ls.linenumber;  /* may be needed for error messages */
            enterlevel(ls);
            switch (ls.Tk.token) {
                case (TokenKind)';': {  /* stat . ';' (empty statement) */
                        ls.ReadNext();  /* skip ';' */
                        break;
                    }
                case TokenKind.If: {  /* stat . ifstat */
                        ifstat(ls, line);
                        break;
                    }
                case TokenKind.While: {  /* stat . whilestat */
                        whilestat(ls, line);
                        break;
                    }
                case TokenKind.Do: {  /* stat . DO block END */
                        ls.ReadNext();  /* skip DO */
                        block(ls);
                        check_match(ls, TokenKind.End, TokenKind.Do, line);
                        break;
                    }
                case TokenKind.For: {  /* stat . forstat */
                        forstat(ls, line);
                        break;
                    }
                case TokenKind.Repeat: {  /* stat . repeatstat */
                        repeatstat(ls, line);
                        break;
                    }
                case TokenKind.Function: {  /* stat . funcstat */
                        funcstat(ls, line);
                        break;
                    }
                case TokenKind.Local: {  /* stat . localstat */
                        ls.ReadNext();  /* skip LOCAL */
                        if (testnext(ls, TokenKind.Function))
                        {  /* local function? */
                            localfunc(ls);
                        }
                        else if (testnext(ls, TokenKind.Undef))
                        {
                            //(void)0;  /* ignore */
                            /* old versions may need to declare 'local undef'
                               when using 'undef' with no environment; so this
                               version accepts (and ignores) these declarations */
                        }
                        else
                        {
                            localstat(ls);
                        }
                        break;
                    }
                case TokenKind.DbColon: {  /* stat . label */
                        ls.ReadNext();  /* skip double colon */
                        labelstat(ls, str_checkname(ls), line);
                        break;
                    }
                case TokenKind.Return: {  /* stat . retstat */
                        ls.ReadNext();  /* skip RETURN */
                        retstat(ls);
                        break;
                    }
                case TokenKind.Break: {  /* stat . breakstat */
                        breakstat(ls, Code.jump(ls.fs));
                        break;
                    }
                case TokenKind.Goto: {  /* stat . 'goto' NAME */
                        gotostat(ls, Code.jump(ls.fs));
                        break;
                    }
                default: {  /* stat . func | assignment */
                        exprstat(ls);
                        break;
                    }
            }

            //Lexer.assert(ls.fs.f.maxstacksize >= ls.fs.freereg &&
            //           ls.fs.freereg >= ls.fs.nactvar);
            ls.fs.freereg = ls.fs.nactvar;  /* free registers */
            leavelevel(ls);
        }

/* }====================================================================== */


        /*
        ** compiles the main function, which is a regular vararg function with an
        ** upvalue named LUA_ENV
        */
        void mainfunc (Lexer ls, FuncState fs)
        {
            BlockCnt bl = new BlockCnt();
            ExpDesc v = new ExpDesc();
            open_func(ls, fs, bl);
            setvararg(fs, 0);  /* main function is always declared vararg */
            init_exp(v, ExpKind.Local, 0);  /* create and... */
            newupvalue(fs, ls.envn, v);  /* ...set environment upvalue */
            ls.ReadNext();  /* read first token */
            statlist(ls);  /* parse main body */
            check(ls, TokenKind.Eos);
            close_func(ls);
        }

        internal Proto Parse (LuaState L, ZIO z, string name, TokenKind firstchar)
        {
            Lexer lex = new Lexer(z, name, firstchar, new DynData());
            FuncState funcstate = new FuncState();
            var f = new Proto();  /* create main closure */
            f.Upvals.Add(new UpvalTag { Name = "_ENV", Index = 0, InStack = 0 }); // _ENVを定義
            //setclLvalue2s(L, L.top, cl);  /* anchor it (to avoid being collected) */
            //luaD_inctop(L);
            //sethvalue2s(L, L.top, lexstate.h);  /* anchor it */
            //luaD_inctop(L);
            funcstate.f = f;// = new Proto();
            funcstate.f.Filename = name;  /* create and anchor TString */
            //Lexer.assert(iswhite(funcstate.f));  /* do not need barrier here */
            //dyd.actvar.n = dyd.gt.n = dyd.label.n = 0;
            mainfunc(lex, funcstate);
            //Lexer.assert(!funcstate.prev && funcstate.nups == 1 && !lexstate.fs);
            /* all scopes should be correctly finished */
            //Lexer.assert(dyd.actvar.n == 0 && dyd.gt.n == 0 && dyd.label.n == 0);
            //L.top--;  /* remove scanner's table */
            return f;  /* closure is on the stack, too */
        }
    }
}

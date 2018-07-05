using System;

namespace TLua
{
    public class Parser
    {
        enum ExpKind {
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

//#define vkisvar(k)	(VLOCAL <= (k) && (k) <= VINDEXSTR)
//#define vkisindexed(k)	(VINDEXED <= (k) && (k) <= VINDEXSTR)
//#define vkisinreg(k)	((k) == VNONRELOC || (k) == VLOCAL)

        struct ExpDesc
        {
            ExpKind k;
            LuaValue val;
            int info;  /* for generic use */
            short idxIdx; /* index (R or "long" K) */
            byte idxT; /* table (register or upvalue) */
            int t;  /* patch list of 'exit when true' */
            int f;  /* patch list of 'exit when false' */
        }

        /* description of active local variable */
        struct VarDesc
        {
            short idx;  /* variable index in stack */
        }


        /* description of pending goto statements and label statements */
        struct Labeldesc
        {
            string name;  /* label identifier */
            int pc;  /* position in code */
            int line;  /* line where it appeared */
            byte nactvar;  /* local level where it appears in current block */
        }

        /* list of labels or gotos */
        struct LabelList
        {
            Labeldesc[] arr;  /* array */
            int n;  /* number of entries in use */
            int size;  /* array size */
        }


        /* dynamic structures used by the parser */
        internal struct DynData
        {
            VarDesc[] arr;
            int n;
            int size;
            LabelList gt;  /* list of pending gotos */
            LabelList label;   /* list of active labels */
        }

        /* control of blocks */
        //struct BlockCnt;  /* defined in lparser.c */


        /* state needed to generate code for a given function */
        class FuncState
        {
            Function f;  /* current function header */
            FuncState prev;  /* enclosing function */
            Lexer.LexState ls;  /* lexical state */
            //BlockCnt bl;  /* chain of current blocks */
            int pc;  /* next position to code (equivalent to 'ncode') */
            int lasttarget;   /* 'label' of last 'jump label' */
            int previousline;  /* last line that was saved in 'lineinfo' */
            int nk;  /* number of elements in 'k' */
            int np;  /* number of elements in 'p' */
            int nabslineinfo;  /* number of elements in 'abslineinfo' */
            int firstlocal;  /* index of first local var (in Dyndata array) */
            short nlocvars;  /* number of elements in 'f->locvars' */
            byte nactvar;  /* number of active local variables */
            byte nups;  /* number of upvalues */
            byte freereg;  /* first free register */
            byte iwthabs;  /* instructions issued since last absolute line info */
        }


        public Parser()
        {
        }


#if false
        Closure luaY_parser(LuaState L, ZIO z, Mbuffer* buff,
                                   Dyndata* dyd, string name, int firstchar)
        {
            LexState lexstate;
            FuncState funcstate;
            LClosure* cl = luaF_newLclosure(L, 1);  /* create main closure */
            setclLvalue2s(L, L->top, cl);  /* anchor it (to avoid being collected) */
            luaD_inctop(L);
            lexstate.h = luaH_new(L);  /* create table for scanner */
            sethvalue2s(L, L->top, lexstate.h);  /* anchor it */
            luaD_inctop(L);
            funcstate.f = cl->p = luaF_newproto(L);
            funcstate.f->source = luaS_new(L, name);  /* create and anchor TString */
            lua_assert(iswhite(funcstate.f));  /* do not need barrier here */
            lexstate.buff = buff;
            lexstate.dyd = dyd;
            dyd->actvar.n = dyd->gt.n = dyd->label.n = 0;
            luaX_setinput(L, &lexstate, z, funcstate.f->source, firstchar);
            mainfunc(&lexstate, &funcstate);
            lua_assert(!funcstate.prev && funcstate.nups == 1 && !lexstate.fs);
            /* all scopes should be correctly finished */
            lua_assert(dyd->actvar.n == 0 && dyd->gt.n == 0 && dyd->label.n == 0);
            L->top--;  /* remove scanner's table */
            return cl;  /* closure is on the stack, too */
        }
#endif
    }
}

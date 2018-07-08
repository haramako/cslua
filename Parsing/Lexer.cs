using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TLua;
using System.Diagnostics;

namespace TLua.Parsing
{
    static class TokenCharExtension
    {
        internal static bool IsChar(this TokenKind token)
        {
            return token >= 0;
        }

        internal static char c(this TokenKind token)
        {
            return (char)token;
        }
    }

    /*
    * WARNING: if you change the order of this enumeration,
    * grep "ORDER RESERVED"
    */
    internal enum TokenKind
    {
        /* terminal symbols denoted by reserved words */
        And = -38,
        Break,
        Do,
        Else,
        ElseIf,
        End,
        False,
        For,
        Function,
        Goto,
        If,
        In,
        Local,
        Nil,
        Not,
        Or,
        Repeat,
        Return,
        Then,
        True,
        Undef,
        Until,
        While,
        IDiv,
        Concat,
        Dots,
        Eq,
        Ge,
        Le,
        Ne,
        Shl,
        Shr,
        DbColon,
        Eos,
        Float,
        Int,
        Name,
        String, // last element must be -1
    }

    public class Lexer
    {
        /* number of reserved words */
        //#define NUM_RESERVED	(cast_int(TK_WHILE-FIRST_RESERVED + 1))

        internal class Token
        {
            internal TokenKind token;
            internal float r;
            internal int i;
            internal string ts;
        }


        /* state of the lexer plus state of the parser when shared by all
           functions */
        TokenKind current;  /* current character (charint) */
        internal int linenumber;  /* input line counter */
        int lastline;  /* line of last token 'consumed' */
        Token t = new Token();  /* current token */
        Token lookahead = new Token();  /* look ahead token */
        internal FuncState fs;  /* current function (parser) */
        StringBuilder buff = new StringBuilder();
        ZIO z;  /* input stream */
        internal string source;  /* current source name */
        internal string envn;  /* environment variable name */
        internal DynData dyd;


        internal Lexer(ZIO z_, string source_, TokenKind firstchar, DynData dyd_)
        {
            t.token = 0;
            current = firstchar;
            lookahead.token = TokenKind.Eos;  /* no look-ahead token */
            z = z_;
            fs = null;
            linenumber = 1;
            lastline = 1;
            source = source_;
            envn = "_ENV";  /* get env name */
            dyd = dyd_;
        }


        void next() {
            var c = z.ReadChar();
            if( c == -1)
            {
                current = TokenKind.Eos;
            }
            else
            {
                current = (TokenKind)c;
            }
        }

        bool currIsNewline() {
            return (char)current == '\n' || (char)current == '\r';
        }

        static Dictionary<string, TokenKind> tokenDict = new Dictionary<string, TokenKind>
        {
            { "and", TokenKind.And },
            { "break",TokenKind.Break },
            { "do",TokenKind.Do },
            { "else",TokenKind.Else },
            { "elseif",TokenKind.ElseIf },
            { "end",TokenKind.End },
            { "false",TokenKind.False },
            { "for",TokenKind.For },
            { "function",TokenKind.Function },
            { "goto",TokenKind.Goto },
            { "if",TokenKind.If },
            { "in",TokenKind.In },
            { "local",TokenKind.Local },
            { "nil",TokenKind.Nil },
            { "not",TokenKind.Not },
            { "or",TokenKind.Or },
            { "repeat",TokenKind.Repeat },
            { "return",TokenKind.Return },
            { "then",TokenKind.Then },
            { "true",TokenKind.True },
            { "undef",TokenKind.Undef },
            { "until",TokenKind.Until },
            { "while",TokenKind.While },
            { "//",TokenKind.IDiv },
            { "..",TokenKind.Concat },
            { "...",TokenKind.Dots },
            { "==",TokenKind.Eq },
            { ">=",TokenKind.Ge },
            { "<=",TokenKind.Le },
            { "~=",TokenKind.Ne },
            { "<<",TokenKind.Shl },
            { ">>",TokenKind.Shr },
            { "::",TokenKind.DbColon },
            { "<eof>",TokenKind.Eos },
            { "<number>",TokenKind.Float },
            { "<integer>",TokenKind.Int },
            { "<name>",TokenKind.Name },
            { "<string>", TokenKind.String },
        };

        static Dictionary<TokenKind, string> tokenKindDict = tokenDict.ToDictionary(kv => kv.Value, kv => kv.Key);

        void saveAndNext()
        {
            save(current);
            next();
        }

        // static l_noret lexerror(LexState* ls, const char* msg, int token);


        void save(char c)
        {
            buff.Append(c);
        }

        void save(TokenKind c)
        {
            buff.Append((char)c);
        }

        internal static string token2str(TokenKind token) 
        {
            if( !token.IsChar())
            {
                return tokenKindDict[token];
            }
            else
            {
                return ((char)token).ToString();
            }
        }


        internal string txtToken (TokenKind token)
        {
            switch (token)
            {
                case TokenKind.Name:
                case TokenKind.String:
                case TokenKind.Float:
                case TokenKind.Int:
                    return buff.ToString();
                default:
                    return token2str(token);
            }
        }

        class ParserException : Exception
        {
            public ParserException(string message) : base(message) { }
        }

        [DebuggerNonUserCode]
        void lexerror(string msg, TokenKind token) {
            throw new ParserException(string.Format("{0} near {1}", msg, txtToken(token)));
        }

        [DebuggerNonUserCode]
        internal void syntaxerror(string msg)
        {
            lexerror(msg, t.token);
        }

        /*
        ** increment line number and skips newline sequence (any of
        ** \n, \r, \n\r, or \r\n)
        */
        void inclinenumber()
        {
            var old = current;
            next();  /* skip '\n' or '\r' */
            if (currIsNewline() && current != old)
            {
                next();  /* skip '\n\r' or '\r\n' */
            }
            linenumber++;
        }


        /*
        ** =======================================================
        ** LEXICAL ANALYZER
        ** =======================================================
        */


        bool check_next1(char c)
        {
            if ((char)current == c)
            {
                next();
                return true;
            }
            else
            {
                return false;
            }
        }


        /*
        ** Check whether current char is in set 'set' (with two chars) and
        ** saves it
        */
        bool check_next2(string set)
        {
            //lua_assert(set[2] == '\0');
            if ((char)current == set[0] || (char)current == set[1])
            {
                saveAndNext();
                return true;
            }
            else
            {
                return false;
            }
        }


        /* LUA_NUMBER */
        /*
        ** this function is quite liberal in what it accepts, as 'luaO_str2num'
        ** will reject ill-formed numerals.
        */
        TokenKind readNumeral(Token seminfo)
        {
            string expo = "Ee";
            char first = (char)current;
            //lua_assert(lisdigit(ls->current));
            saveAndNext();
            if (first == '0' && check_next2("xX"))  /* hexadecimal? */
                expo = "Pp";
            for (;;)
            {
                if (check_next2(expo))  /* exponent part? */
                    check_next2("-+");  /* optional exponent sign */
                if (isxdigit(current.c()))
                    saveAndNext();
                else if ((char)current == '.')
                    saveAndNext();
                else break;
            }
            //save('\0');
            float num;
            if (!Single.TryParse(buff.ToString(), out num))
            {
                lexerror("malformed number", TokenKind.Float);
            }
            if( Math.Floor(num) == num)
            {
                seminfo.i = (int)Math.Floor(num);
                return TokenKind.Int;
            }
            else
            {
                seminfo.r = num;
                return TokenKind.Float;
            }
        }

        [DebuggerNonUserCode]
        public static void assert(bool pred)
        {
            if( !pred)
            {
                throw new InvalidProgramException();
            }
        }

        /*
        ** skip a sequence '[=*[' or ']=*]'; if sequence is well formed, return
        ** its number of '='s; otherwise, return a negative number (-1 iff there
        ** are no '='s after initial bracket)
        */
        int skipSep()
        {
            int count = 0;
            TokenKind s = current;
            assert((char)s == '[' || (char)s == ']');
            saveAndNext();
            while ((char)current == '=')
            {
                saveAndNext();
                count++;
            }
            return (current == s) ? count : (-count) - 1;
        }


        void readLongString(Token seminfo, int sep, bool isString)
        {
            int line = linenumber;  /* initial line (for error message) */
            saveAndNext();  /* skip 2nd '[' */
            if (currIsNewline())
            {
                /* string starts with a newline? */
                inclinenumber();  /* skip it */
            }
            for (;;)
            {
                switch ((char)current)
                {
                    case '\0':
                        {  /* error */
                            string what = (isString ? "string" : "comment");
                            var msg = string.Format("unfinished long %s (starting at line %d)", what, line);
                            lexerror(msg, TokenKind.Eos);
                            break;  /* to avoid warnings */
                        }
                    case ']':
                        {
                            if (skipSep() == sep)
                            {
                                saveAndNext();  /* skip 2nd ']' */
                                goto endloop;
                            }
                            break;
                        }
                    case '\n':
                    case '\r':
                        {
                            save('\n');
                            inclinenumber();
                            buff.Clear();
                            //if (!seminfo) luaZ_resetbuffer(ls->buff);  /* avoid wasting space */
                            break;
                        }
                    default:
                        {
                            if (isString)
                            {
                                saveAndNext();
                            }
                            else
                            {
                                next();
                            }
                            break;
                        }
                }
            }
            endloop:
            if (isString)
            {
                seminfo.ts = buff.ToString();
            }
        }


        void esccheck(bool c, string msg)
        {
            if (!c)
            {
                if ((char)current != '\0')
                {
                    saveAndNext();  /* add current to buffer for error message */
                }
                lexerror(msg, TokenKind.String);
            }
        }

        bool isxdigit(char c)
        {
            return hexavalue(c) != -1;
        }

        int hexavalue(char c)
        {
            if( char.IsDigit(c))
            {
                return c - '0';
            }
            else if(c >= 'a' && c <= 'f')
            {
                return c - 'a';
            }
            else if (c >= 'A' && c <= 'F')
            {
                return c - 'A';
            }
            else
            {
                return -1;
            }
        }

        int gethexa()
        {
            saveAndNext();
            var n = hexavalue((char)current);
            if (n < 0)
            {
                esccheck(false, "hexadecimal digit expected");
                return 0;
            }
            else
            {
                return n;
            }
        }


        char readhexaesc()
        {
            int r = gethexa();
            r = (r << 4) + gethexa();
            buff.Remove(0, 2);
            //luaZ_buffremove(ls->buff, 2);  /* remove saved chars from buffer */
            return (char)r;
        }


        char readutf8esc()
        {
            int r;
            int i = 4;  /* chars to be removed: '\', 'u', '{', and first digit */
            saveAndNext();  /* skip 'u' */
            esccheck((char)current == '{', "missing '{'");
            r = gethexa();  /* must have at least one digit */
            saveAndNext();
            while (isxdigit((char)current)) {
                i++;
                r = (r << 4) + hexavalue((char)current);
                esccheck(r <= 0x10FFFF, "UTF-8 value too large");
                saveAndNext();
            }
            esccheck((char)current == '}', "missing '}'");
            next();  /* skip '}' */
            buff.Remove(0, i);
            //luaZ_buffremove(ls->buff, i);  /* remove saved chars from buffer */
            return (char)r;
        }


        void utf8esc()
        {
            /*
            char buff[UTF8BUFFSZ];
            int n = utf8esc(buff, readutf8esc());
            for (; n > 0; n--) {
                /* add 'buff' to string /
                save(buff[UTF8BUFFSZ - n]);
            }
            */
        }


        int readdecesc()
        {
            int i;
            int r = 0;  /* result accumulator */
            for (i = 0; i < 3 && Char.IsDigit((char)current); i++)
            {  /* read up to 3 digits */
                r = 10 * r + ((char)current - '0');
                saveAndNext();
            }
            esccheck(r <= System.Byte.MaxValue, "decimal escape too large");
            buff.Remove(0, i);
            //luaZ_buffremove(ls->buff, i);  /* remove read digits from buffer */
            return r;
        }


        void readString(char del, Token seminfo)
        {
            saveAndNext();  /* keep delimiter (for error messages) */
            while ((char)current != del)
            {
                switch ((char)current)
                {
                    case '\0':
                        lexerror("unfinished string", TokenKind.Eos);
                        break;  /* to avoid warnings */
                    case '\n':
                    case '\r':
                        lexerror("unfinished string", TokenKind.String);
                        break;  /* to avoid warnings */
                    case '\\':
                        {  /* escape sequences */
                            char c;  /* final character to be saved */
                            saveAndNext();  /* keep '\\' for error messages */
                            switch ((char)current)
                            {
                                case 'a': c = '\a'; goto read_save;
                                case 'b': c = '\b'; goto read_save;
                                case 'f': c = '\f'; goto read_save;
                                case 'n': c = '\n'; goto read_save;
                                case 'r': c = '\r'; goto read_save;
                                case 't': c = '\t'; goto read_save;
                                case 'v': c = '\v'; goto read_save;
                                case 'x': c = readhexaesc(); goto read_save;
                                case 'u': utf8esc(); goto no_save;
                                case '\n':
                                case '\r':
                                    inclinenumber(); c = '\n'; goto only_save;
                                case '\\':
                                case '\"':
                                case '\'':
                                    c = (char)current; goto read_save;
                                case '\0': goto no_save;  /* will raise an error next loop */
                                case 'z':
                                    {  /* zap following span of spaces */
                                        buff.Remove(0, 1);
                                        //luaZ_buffremove(ls->buff, 1);  /* remove '\\' */
                                        next();  /* skip the 'z' */
                                        while (Char.IsWhiteSpace((char)current))
                                        {
                                            if (currIsNewline())
                                            {
                                                inclinenumber();
                                            }
                                            else
                                            {
                                                next();
                                            }
                                        }
                                        goto no_save;
                                    }
                                default:
                                    {
                                        esccheck(Char.IsDigit((char)current), "invalid escape sequence");
                                        c = (char)readdecesc();  /* digital escape '\ddd' */
                                        goto only_save;
                                    }
                            }
                        read_save:
                            next();
                            /* go through */
                            only_save:
                            buff.Remove(0, 1);
                            //luaZ_buffremove(ls->buff, 1);  /* remove '\\' */
                            save(c);
                            /* go through */
                        no_save:
                            break;
                        }
                    default:
                        saveAndNext();
                        break;
                }
            }
            saveAndNext();  /* skip delimiter */
            seminfo.ts = buff.ToString(1, buff.Length - 2);
        }

        TokenKind llex(Token seminfo)
        {
            buff.Clear();
            for (;;)
            {
                switch ((char)current)
                {
                    case '\n':
                    case '\r':
                        {  /* line breaks */
                            inclinenumber();
                            break;
                        }
                    case ' ':
                    case '\f':
                    case '\t':
                    case '\v':
                        {  /* spaces */
                            next();
                            break;
                        }
                    case '-':
                        {  /* '-' or '--' (comment) */
                            next();
                            if ((char)current != '-')
                            {
                                return (TokenKind)'-';
                            }
                            /* else is a comment */
                            next();
                            if ((char)current == '[')
                            {  /* long comment? */
                                int sep = skipSep();
                                buff.Clear();
                                if (sep >= 0)
                                {
                                    readLongString(seminfo, sep, false);  /* skip long comment */
                                    buff.Clear();  /* previous call may dirty the buff. */
                                    break;
                                }
                            }
                            /* else short comment */
                            while (!currIsNewline() && (char)current != '\0')
                            {
                                next();  /* skip until end of line (or end of file) */
                            }
                            break;
                        }
                    case '[':
                        {  /* long string or simply '[' */
                            int sep = skipSep();
                            if (sep >= 0)
                            {
                                readLongString(seminfo, sep, true);
                                return TokenKind.String;
                            }
                            else if (sep != -1)
                            {
                                /* '[=...' missing second bracket */
                                lexerror("invalid long string delimiter", TokenKind.String);
                            }
                            return (TokenKind)'[';
                        }
                    case '=':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Eq;
                            else return (TokenKind)'=';
                        }
                    case '<':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Le;
                            else if (check_next1('<')) return TokenKind.Shl;
                            else return (TokenKind)'<';
                        }
                    case '>':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Ge;
                            else if (check_next1('>')) return TokenKind.Shr;
                            else return (TokenKind)'>';
                        }
                    case '/':
                        {
                            next();
                            if (check_next1('/')) return TokenKind.IDiv;
                            else return (TokenKind)'/';
                        }
                    case '~':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Ne;
                            else return (TokenKind)'~';
                        }
                    case ':':
                        {
                            next();
                            if (check_next1(':')) return TokenKind.DbColon;
                            else return (TokenKind)':';
                        }
                    case '"':
                    case '\'':
                        {  /* short literal strings */
                            readString((char)current, seminfo);
                            return TokenKind.String;
                        }
                    case '.':
                        {  /* '.', '..', '...', or number */
                            saveAndNext();
                            if (check_next1('.'))
                            {
                                if (check_next1('.'))
                                    return TokenKind.Dots;   /* '...' */
                                else return TokenKind.Concat;   /* '..' */
                            }
                            else if (!Char.IsDigit((char)current)) return (TokenKind)'.';
                            else return readNumeral(seminfo);
                        }
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        {
                            return readNumeral(seminfo);
                        }
                    case '\0':
                        {
                            return TokenKind.Eos;
                        }
                    default:
                        {
                            if (islalpha((char)current))
                            {  /* identifier or reserved word? */
                                do
                                {
                                    saveAndNext();
                                } while (islalnum((char)current));
                                var ts = buff.ToString();
                                seminfo.ts = ts;
                                TokenKind kind;
                                if (tokenDict.TryGetValue(ts, out kind))
                                {
                                    /* reserved word? */
                                    return kind;
                                }
                                else
                                {

                                    return TokenKind.Name;
                                }
                            }
                            else
                            {  /* single-char tokens (+ - / ...) */
                                var c = current;
                                next();
                                return c;
                            }
                        }
                }
            }
        }

        static bool islalpha(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
        }

        static bool islalnum(char c)
        {
            return islalpha(c) || Char.IsDigit(c);
        }

        static bool isreserved(string t)
        {
            return tokenDict.ContainsKey(t);
        }

        public void ReadNext()
        {
            lastline = linenumber;
            if (lookahead.token != TokenKind.Eos)
            {  /* is there a look-ahead token? */
				var tmp = t;
                t = lookahead;  /* use this one */
				lookahead = tmp;
                lookahead.token = TokenKind.Eos;  /* and discharge it */
            }
            else
            {
                t.token = llex(t);  /* read next token */
				Parser.trace("TK", txtToken(t.token));
            }
        }

        internal Token Tk
        {
            get
            {
                return t;
            }
        }

        internal TokenKind ReadLookahead()
        {
            assert(lookahead.token == TokenKind.Eos);
            lookahead.token = llex(lookahead);
			Parser.trace("TK", txtToken(lookahead.token));
            return lookahead.token;
        }

    }
}

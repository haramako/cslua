using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TLua
{
    static class TokenCharExtension
    {
        internal static bool IsChar(this Lexer.TokenChar token)
        {
            return token >= 0;
        }

        internal static char c(this Lexer.TokenChar token)
        {
            return (char)token;
        }

        internal static Lexer.TokenKind kind(this Lexer.TokenChar token)
        {
            return (Lexer.TokenKind)(-(int)token);
        }

        internal static Lexer.TokenChar tc(this Lexer.TokenKind token)
        {
            return (Lexer.TokenChar)(-(int)token);
        }
    }

    public class Lexer
    {
        internal enum TokenChar
        {
        }

        /*
        * WARNING: if you change the order of this enumeration,
        * grep "ORDER RESERVED"
        */
        internal enum TokenKind
        {
            /* terminal symbols denoted by reserved words */
            And = 1,
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
            String,
        }

        /* number of reserved words */
        //#define NUM_RESERVED	(cast_int(TK_WHILE-FIRST_RESERVED + 1))

        struct Token
        {
            internal TokenChar token;
            internal LuaValue seminfo;
        }


        /* state of the lexer plus state of the parser when shared by all
           functions */
        TokenChar current;  /* current character (charint) */
        int linenumber;  /* input line counter */
        int lastline;  /* line of last token 'consumed' */
        Token t;  /* current token */
        Token lookahead;  /* look ahead token */
        Function fs;  /* current function (parser) */
        LuaState L;
        StringBuilder buff = new StringBuilder();
        ZIO z;  /* input stream */
        Table h;  /* to avoid collection/reuse strings */
        Parser.DynData dyd;  /* dynamic structures used by the parser */
        string source;  /* current source name */
        string envn;  /* environment variable name */


        public Lexer(LuaState L)
        {
        }


        void next() {
            current = (TokenChar)z.ReadByte();
        }

        bool currIsNewline() {
            return (char)current == '\n' || (char)current == '\r';
        }

        static string[] tokens = new string[]
        {
            "and", "break", "do", "else", "elseif",
            "end", "false", "for", "function", "goto", "if",
            "in", "local", "nil", "not", "or", "repeat",
            "return", "then", "true", "undef", "until", "while",
            "//", "..", "...", "==", ">=", "<=", "~=",
            "<<", ">>", "::", "<eof>",
            "<number>", "<integer>", "<name>", "<string>"
        };

        static Dictionary<string, int> tokenDict;

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

        void save(TokenChar c)
        {
            buff.Append((char)c);
        }

        static Lexer()
        {
            int i = 0;
            tokenDict = tokens.ToDictionary(t => t, t => i++);
        }


        string token2str(TokenChar token) 
        {
            if( token.IsChar())
            {
                return tokens[(int)token.kind()-1];
            }
            else
            {
                return ((char)token).ToString();
            }
        }


        string txtToken (TokenChar token)
        {
            switch ((TokenKind)token)
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

        void lexerror(string msg, TokenKind token) {
            throw new ParserException(string.Format("{0} near {1}", msg, txtToken(token.tc())));
        }

        void syntaxerror(string msg)
        {
            lexerror(msg, t.token.kind());
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


        void setinput(LuaState L_, ZIO z_, string source_, TokenChar firstchar)
        {
            t.token = 0;
            L = L_;
            current = firstchar;
            lookahead.token = TokenKind.Eos.tc();  /* no look-ahead token */
            z = z_;
            fs = null; 
            linenumber = 1;
            lastline = 1;
            source = source_;
            envn = "_ENV";  /* get env name */
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
        TokenKind readNumeral(out LuaValue seminfo)
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
            save('\0');
            float num;
            if (!Single.TryParse(buff.ToString(), out num))
            {
                lexerror("malformed number", TokenKind.Float);
            }
            LuaValue obj = new LuaValue(num);
            seminfo = obj;
            if (obj.IsInteger)
            {
                return TokenKind.Int;
            }
            else
            {
                return TokenKind.Float;
            }
        }


        void assert(bool pred)
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
            TokenChar s = current;
            assert((char)s == '[' || (char)s == ']');
            saveAndNext();
            while ((char)current == '=')
            {
                saveAndNext();
                count++;
            }
            return (current == s) ? count : (-count) - 1;
        }


        void readLongString(out LuaValue seminfo, int sep, bool isString)
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
                seminfo = new LuaValue(buff.ToString());
            }
            else
            {
                seminfo = new LuaValue();
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


        void readString(char del, out LuaValue seminfo)
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
            seminfo = new LuaValue(buff.ToString(1, buff.Length - 2));
        }

        TokenChar llex(out LuaValue seminfo)
        {
            seminfo = new LuaValue();
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
                                return (TokenChar)'-';
                            }
                            /* else is a comment */
                            next();
                            if ((char)current == '[')
                            {  /* long comment? */
                                int sep = skipSep();
                                buff.Clear();
                                if (sep >= 0)
                                {
                                    readLongString(out seminfo, sep, false);  /* skip long comment */
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
                                readLongString(out seminfo, sep, true);
                                return TokenKind.String.tc();
                            }
                            else if (sep != -1)
                            {
                                /* '[=...' missing second bracket */
                                lexerror("invalid long string delimiter", TokenKind.String);
                            }
                            return (TokenChar)'[';
                        }
                    case '=':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Eq.tc();
                            else return (TokenChar)'=';
                        }
                    case '<':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Le.tc();
                            else if (check_next1('<')) return TokenKind.Shl.tc();
                            else return (TokenChar)'<';
                        }
                    case '>':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Ge.tc();
                            else if (check_next1('>')) return TokenKind.Shr.tc();
                            else return (TokenChar)'>';
                        }
                    case '/':
                        {
                            next();
                            if (check_next1('/')) return TokenKind.IDiv.tc();
                            else return (TokenChar)'/';
                        }
                    case '~':
                        {
                            next();
                            if (check_next1('=')) return TokenKind.Ne.tc();
                            else return (TokenChar)'~';
                        }
                    case ':':
                        {
                            next();
                            if (check_next1(':')) return TokenKind.DbColon.tc();
                            else return (TokenChar)':';
                        }
                    case '"':
                    case '\'':
                        {  /* short literal strings */
                            readString((char)current, out seminfo);
                            return TokenKind.DbColon.tc();
                        }
                    case '.':
                        {  /* '.', '..', '...', or number */
                            saveAndNext();
                            if (check_next1('.'))
                            {
                                if (check_next1('.'))
                                    return TokenKind.Dots.tc();   /* '...' */
                                else return TokenKind.Concat.tc();   /* '..' */
                            }
                            else if (!Char.IsDigit((char)current)) return (TokenChar)'.';
                            else return readNumeral(out seminfo).tc();
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
                            return readNumeral(out seminfo).tc();
                        }
                    case '\0':
                        {
                            return TokenKind.Eos.tc();
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
                                seminfo = new LuaValue(tc);
                                if (isreserved(ts))  /* reserved word? */
                                    return ts->extra - 1 + FIRST_RESERVED;
                                else
                                {
                                    return TokenKind.Name.tc();
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


        void luaX_next(LexState* ls)
        {
            ls->lastline = ls->linenumber;
            if (ls->lookahead.token != TK_EOS)
            {  /* is there a look-ahead token? */
                ls->t = ls->lookahead;  /* use this one */
                ls->lookahead.token = TK_EOS;  /* and discharge it */
            }
            else
                ls->t.token = llex(ls, &ls->t.seminfo);  /* read next token */
        }


        int luaX_lookahead(LexState* ls)
        {
            lua_assert(ls->lookahead.token == TK_EOS);
            ls->lookahead.token = llex(ls, &ls->lookahead.seminfo);
            return ls->lookahead.token;
        }

    }
}

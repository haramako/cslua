using System;
using System.IO;

namespace TLua
{
	public class ZIO
	{
		BinaryReader r_;

		public ZIO(Stream s)
		{
			r_ = new BinaryReader(s, System.Text.Encoding.UTF8);
		}

		public byte[] ReadBytes(int size)
		{
			return r_.ReadBytes(size);
		}

        public int ReadChar()
        {
            if (r_.PeekChar() == -1) {
                return -1;
            } else {
                return r_.ReadChar();
            }
        }

		public byte ReadByte()
		{
			return r_.ReadByte();
		}

		public int ReadInt()
		{
			return r_.ReadInt32();
		}

		public long ReadInt64()
		{
			return r_.ReadInt64();
		}

		public double ReadNumber()
		{
			return r_.ReadDouble();
		}

		public double ReadDouble()
		{
			return r_.ReadDouble();
		}

        public int ReadSize()
        {
            int r = 0;
            for (;;)
            {
                var b = ReadByte();
                r = r << 7 | (b & 0x7f);
                if ( b >= 0x80)
                {
                    break;
                }
            }
            return r;
        }

		public string ReadString()
		{
			var size = ReadSize();
			if (size == 0) {
				return "";
			} else {
                var r = System.Text.Encoding.UTF8.GetString(r_.ReadBytes(size - 1));
                //Console.WriteLine("s {0} ''{1}''", size, r);
                return r;
			}
		}



	}
}

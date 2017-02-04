using System;
using System.Diagnostics;

namespace TLua
{
	public enum ValueType
	{
		Nil,
		Integer,
		Float,
		Bool,
		String,
		Table,
		UserData,
	}

	public struct LuaValue : IEquatable<LuaValue>
	{
		const ulong NanBoxingMask = 0xffff000000000000;
		const ulong ValueMask = 0x0000ffffffffffff;
		const ulong NanBits = 0x7ff8000000000000;
		const ulong NonFloatBits = 0xfff0000000000000;
		const ulong SignMask = 0x0000800000000000;
		const ulong MinusBits = 0xffff000000000000;

		const ulong NilMark = (((ulong)ValueType.Nil) << 48) | NonFloatBits;
		const ulong IntegerMark = (((ulong)ValueType.Integer) << 48) | NonFloatBits;
		const ulong StringMark = (((ulong)ValueType.String) << 48) | NonFloatBits;
		const ulong BoolMark = (((ulong)ValueType.Bool) << 48) | NonFloatBits;
		const ulong TableMark = (((ulong)ValueType.Table) << 48) | NonFloatBits;
		const ulong UserDataMark = (((ulong)ValueType.UserData) << 48) | NonFloatBits;

		// type用の16bit(bit63..48)の情報
		const int Type16Mask = 0x0007;
		const int Type16FloatBits = 0xfff8;
		const int Type16NotFloat = 0x7ff8;
		const int Type16Nan = 0x0008;


		ulong val_;
		object obj_;

		public static LuaValue Nil {
			get {
				return new LuaValue((object)null);
			}
		}

		public LuaValue(LuaValue v)
		{
			val_ = v.val_;
			obj_ = v.obj_;
		}

		public LuaValue(long v)
		{
			val_ = 0;
			obj_ = null;
			AsInt = v;
		}

		public LuaValue(double v)
		{
			val_ = 0;
			obj_ = null;
			AsFloat = v;
		}

		public LuaValue(string v)
		{
			if (v == null) {
				val_ = NilMark;
				obj_ = null;
			}else{
				val_ = StringMark;
				obj_ = v;
			}
		}

		public LuaValue(bool v)
		{
			val_ = (v ? (ulong)1 : (ulong)0) | BoolMark;
			obj_ = null;
		}

		public LuaValue(LuaTable v)
		{
			val_ = TableMark;
			obj_ = v;
		}

		public LuaValue(object v)
		{
			val_ = 0;
			obj_ = null;
			AsUserData = v;
		}

		[Conditional("DEBUG")]
		public void check(bool cond)
		{
			if (!cond) {
				throw new Exception("assert failed!");
			}
		}

		[Conditional("DEBUG")]
		public void checkType(ValueType t)
		{
			check(ValueType == t);
		}

		public ValueType ValueType {
			get {
				if (val_ < NonFloatBits) {
					return ValueType.Float;
				} else {
					var type16 = (int)((val_ & NanBoxingMask) >> 48);
					return (ValueType)(type16 & Type16Mask);
				}
			}
		}

		public void Clear() {
			val_ = NilMark;
			obj_ = null;
		}

		public bool IsNil {
			get {
				return (ValueType == ValueType.Nil);
			}
		}

		// TODO: 48bit以上の扱い
		public long AsInt {
			get {
				checkType(ValueType.Integer);
				ulong result = val_ & ValueMask;
				if ((result & SignMask) != 0) {
					return (long)(result | MinusBits);
				} else {
					return (long)result;
				}
			}
			set {
				val_ = ((ulong)value & ValueMask) | IntegerMark;
			}
		}

		public double AsFloat {
			get {
				checkType(ValueType.Float);
				return BitConverter.Int64BitsToDouble((long)val_);
			}
			set {
				if (double.IsNaN(value)) {
					val_ = NanBits;
				} else {
					val_ = (ulong)BitConverter.DoubleToInt64Bits(value);
				}
			}
		}

		public bool AsBool {
			get {
				checkType(ValueType.Bool);
				ulong result = val_ & ValueMask;
				return result != 0;
			}
			set {
				val_ = (value ? (ulong)1 : (ulong)0) | BoolMark;
			}
		}

		public string AsString {
			get {
				checkType(ValueType.String);
				return (string)obj_;
			}
			set {
				val_ = StringMark;
				obj_ = value;
			}
		}

		public LuaTable AsTable {
			get {
				checkType(ValueType.Table);
				return (LuaTable)obj_;
			}
			set {
				val_ = TableMark;
				obj_ = value;
			}
		}

		public object AsUserData {
			get {
				checkType(ValueType.UserData);
				return obj_;
			}
			set {
				if (value == null) {
					val_ = NilMark;
					obj_ = null;
				} else {
					var type = value.GetType();
					if (type == typeof(string)) {
						AsString = (string)value;
					} else if (type == typeof(LuaTable)) {
						AsTable = (LuaTable)value;
					} else {
						val_ = UserDataMark;
						obj_ = value;
					}
				}
			}
		}

		//====================================================
		// Operators
		//====================================================

		public bool Equals(LuaValue x)
		{
			if (x.val_ == val_) {
				switch (this.ValueType) {
				case ValueType.String:
					return (string)this.obj_ == (string)x.obj_;
				case ValueType.Table:
				case ValueType.UserData:
					return this.obj_ == x.obj_;
				default:
					return true;
				}
			} else {
				return false;
			}
		}

		public static bool operator ==(LuaValue a, LuaValue b)
		{
			return a.Equals(b);
		}

		public static bool operator !=(LuaValue a, LuaValue b)
		{
			return !a.Equals(b);
		}
	}
}

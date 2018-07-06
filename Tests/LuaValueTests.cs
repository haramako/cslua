using System;
using NUnit.Framework;

namespace TLua
{
	[TestFixture]
	public class LuaValueTests
	{
		public static void Main(string[] args)
		{
		}

		[TestCase]
		public void InitNilTest()
		{
			var v = LuaValue.Nil;
			Assert.AreEqual(v.ValueType, ValueType.Nil);
			Assert.AreEqual(true, v.IsNil);
		}

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(-1)]
		[TestCase(int.MaxValue)]
		[TestCase(int.MinValue)]
		public void InitIntegerTest(int i)
		{
			var v = new LuaValue(i);
			Assert.AreEqual(ValueType.Integer, v.ValueType);
			Assert.AreEqual(i, v.AsInt);
		}

		[TestCase(0.0)]
		[TestCase(1.0)]
		[TestCase(double.NaN)]
		[TestCase(-double.NaN)]
		[TestCase(double.MaxValue)]
		[TestCase(double.MinValue)]
		public void InitFloatTest(double d)
		{
			var v = new LuaValue(d);
			Assert.AreEqual(ValueType.Float, v.ValueType);
			Assert.AreEqual(d, v.AsFloat);
		}

		[TestCase(true)]
		[TestCase(false)]
		public void InitBoolTest(bool b)
		{
			var v = new LuaValue(b);
			Assert.AreEqual(ValueType.Bool, v.ValueType);
			Assert.AreEqual(b, v.AsBool);
		}

		[TestCase("")]
		[TestCase("hoge")]
		public void InitStringTest(string s)
		{
			var v = new LuaValue(s);
			Assert.AreEqual(ValueType.String, v.ValueType);
			Assert.AreEqual(s, v.AsString);
		}

		[TestCase]
		public void InitNilStringTest()
		{
			var v = new LuaValue((string)null);
			Assert.AreEqual(ValueType.Nil, v.ValueType);
			Assert.AreEqual(true, v.IsNil);
		}

		[TestCase]
		public void InitTableDataTest()
		{
			var t = new Table();
			var v = new LuaValue(t);
			Assert.AreEqual(ValueType.Table, v.ValueType);
			Assert.AreEqual(t, v.AsTable);
		}

		[TestCase]
		public void InitUserDataTest()
		{
			{
				object obj = new object();
				var v = new LuaValue(obj);
				Assert.AreEqual(ValueType.UserData, v.ValueType);
				Assert.AreEqual(obj, v.AsUserData);
			}

			{
				object obj = "string";
				var v = new LuaValue(obj);
				Assert.AreEqual(ValueType.String, v.ValueType);
				Assert.AreEqual(obj, v.AsString);

				v.Clear();
				Assert.AreEqual(ValueType.Nil, v.ValueType);
				Assert.AreEqual(true, v.IsNil);
			}
		}

		[TestCase]
		public void InitTableTest()
		{
			{
				var t = new Table();
				var v = new LuaValue(t);
				Assert.AreEqual(ValueType.Table, v.ValueType);
				Assert.AreEqual(t, v.AsTable);
			}
		}

		public void SampleLuaApi(LuaState L)
		{
		}

		[TestCase]
		public void InitClosureTest()
		{
			{
				var f = new Function();
				var c = new Closure(f);
				var v = new LuaValue(c);
				Assert.AreEqual(ValueType.Closure, v.ValueType);
				Assert.AreEqual(c, v.AsClosure);
			}

			{
				var v = new LuaValue((Closure)null);
				Assert.AreEqual(ValueType.Nil, v.ValueType);
			}
		}

		[TestCase]
		public void InitLuaApiTest()
		{
			{
				var v = new LuaValue(SampleLuaApi);
				Assert.AreEqual(ValueType.LuaApi, v.ValueType);
				Assert.AreEqual((LuaApi)SampleLuaApi, v.AsLuaApi);
			}
			{
				var v = new LuaValue((LuaApi)null);
				Assert.AreEqual(ValueType.Nil, v.ValueType);
			}
		}

		[TestCase]
		public void EqualTest()
		{
			Assert.True(new LuaValue(1) == new LuaValue(1));
			Assert.False(new LuaValue(1) != new LuaValue(1));
			Assert.False(new LuaValue(1) == new LuaValue(2));
			Assert.True(new LuaValue(1) != new LuaValue(2));

			Assert.True(new LuaValue("hoge") == new LuaValue("hoge"));
			Assert.True(new LuaValue("hoge") != new LuaValue("fuga"));
			Assert.True(new LuaValue("hoge") != LuaValue.Nil);
		}

	}
}

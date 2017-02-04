using System;
using NUnit.Framework;

namespace TLua
{
	[TestFixture]
	public class LuaTableTests
	{
		[TestCase]	
		public void TestInit()
		{
			var t = new Table();
			t.SetByLuaIdx(1, new LuaValue(1));
			t.SetByLuaIdx(2, new LuaValue(2));
			Assert.AreEqual(t.GetByLuaIdx(1), new LuaValue(1));
		}

		[TestCase]
		public void TestResize()
		{
			var t = new Table();
			t.Resize(10);
			Assert.True(t[0].IsNil);
			Assert.True(t[9].IsNil);
			Assert.AreEqual(t.ArraySize, 10);
		}
	}
}

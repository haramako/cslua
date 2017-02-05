using System;
using System.Collections.Generic;

namespace TLua
{
	public sealed class Table
	{
		public struct Range
		{
			public int Start;
			public int End;
			public bool Valid {
				get {
					return Start >= 0 && End >= 0;
				}
			}
		}

		List<LuaValue> array_;
		Dictionary<string, LuaValue> map_;

		public Table()
		{
			array_ = new List<LuaValue>();
			map_ = new Dictionary<string,LuaValue>();
		}

		public Table(int arrayCapacity, int mapCapacity = 0)
		{
			if (arrayCapacity > 0) {
				array_ = new List<LuaValue>(arrayCapacity);
				Resize(arrayCapacity);
			}
			if (mapCapacity > 0) {
				map_ = new Dictionary<string,LuaValue>(mapCapacity);
			}
		}

		public List<LuaValue> GetRawArray()
		{
			return array_;
		}

		public Dictionary<string,LuaValue> GetRawMap()
		{
			return map_;
		}

		public LuaValue this[int idx]{
			get {
				return array_[idx];
			}
			set {
				array_[idx] = value;
			}
		}

		public LuaValue this[string idx] {
			get {
				return map_[idx];
			}
			set {
				map_[idx] = value;
			}
		}

		// luaのインデックス表記を実際のarray_のインデックスに直す
		// 範囲外の場合は-1を返す
		int luaIdxToRawIdx(int idx)
		{
			if (idx < 0) {
				idx = array_.Count + idx;
			}
			return idx - 1;
		}

		public int Size {
			get {
				return array_.Count + map_.Count;
			}
		}

		public int ArraySize {
			get {
				return array_.Count;
			}
		}

		public void Resize(int newSize)
		{
			if (newSize < array_.Count) {
				array_.RemoveRange(newSize, array_.Count - newSize);
			} else {
				for (int i = array_.Count; i < newSize; i++) {
					array_.Add(LuaValue.Nil);
				}
			}
		}

		public Range GetRange(LuaValue start, LuaValue end)
		{
			int istart, iend;
			if (start.IsNil) {
				istart = 0;
			} else if (start.IsNumber) {
				istart = start.ConvertToInt();
				if (istart < 0) {
					istart = array_.Count + istart;
				} else {
					istart = istart - 1;
				}
			} else {
				throw new LuaException("invalid start index " + start);
			}

			if (end.IsNil) {
				iend = array_.Count;
			} else if (end.IsNumber) {
				iend = end.ConvertToInt();
				if (iend < 0) {
					iend = array_.Count + iend + 1;
				}
			} else {
				throw new LuaException("invalid end index " + start);
			}

			if (istart < 0 || istart >= array_.Count || iend < 0 || iend > array_.Count) {
				return new Range() { Start = -1, End = -1 };
			} else {
				return new Range() { Start = istart, End = iend };
			}
		}

		// Lua流のインデックスでアクセスする
		// - 1はじまり
		// - -1は配列の一番最後の要素
		// - サイズ外の場合は、nilを返す
		public LuaValue GetByLuaIdx(int luaIdx)
		{
			var idx = luaIdxToRawIdx(luaIdx);
			if (idx >= 0 && idx < array_.Capacity) {
				return array_[idx];
			} else {
				return LuaValue.Nil;
			}
		}

		// Lua流のインデックスで値を設定する
		public void SetByLuaIdx(int luaIdx, LuaValue val)
		{
			var idx = luaIdxToRawIdx(luaIdx);
			if (idx >= 0) {
				if (idx >= array_.Count) {
					Resize(idx+1);
				}
				array_[idx] = val;
			} else {
				// DO NOTHING	
			}
		}

		public LuaValue this[LuaValue idx] {
			get {
				return GetByLuaValue(idx);
			}
			set {
				SetByLuaValue(idx, value);
			}
		}

		public LuaValue GetByLuaValue(LuaValue idx)
		{
			switch(idx.ValueType){
			case ValueType.Integer: 
			case ValueType.Float: {
					var rawIdx = luaIdxToRawIdx(idx.ConvertToInt());
					if (rawIdx >= 0 && rawIdx < array_.Count) {
						return array_[rawIdx];
					} else {
						return LuaValue.Nil;
					}
				}
			case ValueType.String: {
					LuaValue result;
					if (map_.TryGetValue(idx.AsString, out result)) {
						return result;
					} else {
						return LuaValue.Nil;
					}
				}
			default:
				throw new LuaException("invalid indexing");
			}
		}

		public void SetByLuaValue(LuaValue idx, LuaValue val)
		{
			switch (idx.ValueType) {
			case ValueType.Integer: 
			case ValueType.Float:
				var rawIdx = luaIdxToRawIdx(idx.ConvertToInt());
				if (rawIdx >= 0) {
					if (rawIdx >= array_.Count) {
						Resize(rawIdx + 1);
					}
					array_[rawIdx] = val;
				} else {
					// DO NOTHING
				}
				break;
			case ValueType.String:
				map_[idx.AsString] = val;
				break;
			default:
				throw new LuaException("invalid indexing");
			}
		}
	}
}

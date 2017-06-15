﻿using System;
using System.IO;
using symfile.util;

namespace symfile
{
	public class Label
	{
		private readonly TypedValue typedOffset;

		public int offset => typedOffset.value;

		public string name { get; private set; }

		public Label(TypedValue typedValue, BinaryReader fs)
		{
			this.typedOffset = typedValue;
			this.name = fs.readPascalString();
		}

		public override string ToString()
		{
			return $"0x{offset:X} {name}";
		}
	}
}
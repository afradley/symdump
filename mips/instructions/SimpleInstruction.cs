﻿using System;
using System.Linq;
using core;

namespace mips.instructions
{
    public class SimpleInstruction : Instruction
    {
        public readonly string format;
        public readonly string mnemonic;

        public SimpleInstruction(string mnemonic, string format, params IOperand[] operands)
        {
            this.mnemonic = mnemonic;
            this.operands = operands;
            this.format = format;
        }

        public override IOperand[] operands { get; }

        public override string ToString()
        {
            var args = string.Join(", ", operands.Select(o => o.ToString()));
            return $"{mnemonic} {args}".Trim();
        }

        public override string asReadable()
        {
            return format == null ? ToString() : string.Format(format, operands);
        }

        public override IExpressionNode toExpressionNode(IDataFlowState dataFlowState)
        {
            throw new Exception("Cannot convert simple instruction to expression node");
        }
    }
}
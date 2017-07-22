﻿using core;
using core.expression;

namespace mips.operands
{
    public class LabelOperand : IOperand
    {
        // TODO address
        public readonly string label;

        public LabelOperand(string label)
        {
            this.label = label;
        }

        public bool Equals(IOperand other)
        {
            var o = other as LabelOperand;
            return label == o?.label;
        }

        public IExpressionNode toExpressionNode(IDataFlowState dataFlowState)
        {
            return new NamedMemoryLayout(label, dataFlowState.debugSource.findTypeDefinitionForLabel(label));
        }

        public override string ToString()
        {
            return label;
        }
    }
}
using System;
using System.IO;
using symdump.exefile;
using symdump.exefile.util;
using symdump.symfile;

namespace symdump
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            if (!File.Exists(args[0]))
                return;

            SymFile symFile;
            using (var fs = new FileStream(args[0], FileMode.Open))
            {
                symFile = new SymFile(new BinaryReader(fs));
            }

            var labelDefFileName = Path.ChangeExtension(args[0], "map");

            FileStream output = new FileStream(labelDefFileName, FileMode.Create);
            StreamWriter writer = new StreamWriter(output);
            foreach (Function func in symFile.functions)
            {
                writer.Write(func.address.ToString("x") + ":\r\n" + ".code\t" + func.Name + "\r\n");
            }

            foreach (System.Collections.Generic.KeyValuePair<string, Variable> pair in symFile.variables)
            {
                Variable variable = pair.Value;
                WriteVariableRecursive(symFile, writer, variable.m_Name, variable.m_Address, variable.m_TypeInfo);
            }

            output.Flush();
        }

        static void WriteVariableRecursive(SymFile symFile, StreamWriter writer, string name, uint address, TypeInfo typeInfo)
        {
            if (typeInfo.isFake)
            {
                return;
            }

            BaseType baseType = typeInfo.typeDef.baseType;

            uint arrayLength = 1;
            bool isArray = Array.IndexOf(typeInfo.typeDef.derivedTypes, DerivedType.Array) >= 0;
            if (isArray)
            {
                int dims = typeInfo.dims.Length;
                foreach (uint d in typeInfo.dims)
                {
                    arrayLength *= d;
                }
            }

            arrayLength = Math.Max(1, arrayLength);
            uint arraySize = Math.Max(1, typeInfo.size / arrayLength);

            for (int i = 0; i < arrayLength; i++)
            {
                uint arrayAddress = address + (arraySize * (uint)i);
                string arrayName = name;
                if (isArray)
                {
                    arrayName += "[" + i.ToString() + "]";
                }

                if (baseType == BaseType.StructDef)
                {
                    StructDef structDef = symFile.m_structs[typeInfo.tag];
                    foreach (StructMember member in structDef.members)
                    {
                        uint memberAddress = arrayAddress + unchecked((uint)member.typedValue.value);
                        string memberName = arrayName + "." + member.name;
                        if (Array.IndexOf(member.typeInfo.typeDef.derivedTypes, DerivedType.Pointer) >= 0)
                        {
                            WriteVariable(symFile, writer, memberName, memberAddress, member.typeInfo, true);
                        }
                        else
                        {
                            WriteVariableRecursive(symFile, writer, memberName, memberAddress, member.typeInfo);
                        }
                    }
                }
                else
                {
                    WriteVariable(symFile, writer, arrayName, arrayAddress, typeInfo, false);
                }
            }
        }

        static void WriteVariable(SymFile symFile, StreamWriter writer, string name, uint address, TypeInfo typeInfo, bool isPointer)
        {
            string typeCode = null;

            if (isPointer)
            {
                typeCode = ".word\t";
            }
            else
            {
                switch (typeInfo.typeDef.baseType)
                {
                    case BaseType.Null:
                    case BaseType.StructDef:
                    case BaseType.EnumMember:
                        break;
                    case BaseType.UnionDef:
                    case BaseType.EnumDef:
                        {
                            if (typeInfo.size < 2)
                            {
                                typeCode = ".byte\t";
                            }
                            else if (typeInfo.size < 3)
                            {
                                typeCode = ".half\t";
                            }
                            else
                            {
                                typeCode = ".word\t";
                            }

                            break;
                        }
                    case BaseType.Char:
                    case BaseType.UChar:
                        typeCode = ".byte\t";
                        break;
                    case BaseType.Short:
                    case BaseType.UShort:
                        typeCode = ".half\t";
                        break;
                    case BaseType.Int:
                    case BaseType.UInt:
                    case BaseType.Long:
                    case BaseType.ULong:
                        typeCode = ".word\t";
                        break;
                    case BaseType.Double:
                        typeCode = ".dword\t";
                        break;
                    case BaseType.Float:
                        typeCode = ".float\t";
                        break;
                    case BaseType.Void:
                        typeCode = ".word\t";
                        break;
                    default:
                        break;
                }
            }

            if (typeCode != null)
            {
                writer.Write(address.ToString("x") + ":\r\n" + typeCode + name + "\r\n");
            }
        }
    }
}
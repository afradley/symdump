using System;
using System.IO;
using symdump.exefile;
using symdump.exefile.util;
using symdump.symfile;

namespace symdump
{
    internal class Program
    {
        static string fileName = null;
        static FileStream output = null;
        static StreamWriter writer = null;
        static uint numEntries = 0;
        static uint fileIndex = 0;
        static uint maxEntries = 100000;
        static uint maxArrayLength = 1000;

        private static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                return;
            }

            fileName = args[0];

            if (args.Length >= 2) UInt32.TryParse(args[1], out maxEntries);
            if (args.Length >= 3) UInt32.TryParse(args[2], out maxArrayLength);

            if (!File.Exists(args[0]))
            {
                return;
            }

            SymFile symFile;
            using (var fs = new FileStream(fileName, FileMode.Open))
            {
                symFile = new SymFile(new BinaryReader(fs));
            }

            foreach (Function func in symFile.functions)
            {
                uint address = 0x00000000 + func.address;
                WriteEntry(address.ToString("x").PadLeft(8, '0') + ":\r\n" + ".code\t" + func.Name + "\r\n");
            }

            foreach (System.Collections.Generic.KeyValuePair<string, Variable> pair in symFile.variables)
            {
                Variable variable = pair.Value;
                uint address = 0x00000000 + variable.m_Address;
                WriteVariableRecursive(symFile, variable.m_Name, address, variable.m_TypeInfo);
            }
        }

        static void WriteVariableRecursive(SymFile symFile, string name, uint address, TypeInfo typeInfo)
        {
            if (typeInfo.isFake)
            {
                return;
            }

            BaseType baseType = typeInfo.typeDef.baseType;

            uint arrayLength = 1;
            //bool isArray = IsArray(typeInfo.typeDef);
            bool isArray = Array.IndexOf(typeInfo.typeDef.derivedTypes, DerivedType.Array) >= 0;
            bool isPointer = Array.IndexOf(typeInfo.typeDef.derivedTypes, DerivedType.Pointer) >= 0;

            if (isArray)
            {
                int dims = typeInfo.dims.Length;
                foreach (uint d in typeInfo.dims)
                {
                    arrayLength *= d;
                }
            }

            arrayLength = Math.Max(1, arrayLength);
            uint arrayEntrySize = Math.Max(1, typeInfo.size / arrayLength);

            // Only cap the array length after getting the arrayEntrySize.
            arrayLength = Math.Min(arrayLength, maxArrayLength);

            for (int i = 0; i < arrayLength; i++)
            {
                uint arrayAddress = address + (arrayEntrySize * (uint)i);
                string arrayName = name;
                if (isArray)
                {
                    arrayName += "[" + i.ToString() + "]";
                }

                if (baseType == BaseType.StructDef && !isPointer)
                {
                    StructDef structDef = symFile.m_structs[typeInfo.tag];
                    foreach (StructMember member in structDef.members)
                    {
                        uint memberAddress = arrayAddress + unchecked((uint)member.typedValue.value);
                        string memberName = arrayName + "." + member.name;
                        WriteVariableRecursive(symFile, memberName, memberAddress, member.typeInfo);
                    }
                }
                else
                {
                    WriteVariable(symFile, arrayName, arrayAddress, typeInfo, isPointer);
                }
            }
        }

        static void WriteVariable(SymFile symFile, string name, uint address, TypeInfo typeInfo, bool isPointer)
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
                WriteEntry(address.ToString("x").PadLeft(8, '0') + ":\r\n" + typeCode + name + "\r\n");
            }
        }

        static void WriteEntry(string entry)
        {
            if (output == null)
            {
                string labelDefFileName =
                    Path.GetFileNameWithoutExtension(fileName) +
                    "." + fileIndex + ".map";

                output = new FileStream(labelDefFileName, FileMode.Create);
                writer = new StreamWriter(output);
            }

            writer.Write(entry);
            writer.Flush();

            numEntries++;
            if (numEntries > maxEntries)
            {
                output = null;
                writer = null;
                numEntries = 0;
                fileIndex++;
            }
        }

        static bool IsArray(TypeDef typeDef)
        {
            // This is so that pointers to arrays don't get expanded.
            bool isArray = false;
            foreach (DerivedType type in typeDef.derivedTypes)
            {
                if (type == DerivedType.None)
                {
                    break;
                }

                if (type == DerivedType.Array)
                {
                    isArray = true;
                }
                else
                {
                    break;
                }
            }

            return isArray;
        }
    }
}
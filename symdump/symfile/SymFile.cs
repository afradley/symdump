﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using symdump.symfile.util;
using symdump.util;

namespace symdump.symfile
{
    public class SymFile
    {
        private readonly Dictionary<string, EnumDef> m_enums = new Dictionary<string, EnumDef>();
        private readonly SortedSet<string> m_externs = new SortedSet<string>();
        public readonly List<Function> functions = new List<Function>();
        private readonly Dictionary<string, string> m_funcTypes = new Dictionary<string, string>();
        internal readonly Dictionary<uint, List<Label>> labels = new Dictionary<uint, List<Label>>();
        private readonly Dictionary<string, StructDef> m_structs = new Dictionary<string, StructDef>();
        private readonly byte m_targetUnit;
        private readonly Dictionary<string, TypeInfo> m_typedefs = new Dictionary<string, TypeInfo>();
        private readonly Dictionary<string, UnionDef> m_unions = new Dictionary<string, UnionDef>();
        private readonly byte m_version;
        private string m_mxInfo;

        public SymFile(BinaryReader stream)
        {
            stream.BaseStream.Seek(0, SeekOrigin.Begin);

            stream.skip(3);
            m_version = stream.ReadByte();
            m_targetUnit = stream.ReadByte();

            stream.skip(3);
            while (stream.BaseStream.Position < stream.BaseStream.Length)
                dumpEntry(stream);
        }

        public void dump(TextWriter output)
        {
            var writer = new IndentedTextWriter(output);
            writer.WriteLine($"Version = {m_version}, targetUnit = {m_targetUnit}");

            writer.WriteLine();
            writer.WriteLine($"// {m_enums.Count} enums");
            foreach (var e in m_enums.Values)
                e.dump(writer);

            writer.WriteLine();
            writer.WriteLine($"// {m_unions.Count} unions");
            foreach (var e in m_unions.Values)
                e.dump(writer);

            writer.WriteLine();
            writer.WriteLine($"// {m_structs.Count} structs");
            foreach (var e in m_structs.Values)
                e.dump(writer);

            writer.WriteLine();
            writer.WriteLine($"// {m_typedefs.Count} typedefs");
            foreach (var t in m_typedefs)
                writer.WriteLine($"typedef {t.Value.asCode(t.Key)};");

            writer.WriteLine();
            writer.WriteLine($"// {labels.Count} labels");
            foreach (var l in labels)
            foreach (var l2 in l.Value)
                writer.WriteLine(l2);

            writer.WriteLine();
            writer.WriteLine($"// {m_externs.Count} external declarations");
            foreach (var e in m_externs)
                writer.WriteLine(e);

            writer.WriteLine();
            writer.WriteLine($"// {functions.Count} functions");
            foreach (var f in functions)
                f.dump(writer);
        }

        private void dumpEntry(BinaryReader stream)
        {
            var typedValue = new TypedValue(stream);
            if (typedValue.type == 8)
            {
                m_mxInfo = $"${typedValue.value:X} MX-info {stream.ReadByte():X}";
                return;
            }

            if (typedValue.isLabel)
            {
                var lbl = new Label(typedValue, stream);

                if (!labels.ContainsKey(lbl.offset))
                    labels.Add(lbl.offset, new List<Label>());

                labels[lbl.offset].Add(lbl);
                return;
            }

            switch (typedValue.type & 0x7f)
            {
                case 0:
#if WITH_SLD
                writer.WriteLine($"${typedValue.value:X} Inc SLD linenum");
                #endif
                    break;
                case 2:
#if WITH_SLD
                writer.WriteLine($"${typedValue.value:X} Inc SLD linenum by byte {stream.ReadU1()}");
                #else
                    stream.skip(1);
#endif
                    break;
                case 4:
#if WITH_SLD
                writer.WriteLine($"${typedValue.value:X} Inc SLD linenum by word {stream.ReadUInt16()}");
#else
                    stream.skip(2);
#endif
                    break;
                case 6:
#if WITH_SLD
                writer.WriteLine($"${typedValue.value:X} Set SLD linenum to {stream.ReadUInt32()}");
#else
                    stream.skip(4);
#endif
                    break;
                case 8:
#if WITH_SLD
                writer.WriteLine($"${typedValue.value:X} Set SLD to line {stream.ReadUInt32()} of file " +
                    stream.readPascalString());
#else
                    stream.skip(4);
                    stream.skip(stream.ReadByte());
#endif
                    break;
                case 10:
#if WITH_SLD
                writer.WriteLine($"${typedValue.value:X} End SLD info");
#endif
                    break;
                case 12:
                    dumpType12(stream, typedValue.value);
                    break;
                case 20:
                    dumpType20(stream, typedValue.value);
                    break;
                case 22:
                    dumpType22(stream, typedValue.value);
                    break;
                default:
                    throw new Exception("Sodom");
            }
        }

        private void dumpType12(BinaryReader stream, int offset)
        {
            functions.Add(new Function(stream, (uint) offset, m_funcTypes));
            //writer.WriteLine("{");
            //++writer.Indent;
        }

        private void readEnum(BinaryReader reader, string name)
        {
            var e = new EnumDef(reader, name);

            EnumDef already;
            if (m_enums.TryGetValue(name, out already))
            {
                if (!e.Equals(already))
                    throw new Exception($"Non-uniform definitions of enum {name}");

                return;
            }

            m_enums.Add(name, e);
        }

        private void readUnion(BinaryReader reader, string name)
        {
            var e = new UnionDef(reader, name);

            UnionDef already;
            if (m_unions.TryGetValue(name, out already))
            {
                if (e.Equals(already))
                    return;

                if (!e.isFake)
                    throw new Exception($"Non-uniform definitions of union {name}");

                // generate new "fake fake" name
                var n = 0;
                while (m_unions.ContainsKey($"{name}.{n}"))
                    ++n;

                m_unions.Add($"{name}.{n}", e);

                return;
            }

            m_unions.Add(name, e);
        }

        private void readStruct(BinaryReader reader, string name)
        {
            var e = new StructDef(reader, name);

            StructDef already;
            if (m_structs.TryGetValue(name, out already))
            {
                if (e.Equals(already))
                    return;

                if (!e.isFake)
                    Console.WriteLine($"WARNING: Non-uniform definitions of struct {name}");

                // generate new "fake fake" name
                var n = 0;
                while (m_structs.ContainsKey($"{name}.{n}"))
                    ++n;

                m_structs.Add($"{name}.{n}", e);

                return;
            }

            m_structs.Add(name, e);
        }

        private void addTypedef(string name, TypeInfo typeInfo)
        {
            TypeInfo already;
            if (m_typedefs.TryGetValue(name, out already))
            {
                if (!typeInfo.Equals(already))
                    throw new Exception($"Non-uniform definitions of typedef for {name}");

                return;
            }

            m_typedefs.Add(name, typeInfo);
        }

        private void dumpType20(BinaryReader stream, int offset)
        {
            var ti = stream.readTypeInfo(false);
            var name = stream.readPascalString();

            if (ti.classType == ClassType.Enum && ti.typeDef.baseType == BaseType.EnumDef)
            {
                readEnum(stream, name);
                return;
            }
            if (ti.classType == ClassType.FileName)
                return;
            if (ti.classType == ClassType.Struct && ti.typeDef.baseType == BaseType.StructDef)
                readStruct(stream, name);
            else if (ti.classType == ClassType.Union && ti.typeDef.baseType == BaseType.UnionDef)
                readUnion(stream, name);
            else if (ti.classType == ClassType.Typedef)
                addTypedef(name, ti);
            else if (ti.classType == ClassType.External)
                if (ti.typeDef.isFunctionReturnType)
                    m_funcTypes[name] = ti.asCode("").Trim();
                else
                    m_externs.Add($"extern {ti.asCode(name)}; // offset 0x{offset:X}");
            else if (ti.classType == ClassType.Static)
                if (ti.typeDef.isFunctionReturnType)
                    m_funcTypes[name] = ti.asCode("").Trim();
                else
                    m_externs.Add($"static {ti.asCode(name)}; // offset 0x{offset:X}");
            else
                throw new Exception("Gomorrha");
        }

        private void dumpType22(BinaryReader stream, int offset)
        {
            var ti = stream.readTypeInfo(true);
            var name = stream.readPascalString();

            if (ti.classType == ClassType.Enum && ti.typeDef.baseType == BaseType.EnumDef)
                readEnum(stream, name);
            else if (ti.classType == ClassType.Typedef)
                addTypedef(name, ti);
            else if (ti.classType == ClassType.External)
                if (ti.typeDef.isFunctionReturnType)
                    m_funcTypes[name] = ti.asCode("").Trim();
                else
                    m_externs.Add($"extern {ti.asCode(name)}; // offset 0x{offset:X}");
            else if (ti.classType == ClassType.Static)
                if (ti.typeDef.isFunctionReturnType)
                    m_funcTypes[name] = ti.asCode("").Trim();
                else
                    m_externs.Add($"static {ti.asCode(name)}; // offset 0x{offset:X}");
            else
                throw new Exception("Gomorrha");
        }

        public Function findFunction(uint addr)
        {
            return functions.FirstOrDefault(f => f.address == addr);
        }
    }
}
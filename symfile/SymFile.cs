﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using core;
using core.util;
using symfile.memory;
using symfile.type;
using symfile.util;
using Function = symfile.code.Function;

namespace symfile
{
    public class SymFile : IDebugSource
    {
        private readonly Dictionary<string, EnumDef> m_enums = new Dictionary<string, EnumDef>();
        private readonly Dictionary<string, TypeDecoration> m_externs = new Dictionary<string, TypeDecoration>();
        public IList<IFunction> functions { get; } = new List<IFunction>();
        public readonly Dictionary<string, TypeDecoration> funcTypes = new Dictionary<string, TypeDecoration>();
        public SortedDictionary<uint, IList<NamedLocation>> labels { get; } = new SortedDictionary<uint, IList<NamedLocation>>();
        private readonly Dictionary<string, StructLayout> m_structs = new Dictionary<string, StructLayout>();
        private readonly byte m_targetUnit;
        private readonly Dictionary<string, TypeDecoration> m_typedefs = new Dictionary<string, TypeDecoration>();
        private readonly Dictionary<string, UnionLayout> m_unions = new Dictionary<string, UnionLayout>();
        private readonly byte m_version;
        private string m_mxInfo;
        public readonly Dictionary<string, CompoundLayout> currentlyDefining = new Dictionary<string, CompoundLayout>();

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

        public StructLayout findStructDef(string tag)
        {
            if (tag == null)
                return null;
            
            StructLayout result;
            if (!m_structs.TryGetValue(tag, out result))
                return null;

            return result;
        }

        public UnionLayout findUnionDef(string tag)
        {
            if (tag == null)
                return null;
            
            UnionLayout result;
            if (!m_unions.TryGetValue(tag, out result))
                return null;

            return result;
        }

        public IMemoryLayout findTypeDefinition(string tag)
        {
            IMemoryLayout def = findStructDef(tag);
            def = def ?? findUnionDef(tag);
            def = def ?? currentlyDefining.FirstOrDefault(kv => kv.Key == tag).Value;
            return def;
        }
        
        public IMemoryLayout findTypeDefinitionForLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
                return null;
            
            TypeDecoration ti;
            if (!m_externs.TryGetValue(label, out ti))
                return null;
            
            return findTypeDefinition(ti.tag);
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
                writer.WriteLine($"typedef {t.Value.asDeclaration(t.Key)};");

            writer.WriteLine();
            writer.WriteLine($"// {labels.Count} labels");
            foreach (var l in labels)
            foreach (var l2 in l.Value)
                writer.WriteLine(l2);

            writer.WriteLine();
            writer.WriteLine($"// {m_externs.Count} external declarations");
            foreach (var e in m_externs)
                writer.WriteLine(e.Value.asDeclaration(e.Key));

            writer.WriteLine();
            writer.WriteLine($"// {functions.Count} functions");
            foreach (var f in functions)
                f.dump(writer);
        }

        private void dumpEntry(BinaryReader stream)
        {
            var typedValue = new FileEntry(stream);
            if (typedValue.type == 8)
            {
                m_mxInfo = $"${typedValue.value:X} MX-info {stream.ReadByte():X}";
                return;
            }

            if (typedValue.isLabel)
            {
                var lbl = new NamedLocation((uint)typedValue.value, stream.readPascalString());

                if (!labels.ContainsKey(lbl.address))
                    labels.Add(lbl.address, new List<NamedLocation>());

                labels[lbl.address].Add(lbl);
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
                    dumpType20(stream);
                    break;
                case 22:
                    dumpType22(stream);
                    break;
                default:
                    throw new Exception("Sodom");
            }
        }

        private void dumpType12(BinaryReader stream, int offset)
        {
            functions.Add(new Function(stream, (uint) offset, this));
            //writer.WriteLine("{");
            //++writer.Indent;
        }

        private void readEnum(BinaryReader reader, string name)
        {
            var e = new EnumDef(reader, name, this);

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
            var e = new UnionLayout(reader, name, this);

            UnionLayout already;
            if (m_unions.TryGetValue(name, out already))
            {
                if (e.Equals(already))
                    return;

                if (!e.isAnonymous)
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
            var e = new StructLayout(reader, name, this);

            StructLayout already;
            if (m_structs.TryGetValue(name, out already))
            {
                if (e.Equals(already))
                    return;

                if (!e.isAnonymous)
                {
                    Console.WriteLine($"WARNING: Non-uniform definitions of struct {name}");
                    var tw = new IndentedTextWriter(Console.Out);
                    tw.WriteLine("=== Already defined ===");
                    ++tw.indent;
                    already.dump(tw);
                    --tw.indent;
                    tw.WriteLine("=== New definition ===");
                    ++tw.indent;
                    e.dump(tw);
                    --tw.indent;
                }

                // generate new "fake fake" name
                var n = 0;
                while (m_structs.ContainsKey($"{name}.{n}"))
                    ++n;

                m_structs.Add($"{name}.{n}", e);

                return;
            }

            m_structs.Add(name, e);
        }

        private void addTypedef(string name, TypeDecoration typeDecoration)
        {
            TypeDecoration already;
            if (m_typedefs.TryGetValue(name, out already))
            {
                if (!typeDecoration.Equals(already))
                    throw new Exception($"Non-uniform definitions of typedef for {name}");

                return;
            }

            m_typedefs.Add(name, typeDecoration);
        }

        private void dumpType20(BinaryReader stream)
        {
            var ti = stream.readTypeDecoration(false, this);
            var name = stream.readPascalString();

            if (ti.classType == ClassType.Enum && ti.baseType == BaseType.EnumDef)
            {
                readEnum(stream, name);
                return;
            }
            if (ti.classType == ClassType.FileName)
                return;
            if (ti.classType == ClassType.Struct && ti.baseType == BaseType.StructDef)
                readStruct(stream, name);
            else if (ti.classType == ClassType.Union && ti.baseType == BaseType.UnionDef)
                readUnion(stream, name);
            else if (ti.classType == ClassType.Typedef)
                addTypedef(name, ti);
            else if (ti.classType == ClassType.External)
                if (ti.isFunctionReturnType)
                    funcTypes.Add(name, ti);
                else
                    m_externs.Add(name, ti);
            else if (ti.classType == ClassType.Static)
                if (ti.isFunctionReturnType)
                    funcTypes.Add(name, ti);
                else
                    m_externs.Add(name, ti);
            else
                throw new Exception("Gomorrha");
        }

        private void dumpType22(BinaryReader stream)
        {
            var ti = stream.readTypeDecoration(true, this);
            var name = stream.readPascalString();

            if (ti.classType == ClassType.Enum && ti.baseType == BaseType.EnumDef)
                readEnum(stream, name);
            else if (ti.classType == ClassType.Typedef)
                addTypedef(name, ti);
            else if (ti.classType == ClassType.External)
                if (ti.isFunctionReturnType)
                    funcTypes.Add(name, ti);
                else
                    m_externs.Add(name, ti);
            else if (ti.classType == ClassType.Static)
                if (ti.isFunctionReturnType)
                    funcTypes.Add(name, ti);
                else
                    m_externs.Add(name, ti);
            else
                throw new Exception("Gomorrha");
        }

        public IFunction findFunction(uint addr)
        {
            return functions.FirstOrDefault(f => f.address == addr);
        }

        public IFunction findFunction(string name)
        {
            return functions.FirstOrDefault(f => f.name.Equals(name));
        }
        
        public string getSymbolName(uint addr, int rel = 0)
        {
            addr = (uint) (addr + rel);
            
            // first try to find a memory layout which contains this address
            var typedLabel = labels.LastOrDefault(kv => kv.Key <= addr).Value.First();
            var memoryLayout = findTypeDefinitionForLabel(typedLabel.name);
            if (memoryLayout != null)
            {
                try
                {
                    var path = memoryLayout.getAccessPathTo(addr - typedLabel.address);
                    if (path != null)
                        return path;
                }
                catch
                {
                    // ignored
                }
            }

            IList<NamedLocation> lbls;
            if (!labels.TryGetValue(addr, out lbls))
                return $"lbl_{addr:X}";

            return lbls.First().name;
        }
    }
}
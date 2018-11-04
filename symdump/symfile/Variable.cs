using System.IO;
using symdump.symfile.util;

namespace symdump.symfile
{
    public class Variable
    {
        public readonly uint m_Address;
        public readonly string m_Name;
        public readonly TypeInfo m_TypeInfo;

        public Variable(uint address, string name, TypeInfo typeInfo)
        {
            m_Address = address;
            m_Name = name;
            m_TypeInfo = typeInfo;
        }
    }
}
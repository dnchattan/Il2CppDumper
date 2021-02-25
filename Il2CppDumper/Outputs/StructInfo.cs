using System.Collections.Generic;

namespace Il2CppDumper
{
    public class StructInfo
    {
        public string ImageName;
        public string Namespace;
        public string TypeName;
        public bool IsValueType;
        public bool IsGeneric;
        public string Parent;
        public List<StructFieldInfo> Fields = new List<StructFieldInfo>();
        public List<StructFieldInfo> StaticFields = new List<StructFieldInfo>();
        public List<StructStaticMethodInfo> StaticMethods = new List<StructStaticMethodInfo>();
        public List<StructVTableMethodInfo> VTableMethod = new List<StructVTableMethodInfo>();
        public List<StructRGCTXInfo> RGCTXs = new List<StructRGCTXInfo>();
    }

    public class StructFieldInfo
    {
        public string FieldTypeName;
        public string FieldName;
        public bool IsValueType;
        public bool IsCustomType;
        public int Offset;
        public int Indirection;
    }

    public class StructStaticMethodInfo
    {
        public ulong Address;
        public string TypeArgs;
        public string Name;
    }

    public class StructVTableMethodInfo
    {
        public string MethodName;
    }

    public class StructRGCTXInfo
    {
        public Il2CppRGCTXDataType Type;
        public string TypeName;
        public string ClassName;
        public string MethodName;
    }
}

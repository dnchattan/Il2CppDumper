using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Il2CppDumper
{
    public class Il2CppFieldInfo
    {
        public Il2CppTypeInfo Type;
        public string Name;
        public int Offset;
    }

    public class Il2CppStaticMethodInfo
    {
        public ulong Address;
        public string TypeArgs;
        public string Name;
    }

    public class Il2CppTypeDefinitionInfo
    {
        public Il2CppTypeDefinitionInfo(Il2CppTypeInfo cppTypeInfo)
        {
            Type = cppTypeInfo;
        }

        public string ImageName;
        public Il2CppTypeInfo Type;
        public List<Il2CppFieldInfo> Fields = new List<Il2CppFieldInfo>();
        public List<Il2CppFieldInfo> StaticFields = new List<Il2CppFieldInfo>();
    }

    public class Il2CppTypeInfo
    {
        #region fields
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Il2CppType _cppType;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Il2CppTypeEnum _cppTypeEnum;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private int _indirection = 1;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Il2CppTypeInfo _declaringType;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Il2CppTypeInfo _baseType;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private Il2CppTypeInfo _elementType;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string _typeName = null;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string _namespace = null;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private List<string> _templateArgumentNames = new List<string>();

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private List<Il2CppTypeInfo> _typeArguments = new List<Il2CppTypeInfo>();
        #endregion

        public Il2CppTypeInfo(Il2CppType cppType)
        {
            this._cppType = cppType;
            this._cppTypeEnum = cppType.type;
        }

        public bool IsTemplateArg { get { return _cppTypeEnum == Il2CppTypeEnum.IL2CPP_TYPE_VAR || _cppTypeEnum == Il2CppTypeEnum.IL2CPP_TYPE_MVAR; } }

        // Il2CppTypeEnum value
        public Il2CppTypeEnum Type { get { return _cppTypeEnum; } }

        // is array?
        public bool IsArray
        {
            get; set;
        }

        // pointer indirection
        public int Indirection { get { return _indirection; } set { _indirection = value; } }

        // if defined, this type is nested within another type,
        // e.g. class Foo { public class Bar { ... } }
        //      Bar.DeclaringType == Foo
        public Il2CppTypeInfo DeclaringType
        {
            get
            {
                // if (IsArray) return null;
                return _declaringType;
            }
            set
            {
                if (IsArray) throw new InvalidOperationException("Arrays cannot have a declaring type");
                if (!string.IsNullOrEmpty(_namespace))
                    throw new InvalidOperationException("Cannot set declaring type on a type with a namespace set!");
                _declaringType = value;
            }
        }

        // type which this type inherits from
        public Il2CppTypeInfo BaseType
        {
            get
            {
                // if (IsArray) return null;
                return _baseType;
            }
            set
            {
                if (IsArray) throw new InvalidOperationException("Arrays cannot have a base type");
                _baseType = value;
            }
        }

        // element type for array types
        public Il2CppTypeInfo ElementType
        {
            get
            {
                if (!IsArray) return null;
                return _elementType;
            }
            set
            {
                if (!IsArray) throw new InvalidOperationException("IsArray must be set before setting ElementType");
                _elementType = value;
            }
        }

        // Bare type name without template arguments
        public string TypeName
        {
            get
            {
                // if (IsArray) return null;
                return _typeName;
            }
            set
            {
                if (IsArray) throw new InvalidOperationException("Arrays cannot have a type name");
                _typeName = value;
            }
        }

        // Namespace of containing type
        // Not valid if DeclaringType is set (namespace cannot be defined within a type scope)
        public string Namespace
        {
            get
            {
                return _namespace;
            }
            set
            {
                if (IsArray) throw new InvalidOperationException("Arrays cannot have a namespace");
                _namespace = value;
            }
        }

        // template arguments for a generic class
        // e.g. MethodInfo<T>
        //      TemplateArgumentNames = ["T"]
        public List<string> TemplateArgumentNames
        {
            get { return _templateArgumentNames; }
            set
            {
                if (_typeArguments == null || _typeArguments.Count > 0)
                    throw new InvalidOperationException("Cannot set template argument names type on a type with type arguments!");
                _templateArgumentNames = value;
            }
        }

        // type arguments for a generic class implementation
        // e.g. MethodInfo<string>
        //      TypeArguments = [String]
        public List<Il2CppTypeInfo> TypeArguments
        {
            get { return _typeArguments; }
            set
            {
                if (_templateArgumentNames == null || _templateArgumentNames.Count > 0)
                    throw new InvalidOperationException("Cannot set type arguments on a type with template arguments!");
                _typeArguments = value;
            }
        }

        public bool IsPrimitive { get; internal set; }
    }
}
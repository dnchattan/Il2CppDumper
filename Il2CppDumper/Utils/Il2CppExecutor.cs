﻿using System;
using System.Collections.Generic;
using System.Linq;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper
{
    public class UniqueTypeDefNameMap
    {
        private HashSet<string> _uniqueNames = new HashSet<string>();
        private Dictionary<Il2CppType, string> _typeDefToName = new Dictionary<Il2CppType, string>();
        private WeakReference<Il2CppExecutor> _executor;
        public UniqueTypeDefNameMap(WeakReference<Il2CppExecutor> executor)
        {
            _executor = executor;
        }

        public string GetName(Il2CppTypeDefinition typeDef, string originalName)
        {
            Il2CppExecutor executor;
            if (!_executor.TryGetTarget(out executor))
            {
                throw new InvalidOperationException("Cannot be used with disposed executor!");
            }
            var il2CppType = executor.GetIl2CppTypeFromTypeDefinition(typeDef);

            return GetName(executor, typeDef, il2CppType, originalName);
        }

        public string GetName(Il2CppType il2CppType, string originalName)
        {
            if (_typeDefToName.ContainsKey(il2CppType))
            {
                return _typeDefToName[il2CppType].Split('|')[1];
            }

            Il2CppExecutor executor;
            if (!_executor.TryGetTarget(out executor))
            {
                throw new InvalidOperationException("Cannot be used with disposed executor!");
            }
            var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);

            return GetName(executor, typeDef, il2CppType, originalName);
        }

        private string GetName(Il2CppExecutor executor, Il2CppTypeDefinition typeDef, Il2CppType il2CppType, string originalName)
        {
            if (_typeDefToName.ContainsKey(il2CppType))
            {
                return _typeDefToName[il2CppType].Split('|')[1];
            }

            var uniqueTypeNamePair = $"{typeDef.namespaceIndex}|{originalName}";
            int n = 0;
            while (_uniqueNames.Contains(uniqueTypeNamePair))
            {
                uniqueTypeNamePair = $"{typeDef.namespaceIndex}|_{++n}_{originalName}";
            }
            _uniqueNames.Add(uniqueTypeNamePair);
            _typeDefToName[il2CppType] = uniqueTypeNamePair;
            return uniqueTypeNamePair.Split('|')[1];
        }
    }
    public class Il2CppExecutor
    {
        public Metadata metadata;
        public Il2Cpp il2Cpp;
        private static readonly Dictionary<int, string> TypeString = new Dictionary<int, string>
        {
            {1,"void"},
            {2,"bool"},
            {3,"char"},
            {4,"sbyte"},
            {5,"byte"},
            {6,"short"},
            {7,"ushort"},
            {8,"int"},
            {9,"uint"},
            {10,"long"},
            {11,"ulong"},
            {12,"float"},
            {13,"double"},
            {14,"string"},
            {22,"intptr_t"},
            {24,"intptr_t"},
            {25,"uintptr_t"},
            {28,"object"},
        };
        public ulong[] customAttributeGenerators;
        private Dictionary<Il2CppTypeDefinition, int> TypeDefToIndex = new Dictionary<Il2CppTypeDefinition, int>();
        private Dictionary<Il2CppType, long> TypeToIndex = new Dictionary<Il2CppType, long>();
        private UniqueTypeDefNameMap TypeDefToName;

        public Il2CppExecutor(Metadata metadata, Il2Cpp il2Cpp)
        {
            this.metadata = metadata;
            this.il2Cpp = il2Cpp;
            this.TypeDefToName = new UniqueTypeDefNameMap(new WeakReference<Il2CppExecutor>(this));

            if (il2Cpp.Version >= 27)
            {
                customAttributeGenerators = new ulong[metadata.imageDefs.Sum(x => x.customAttributeCount)];
                foreach (var imageDef in metadata.imageDefs)
                {
                    var imageDefName = metadata.GetStringFromIndex(imageDef.nameIndex);
                    var codeGenModule = il2Cpp.codeGenModules[imageDefName];
                    var pointers = il2Cpp.ReadClassArray<ulong>(il2Cpp.MapVATR(codeGenModule.customAttributeCacheGenerator), imageDef.customAttributeCount);
                    pointers.CopyTo(customAttributeGenerators, imageDef.customAttributeStart);
                }
            }
            else
            {
                customAttributeGenerators = il2Cpp.customAttributeGenerators;
            }

            for (int index = 0; index < metadata.typeDefs.Length; ++index)
            {
                TypeDefToIndex[metadata.typeDefs[index]] = index;
            }
            for (long index = 0; index < il2Cpp.types.Length; ++index)
            {
                TypeToIndex[il2Cpp.types[index]] = index;
            }
        }

        /**/
        public Il2CppTypeInfo GetTypeInfo(Il2CppType il2CppType, bool is_nested = false)
        {
            var result = GetTypeInfoInternal(il2CppType, is_nested);
            return result;
        }

        public Il2CppTypeInfo GetTypeInfo(Il2CppTypeDefinition typeDef, Il2CppGenericClass genericClass = null, bool is_nested = false)
        {
            var il2CppType = GetIl2CppTypeFromTypeDefinition(typeDef);
            var result = GetTypeInfoInternal(typeDef, il2CppType, genericClass, is_nested);
            return result;
        }


        public Il2CppTypeInfo GetTypeInfoInternal(Il2CppTypeDefinition typeDef, Il2CppType il2CppType, Il2CppGenericClass genericClass = null, bool is_nested = false)
        {
            var typeInfo = new Il2CppTypeInfo(il2CppType);
            typeInfo.TypeIndex = TypeToIndex[il2CppType];

            if (typeDef.parentIndex != -1 && !is_nested && !typeDef.IsValueType && !typeDef.IsEnum)
            {
                var baseType = GetTypeInfo(il2Cpp.types[typeDef.parentIndex], is_nested);
                if (!baseType.IsPrimitive && baseType.TypeIndex != typeInfo.TypeIndex)
                {
                    typeInfo.BaseType = baseType;
                }
            }
            if (typeDef.declaringTypeIndex != -1)
            {
                typeInfo.DeclaringType = GetTypeInfo(il2Cpp.types[typeDef.declaringTypeIndex], true);
            }
            else
            {
                typeInfo.Namespace = metadata.GetStringFromIndex(typeDef.namespaceIndex);
            }

            // trim MyGenericType`1 to just MyGenericType
            var typeName = metadata.GetStringFromIndex(typeDef.nameIndex);
            var index = typeName.IndexOf("`");
            if (index != -1)
            {
                typeName = typeName.Substring(0, index);
            }
            typeInfo.TypeName = TypeDefToName.GetName(typeDef, typeName);

            // only recurse if we're using templates!
            if (is_nested && genericClass == null)
            {
                return typeInfo;
            }

            if (typeDef.genericContainerIndex >= 0)
            {
                var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                var paramNames = GetGenericContainerParamNames(genericContainer);
                typeInfo.TemplateArgumentNames.AddRange(paramNames);
            }
            if (genericClass != null)
            {
                var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(genericClass.context.class_inst);
                var args = GetGenericInstParamList(genericInst);
                foreach (var arg in args)
                {
                    // generic argument
                    if (arg.type == Il2CppTypeEnum.IL2CPP_TYPE_VAR || arg.type == Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
                    {
                        // Are these used??
                        var param = GetGenericParameteFromIl2CppType(arg);
                        var tName = metadata.GetStringFromIndex(param.nameIndex);
                        typeInfo.TemplateArgumentNames.Add(tName);
                    }
                    else
                    {
                        var tArg = GetTypeInfoInternal(arg);
                        typeInfo.TypeArguments.Add(tArg);
                    }
                }
            }
            return typeInfo;
        }


        public Il2CppTypeInfo GetTypeInfoInternal(Il2CppType il2CppType, bool is_nested = false)
        {
            var typeInfo = new Il2CppTypeInfo(il2CppType);
            typeInfo.TypeIndex = TypeToIndex[il2CppType];
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var elementType = il2Cpp.GetIl2CppType(arrayType.etype);
                        var elementTypeInfo = GetTypeInfo(elementType, is_nested);

                        // hack: too lazy to special case consumption of array types
                        elementTypeInfo.IsArray = true;
                        return elementTypeInfo;
                        // typeInfo.ElementType = GetTypeInfo(elementType);
                        // return typeInfo;
                        // return $"{GetTypeName(elementType, addNamespace, false)}[{new string(',', arrayType.rank - 1)}]";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var elementType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        var elementTypeInfo = GetTypeInfoInternal(elementType, is_nested);
                        elementTypeInfo.IsArray = true;
                        return elementTypeInfo;
                        // typeInfo.ElementType = GetTypeInfo(elementType);
                        // return typeInfo;
                        // return $"{GetTypeName(elementType, addNamespace, false)}[]";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        var ptrType = GetTypeInfoInternal(oriType, is_nested);
                        ptrType.TypeIndex = typeInfo.TypeIndex;
                        ++ptrType.Indirection;
                        return ptrType;
                        // return $"{GetTypeName(oriType, addNamespace, false)}*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        var param = GetGenericParameteFromIl2CppType(il2CppType);
                        // just a string name, no unique constraints
                        typeInfo.TypeName = metadata.GetStringFromIndex(param.nameIndex);
                        return typeInfo;
                        //return metadata.GetStringFromIndex(param.nameIndex);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        Il2CppTypeDefinition typeDef;
                        Il2CppGenericClass genericClass = null;
                        if (il2CppType.type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                        {
                            genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                            typeDef = GetGenericClassTypeDefinition(genericClass);
                        }
                        else
                        {
                            typeDef = GetTypeDefinitionFromIl2CppType(il2CppType);
                            if (il2CppType.type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE && typeDef.IsEnum)
                            {
                                return GetTypeInfo(il2Cpp.types[typeDef.elementTypeIndex]);
                            }

                        }
                        var concreteType = GetTypeInfo(typeDef, genericClass, true);
                        ++concreteType.Indirection;
                        return concreteType;
                    }
                default:
                    {
                        typeInfo.IsPrimitive = true;
                        typeInfo.TypeName = TypeString[(int)il2CppType.type];
                        return typeInfo;
                    }
            }
        }

        public Il2CppTypeDefinitionInfo GetTypeDefInfo(Il2CppTypeDefinition typeDef, Il2CppGenericClass genericClass = null, bool is_nested = false)
        {
            var typeInfo = GetTypeInfo(typeDef, genericClass);
            var typeDefInfo = new Il2CppTypeDefinitionInfo(typeInfo);
            AddFields(typeDef, typeDefInfo);

            return typeDefInfo;
        }

        private void AddFields(Il2CppTypeDefinition typeDef, Il2CppTypeDefinitionInfo typeDefInfo)
        {
            var typeIndex = TypeDefToIndex[typeDef];
            if (typeDef.field_count > 0)
            {
                var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                {
                    var fieldDef = metadata.fieldDefs[i];
                    var fieldType = il2Cpp.types[fieldDef.typeIndex];
                    if ((fieldType.attrs & FIELD_ATTRIBUTE_LITERAL) != 0)
                    {
                        continue;
                    }
                    var structFieldInfo = new Il2CppFieldInfo();
                    structFieldInfo.Type = GetTypeInfoInternal(fieldType);
                    var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                    structFieldInfo.Name = fieldName;
                    bool isStatic = (fieldType.attrs & FIELD_ATTRIBUTE_STATIC) != 0;
                    if (typeIndex < 0) throw new Exception("type is not in typeDef list!");
                    structFieldInfo.Offset = il2Cpp.GetFieldOffsetFromIndex(typeIndex, i - typeDef.fieldStart, i, typeDef.IsValueType, isStatic);
                    if (isStatic)
                    {
                        typeDefInfo.StaticFields.Add(structFieldInfo);
                    }
                    else
                    {
                        typeDefInfo.Fields.Add(structFieldInfo);
                    }
                }
            }
        }

        /**/

        public string GetTypeName(Il2CppType il2CppType, bool addNamespace, bool is_nested)
        {
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var elementType = il2Cpp.GetIl2CppType(arrayType.etype);
                        return $"{GetTypeName(elementType, addNamespace, false)}[{new string(',', arrayType.rank - 1)}]";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var elementType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return $"{GetTypeName(elementType, addNamespace, false)}[]";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return $"{GetTypeName(oriType, addNamespace, false)}*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        var param = GetGenericParameteFromIl2CppType(il2CppType);
                        return metadata.GetStringFromIndex(param.nameIndex);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        string str = string.Empty;
                        Il2CppTypeDefinition typeDef;
                        Il2CppGenericClass genericClass = null;
                        if (il2CppType.type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
                        {
                            genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                            typeDef = GetGenericClassTypeDefinition(genericClass);
                        }
                        else
                        {
                            typeDef = GetTypeDefinitionFromIl2CppType(il2CppType);
                        }
                        if (typeDef.declaringTypeIndex != -1)
                        {
                            str += GetTypeName(il2Cpp.types[typeDef.declaringTypeIndex], addNamespace, true);
                            str += '.';
                        }
                        else if (addNamespace)
                        {
                            var @namespace = metadata.GetStringFromIndex(typeDef.namespaceIndex);
                            if (@namespace != "")
                            {
                                str += @namespace + ".";
                            }
                        }

                        var typeName = metadata.GetStringFromIndex(typeDef.nameIndex);
                        var index = typeName.IndexOf("`");
                        if (index != -1)
                        {
                            str += typeName.Substring(0, index);
                        }
                        else
                        {
                            str += typeName;
                        }

                        if (is_nested)
                            return str;

                        if (genericClass != null)
                        {
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(genericClass.context.class_inst);
                            str += GetGenericInstParams(genericInst);
                        }
                        else if (typeDef.genericContainerIndex >= 0)
                        {
                            var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                            str += GetGenericContainerParams(genericContainer);
                        }

                        return str;
                    }
                default:
                    return TypeString[(int)il2CppType.type];
            }
        }

        public string GetTypeDefName(Il2CppTypeDefinition typeDef, bool addNamespace, bool genericParameter)
        {
            var prefix = string.Empty;
            if (typeDef.declaringTypeIndex != -1)
            {
                prefix = GetTypeName(il2Cpp.types[typeDef.declaringTypeIndex], addNamespace, true) + ".";
            }
            else if (addNamespace)
            {
                var @namespace = metadata.GetStringFromIndex(typeDef.namespaceIndex);
                if (@namespace != "")
                {
                    prefix = @namespace + ".";
                }
            }
            var typeName = metadata.GetStringFromIndex(typeDef.nameIndex);
            if (typeDef.genericContainerIndex >= 0)
            {
                var index = typeName.IndexOf("`");
                if (index != -1)
                {
                    typeName = typeName.Substring(0, index);
                }
                if (genericParameter)
                {
                    var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                    typeName += GetGenericContainerParams(genericContainer);
                }
            }
            return prefix + typeName;
        }

        public string GetGenericInstParams(Il2CppGenericInst genericInst)
        {
            var genericParameterNames = new List<string>();
            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
            for (int i = 0; i < genericInst.type_argc; i++)
            {
                var il2CppType = il2Cpp.GetIl2CppType(pointers[i]);
                genericParameterNames.Add(GetTypeName(il2CppType, false, false));
            }
            return $"<{string.Join(", ", genericParameterNames)}>";
        }

        public Il2CppType[] GetGenericInstParamList(Il2CppGenericInst genericInst)
        {
            var genericParameterTypes = new Il2CppType[genericInst.type_argc];
            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
            for (int i = 0; i < genericInst.type_argc; i++)
            {
                var il2CppType = il2Cpp.GetIl2CppType(pointers[i]);
                genericParameterTypes[i] = il2CppType;
            }
            return genericParameterTypes;
        }

        public string[] GetGenericContainerParamNames(Il2CppGenericContainer genericContainer)
        {
            var genericParameterNames = new string[genericContainer.type_argc];
            for (int i = 0; i < genericContainer.type_argc; i++)
            {
                var genericParameterIndex = genericContainer.genericParameterStart + i;
                var genericParameter = metadata.genericParameters[genericParameterIndex];
                genericParameterNames[i] = metadata.GetStringFromIndex(genericParameter.nameIndex);
            }
            return genericParameterNames;
        }

        public string GetGenericContainerParams(Il2CppGenericContainer genericContainer)
        {
            var genericParameterNames = new List<string>();
            for (int i = 0; i < genericContainer.type_argc; i++)
            {
                var genericParameterIndex = genericContainer.genericParameterStart + i;
                var genericParameter = metadata.genericParameters[genericParameterIndex];
                genericParameterNames.Add(metadata.GetStringFromIndex(genericParameter.nameIndex));
            }
            return $"<{string.Join(", ", genericParameterNames)}>";
        }

        public (string, string, string) GetMethodSpecName(Il2CppMethodSpec methodSpec, bool addNamespace = false)
        {
            var methodDef = metadata.methodDefs[methodSpec.methodDefinitionIndex];
            var typeDef = metadata.typeDefs[methodDef.declaringType];
            var typeName = GetTypeDefName(typeDef, addNamespace, false);
            string typeParameters = null;
            if (methodSpec.classIndexIndex != -1)
            {
                var classInst = il2Cpp.genericInsts[methodSpec.classIndexIndex];
                typeParameters = GetGenericInstParams(classInst);
            }
            var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
            if (methodSpec.methodIndexIndex != -1)
            {
                var methodInst = il2Cpp.genericInsts[methodSpec.methodIndexIndex];
                methodName += GetGenericInstParams(methodInst);
            }
            return (typeName, methodName, typeParameters);
        }

        public Il2CppGenericContext GetMethodSpecGenericContext(Il2CppMethodSpec methodSpec)
        {
            var classInstPointer = 0ul;
            var methodInstPointer = 0ul;
            if (methodSpec.classIndexIndex != -1)
            {
                classInstPointer = il2Cpp.genericInstPointers[methodSpec.classIndexIndex];
            }
            if (methodSpec.methodIndexIndex != -1)
            {
                methodInstPointer = il2Cpp.genericInstPointers[methodSpec.methodIndexIndex];
            }
            return new Il2CppGenericContext { class_inst = classInstPointer, method_inst = methodInstPointer };
        }

        public Il2CppRGCTXDefinition[] GetRGCTXDefinition(string imageName, Il2CppTypeDefinition typeDef)
        {
            Il2CppRGCTXDefinition[] collection = null;
            if (il2Cpp.Version >= 24.2f)
            {
                il2Cpp.rgctxsDictionary[imageName].TryGetValue(typeDef.token, out collection);
            }
            else
            {
                if (typeDef.rgctxCount > 0)
                {
                    collection = new Il2CppRGCTXDefinition[typeDef.rgctxCount];
                    Array.Copy(metadata.rgctxEntries, typeDef.rgctxStartIndex, collection, 0, typeDef.rgctxCount);
                }
            }
            return collection;
        }

        public Il2CppRGCTXDefinition[] GetRGCTXDefinition(string imageName, Il2CppMethodDefinition methodDef)
        {
            Il2CppRGCTXDefinition[] collection = null;
            if (il2Cpp.Version >= 24.2f)
            {
                il2Cpp.rgctxsDictionary[imageName].TryGetValue(methodDef.token, out collection);
            }
            else
            {
                if (methodDef.rgctxCount > 0)
                {
                    collection = new Il2CppRGCTXDefinition[methodDef.rgctxCount];
                    Array.Copy(metadata.rgctxEntries, methodDef.rgctxStartIndex, collection, 0, methodDef.rgctxCount);
                }
            }
            return collection;
        }

        public Il2CppTypeDefinition GetGenericClassTypeDefinition(Il2CppGenericClass genericClass)
        {
            if (il2Cpp.Version >= 27)
            {
                var il2CppType = il2Cpp.GetIl2CppType(genericClass.type);
                return GetTypeDefinitionFromIl2CppType(il2CppType);
            }
            if (genericClass.typeDefinitionIndex == 4294967295 || genericClass.typeDefinitionIndex == -1)
            {
                return null;
            }
            return metadata.typeDefs[genericClass.typeDefinitionIndex];
        }

        public Il2CppTypeDefinition GetTypeDefinitionFromIl2CppType(Il2CppType il2CppType)
        {
            if (il2Cpp.Version >= 27 && il2Cpp is ElfBase elf && elf.IsDumped)
            {
                var offset = il2CppType.data.typeHandle - metadata.Address - metadata.header.typeDefinitionsOffset;
                var index = offset / (ulong)metadata.SizeOf(typeof(Il2CppTypeDefinition));
                return metadata.typeDefs[index];
            }
            else
            {
                return metadata.typeDefs[il2CppType.data.klassIndex];
            }
        }

        public Il2CppType GetIl2CppTypeFromTypeDefinition(Il2CppTypeDefinition typeDef)
        {
            var typeDefIndex = TypeDefToIndex[typeDef];
            if (typeDefIndex == -1)
            {
                throw new KeyNotFoundException("typedef not found");
            }
            return il2Cpp.types[typeDef.byrefTypeIndex];
        }

        public Il2CppGenericParameter GetGenericParameteFromIl2CppType(Il2CppType il2CppType)
        {
            if (il2Cpp.Version >= 27 && il2Cpp is ElfBase elf && elf.IsDumped)
            {
                var offset = il2CppType.data.genericParameterHandle - metadata.Address - metadata.header.genericParametersOffset;
                var index = offset / (ulong)metadata.SizeOf(typeof(Il2CppGenericParameter));
                return metadata.genericParameters[index];
            }
            else
            {
                return metadata.genericParameters[il2CppType.data.genericParameterIndex];
            }
        }
    }
}

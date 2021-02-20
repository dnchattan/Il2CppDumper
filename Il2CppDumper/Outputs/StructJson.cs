using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static Il2CppDumper.Il2CppConstants;

namespace Il2CppDumper
{
    public class UniqueName
    {
        private HashSet<string> uniqueNamesHash = new HashSet<string>(StringComparer.Ordinal);

        public string Get(string name)
        {
            string uniqueName = name;
            int i = 1;
            while (!uniqueNamesHash.Add(uniqueName))
            {
                uniqueName = $"{name}_{i++}";
            }
            return uniqueName;
        }
    }

    public class TypeDefinitionMetadata
    {
        private static UniqueName UniqueNames = new UniqueName();

        public TypeDefinitionMetadata(int imageIndex, int typeInfoIndex, string imageName, string name)
        {
            ImageIndex = imageIndex;
            TypeInfoIndex = typeInfoIndex;
            ImageName = imageName;
            UniqueName = UniqueNames.Get(name);
        }
        public int ImageIndex;
        public int TypeInfoIndex;
        public string ImageName;
        public string UniqueName;
    }

    public class Il2CppIndex
    {
        private Il2CppExecutor executor;
        private Metadata metadata;
        private Il2Cpp il2Cpp;

        private readonly Dictionary<Il2CppTypeDefinition, TypeDefinitionMetadata> TypeMetadata = new Dictionary<Il2CppTypeDefinition, TypeDefinitionMetadata>();
        private readonly Dictionary<string, Il2CppType> lookupGenericType = new Dictionary<string, Il2CppType>();
        private readonly Dictionary<ulong, string> lookupGenericClassName = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> genericClassList = new HashSet<ulong>();
        private readonly Dictionary<string, List<StructStaticMethodInfo>> TypeNameToStaticMethods = new Dictionary<string, List<StructStaticMethodInfo>>();

        public List<StructInfo> StructInfoList = new List<StructInfo>();
        public List<StructStaticMethodInfo> StaticMethods = new List<StructStaticMethodInfo>();

        public Il2CppIndex(Il2CppExecutor il2CppExecutor)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;
            BuildIndex();
        }

        private void BuildIndex()
        {
            IndexTypeMetadata();
            IndexGenerics();
            IndexMetadataUsage();
            BuildStructInfos();
            return;
        }

        private void BuildStructInfos()
        {
            foreach (var (typeDef, metadata) in TypeMetadata)
            {
                AddStruct(typeDef, metadata);
            }

            foreach (ulong pointer in genericClassList)
            {
                AddGenericClassStruct(pointer);
            }
        }

        private void IndexMetadataUsage()
        {
            if (il2Cpp.Version <= 16 || il2Cpp.Version >= 27)
            {
                return;
            }
            foreach (var (metadataUsageIndex, methodDefIndex) in metadata.metadataUsageDic[3]) //kIl2CppMetadataUsageMethodDef
            {
                var methodDef = metadata.methodDefs[methodDefIndex];
                var typeDef = metadata.typeDefs[methodDef.declaringType];
                var staticMethod = new StructStaticMethodInfo();
                var typeName = executor.GetTypeDefName(typeDef, true, true);
                staticMethod.Address = il2Cpp.GetRVA(il2Cpp.metadataUsages[metadataUsageIndex]);
                staticMethod.Name = metadata.GetStringFromIndex(methodDef.nameIndex);
                StaticMethods.Add(staticMethod);

                if (!TypeNameToStaticMethods.ContainsKey(typeName))
                {
                    TypeNameToStaticMethods.Add(typeName, new List<StructStaticMethodInfo>());
                }
                TypeNameToStaticMethods[typeName].Add(staticMethod);
            }
            foreach (var (metadataUsageIndex, methodSpecIndex) in metadata.metadataUsageDic[6]) //kIl2CppMetadataUsageMethodRef
            {
                var methodSpec = il2Cpp.methodSpecs[methodSpecIndex];
                var methodDef = metadata.methodDefs[methodSpec.methodDefinitionIndex];
                var typeDef = metadata.typeDefs[methodDef.declaringType];
                var staticMethod = new StructStaticMethodInfo();
                staticMethod.Address = il2Cpp.GetRVA(il2Cpp.metadataUsages[metadataUsageIndex]);
                (var typeName, var methodSpecMethodName) = executor.GetMethodSpecName(methodSpec, true);
                staticMethod.Name = methodSpecMethodName;

                if (!TypeNameToStaticMethods.ContainsKey(typeName))
                {
                    TypeNameToStaticMethods.Add(typeName, new List<StructStaticMethodInfo>());
                }
                TypeNameToStaticMethods[typeName].Add(staticMethod);
            }
        }

        private void AddStruct(Il2CppTypeDefinition typeDef, TypeDefinitionMetadata metadata)
        {
            var structInfo = new StructInfo();
            StructInfoList.Add(structInfo);
            structInfo.TypeName = metadata.UniqueName;
            structInfo.IsValueType = typeDef.IsValueType;
            AddCommonStructProperties(typeDef, structInfo);
            AddRGCTX(structInfo, typeDef, metadata);
        }

        private void AddGenericClassStruct(ulong pointer)
        {
            if (!lookupGenericClassName.ContainsKey(pointer))
            {
                return;
            }
            var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(pointer);
            var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
            var structInfo = new StructInfo();
            StructInfoList.Add(structInfo);
            structInfo.TypeName = lookupGenericClassName[pointer];
            structInfo.IsValueType = typeDef.IsValueType;
            AddCommonStructProperties(typeDef, structInfo);
        }

        private void AddCommonStructProperties(Il2CppTypeDefinition typeDef, StructInfo structInfo)
        {
            AddParents(typeDef, structInfo);
            AddFields(typeDef, structInfo, null);
            AddStaticMethods(structInfo);
            AddVTableMethod(structInfo, typeDef);
        }

        private void IndexTypeMetadata()
        {
            // build type -> image reverse lookup
            for (int imageIndex = 0; imageIndex < metadata.imageDefs.Length; imageIndex++)
            {
                Il2CppImageDefinition imageDef = metadata.imageDefs[imageIndex];
                string imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                long typeEnd = imageDef.typeStart + imageDef.typeCount;
                foreach (int typeIndex in imageDef.TypeRange)
                {
                    Il2CppTypeDefinition typeDef = metadata.typeDefs[typeIndex];
                    string typeName = executor.GetTypeDefName(typeDef, true, true);
                    TypeMetadata.Add(typeDef, new TypeDefinitionMetadata(imageIndex, typeIndex, imageName, typeName));
                }
            }
        }

        // requires TypeMetadata
        private void IndexGenerics()
        {
            // build type -> generic instance lookup
            foreach (var il2CppType in il2Cpp.types.Where(x => x.type == Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST))
            {
                var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                var baseTypeDef = executor.GetGenericClassTypeDefinition(genericClass);
                if (baseTypeDef == null)
                {
                    continue;
                }
                TypeDefinitionMetadata baseTypeMetadata = TypeMetadata[baseTypeDef];
                var typeBaseName = TypeMetadata[baseTypeDef].UniqueName;
                var typeToReplaceName = executor.GetTypeDefName(baseTypeDef, true, true);
                var typeReplaceName = executor.GetTypeName(il2CppType, true, false);
                var typeStructName = typeBaseName.Replace(typeToReplaceName, typeReplaceName);
                lookupGenericType[typeStructName] = il2CppType;
                lookupGenericClassName[il2CppType.data.generic_class] = typeStructName;
            }
        }

        private void AddParents(Il2CppTypeDefinition typeDef, StructInfo structInfo)
        {
            if (!typeDef.IsValueType && !typeDef.IsEnum)
            {
                if (typeDef.parentIndex >= 0)
                {
                    var parent = il2Cpp.types[typeDef.parentIndex];
                    if (parent.type != Il2CppTypeEnum.IL2CPP_TYPE_OBJECT)
                    {
                        structInfo.Parent = GetIl2CppStructName(parent);
                    }
                }
            }
        }

        private void AddFields(Il2CppTypeDefinition typeDef, StructInfo structInfo, Il2CppGenericContext context)
        {
            if (typeDef.field_count > 0)
            {
                var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                var cache = new HashSet<string>(StringComparer.Ordinal);
                for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                {
                    var fieldDef = metadata.fieldDefs[i];
                    var fieldType = il2Cpp.types[fieldDef.typeIndex];
                    if ((fieldType.attrs & FIELD_ATTRIBUTE_LITERAL) != 0)
                    {
                        continue;
                    }
                    var structFieldInfo = new StructFieldInfo();
                    structFieldInfo.FieldTypeName = ParseType(fieldType, context);
                    var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                    if (!cache.Add(fieldName))
                    {
                        fieldName = $"_{i - typeDef.fieldStart}_{fieldName}";
                    }
                    structFieldInfo.FieldName = fieldName;
                    structFieldInfo.IsValueType = IsValueType(fieldType, context);
                    structFieldInfo.IsCustomType = IsCustomType(fieldType, context);
                    bool isStatic = (fieldType.attrs & FIELD_ATTRIBUTE_STATIC) != 0;
                    structFieldInfo.Offset = il2Cpp.GetFieldOffsetFromIndex(TypeMetadata[typeDef].TypeInfoIndex, i - typeDef.fieldStart, i, typeDef.IsValueType, isStatic);
                    if (isStatic)
                    {
                        structInfo.StaticFields.Add(structFieldInfo);
                    }
                    else
                    {
                        structInfo.Fields.Add(structFieldInfo);
                    }
                }
            }
        }

        private void AddStaticMethods(StructInfo structInfo)
        {
            if (!TypeNameToStaticMethods.ContainsKey(structInfo.TypeName))
            {
                return;
            }
            structInfo.StaticMethods.AddRange(TypeNameToStaticMethods[structInfo.TypeName]);
            // structInfo.StaticMethods.AddRange(metadata.StaticMethods);
        }

        private void AddVTableMethod(StructInfo structInfo, Il2CppTypeDefinition typeDef)
        {
            var dic = new SortedDictionary<int, Il2CppMethodDefinition>();
            for (int i = 0; i < typeDef.vtable_count; i++)
            {
                var vTableIndex = typeDef.vtableStart + i;
                var encodedMethodIndex = metadata.vtableMethods[vTableIndex];
                var usage = metadata.GetEncodedIndexType(encodedMethodIndex);
                var index = metadata.GetDecodedMethodIndex(encodedMethodIndex);
                Il2CppMethodDefinition methodDef;
                if (usage == 6) //kIl2CppMetadataUsageMethodRef
                {
                    var methodSpec = il2Cpp.methodSpecs[index];
                    methodDef = metadata.methodDefs[methodSpec.methodDefinitionIndex];
                }
                else
                {
                    methodDef = metadata.methodDefs[index];
                }
                dic[methodDef.slot] = methodDef;
            }
            foreach (var i in dic)
            {
                var methodInfo = new StructVTableMethodInfo();
                structInfo.VTableMethod.Add(methodInfo);
                var methodDef = i.Value;
                methodInfo.MethodName = $"_{methodDef.slot}_{metadata.GetStringFromIndex(methodDef.nameIndex)}";
            }
        }

        private void AddRGCTX(StructInfo structInfo, Il2CppTypeDefinition typeDef, TypeDefinitionMetadata metadata)
        {
            var imageName = metadata.ImageName;
            var collection = executor.GetRGCTXDefinition(imageName, typeDef);
            if (collection != null)
            {
                foreach (var definitionData in collection)
                {
                    var structRGCTXInfo = new StructRGCTXInfo();
                    structInfo.RGCTXs.Add(structRGCTXInfo);
                    structRGCTXInfo.Type = definitionData.type;
                    switch (definitionData.type)
                    {
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_TYPE:
                            {
                                var il2CppType = il2Cpp.types[definitionData.data.typeIndex];
                                structRGCTXInfo.TypeName = executor.GetTypeName(il2CppType, true, false);
                                break;
                            }
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_CLASS:
                            {
                                var il2CppType = il2Cpp.types[definitionData.data.typeIndex];
                                structRGCTXInfo.ClassName = executor.GetTypeName(il2CppType, true, false);
                                break;
                            }
                        case Il2CppRGCTXDataType.IL2CPP_RGCTX_DATA_METHOD:
                            {
                                var methodSpec = il2Cpp.methodSpecs[definitionData.data.methodIndex];
                                (var methodSpecTypeName, var methodSpecMethodName) = executor.GetMethodSpecName(methodSpec, true);
                                structRGCTXInfo.MethodName = methodSpecTypeName + "." + methodSpecMethodName;
                                break;
                            }
                    }
                }
            }
        }

        private bool IsValueType(Il2CppType il2CppType, Il2CppGenericContext context)
        {
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        return !typeDef.IsEnum;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                        var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
                        return typeDef.IsValueType && !typeDef.IsEnum;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.class_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return IsValueType(type, null);
                        }
                        return false;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.method_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return IsValueType(type, null);
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }

        private bool IsCustomType(Il2CppType il2CppType, Il2CppGenericContext context)
        {
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return IsCustomType(oriType, context);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        return true;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        if (typeDef.IsEnum)
                        {
                            return IsCustomType(il2Cpp.types[typeDef.elementTypeIndex], context);
                        }
                        return true;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                        var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
                        if (typeDef.IsEnum)
                        {
                            return IsCustomType(il2Cpp.types[typeDef.elementTypeIndex], context);
                        }
                        return true;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.class_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return IsCustomType(type, null);
                        }
                        return false;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.method_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return IsCustomType(type, null);
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }

        private string GetIl2CppStructName(Il2CppType il2CppType, Il2CppGenericContext context = null)
        {
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        return TypeMetadata[typeDef].UniqueName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return GetIl2CppStructName(oriType);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var elementType = il2Cpp.GetIl2CppType(arrayType.etype);
                        var elementStructName = GetIl2CppStructName(elementType, context);
                        var typeStructName = elementStructName + "_array";
                        // TODO
                        // if (structNameHashSet.Add(typeStructName))
                        // {
                        //     ParseArrayClassStruct(elementType, context);
                        // }
                        return typeStructName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var elementType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        var elementStructName = GetIl2CppStructName(elementType, context);
                        var typeStructName = elementStructName + "_array";
                        // TODO
                        // if (structNameHashSet.Add(typeStructName))
                        // {
                        //     ParseArrayClassStruct(elementType, context);
                        // }
                        genericClassList.Add(il2CppType.data.generic_class);
                        return typeStructName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var typeStructName = lookupGenericClassName[il2CppType.data.generic_class];
                        // TODO
                        // if (structNameHashSet.Add(typeStructName))
                        // {
                        //     genericClassList.Add(il2CppType.data.generic_class);
                        // }
                        genericClassList.Add(il2CppType.data.generic_class);
                        return typeStructName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.class_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return GetIl2CppStructName(type);
                        }
                        return "System_Object";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.method_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return GetIl2CppStructName(type);
                        }
                        return "System_Object";
                    }
                default:
                    throw new NotSupportedException();
            }
        }

        private string ParseType(Il2CppType il2CppType, Il2CppGenericContext context = null)
        {
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return "void";
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return "bool";
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return "uint16_t"; //Il2CppChar
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return "int8_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return "uint8_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return "int16_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return "uint16_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return "int32_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return "uint32_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return "int64_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return "uint64_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return "float";
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return "double";
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return "System_String_o*";
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return ParseType(oriType) + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        if (typeDef.IsEnum)
                        {
                            return ParseType(il2Cpp.types[typeDef.elementTypeIndex]);
                        }
                        return TypeMetadata[typeDef].UniqueName;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                    {
                        var typeDef = executor.GetTypeDefinitionFromIl2CppType(il2CppType);
                        return TypeMetadata[typeDef].UniqueName + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.class_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return ParseType(type);
                        }
                        return "Il2CppObject*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var elementType = il2Cpp.GetIl2CppType(arrayType.etype);
                        var elementStructName = GetIl2CppStructName(elementType, context);
                        var typeStructName = elementStructName + "_array";
                        // TODO
                        // if (structNameHashSet.Add(typeStructName))
                        // {
                        //     ParseArrayClassStruct(elementType, context);
                        // }
                        return typeStructName + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                        var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
                        var typeStructName = lookupGenericClassName[il2CppType.data.generic_class];
                        // TODO
                        // if (structNameHashSet.Add(typeStructName))
                        // {
                        // }
                        genericClassList.Add(il2CppType.data.generic_class);
                        if (typeDef.IsValueType)
                        {
                            if (typeDef.IsEnum)
                            {
                                return ParseType(il2Cpp.types[typeDef.elementTypeIndex]);
                            }
                            return typeStructName;
                        }
                        return typeStructName + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return "Il2CppObject*";
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return "intptr_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return "uintptr_t";
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return "Il2CppObject*";
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var elementType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        var elementStructName = GetIl2CppStructName(elementType, context);
                        var typeStructName = elementStructName + "_array";
                        // TODO
                        // if (structNameHashSet.Add(typeStructName))
                        // {
                        //     ParseArrayClassStruct(elementType, context);
                        // }
                        return typeStructName + "*";
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (context != null)
                        {
                            var genericParameter = executor.GetGenericParameteFromIl2CppType(il2CppType);
                            var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(context.method_inst);
                            var pointers = il2Cpp.MapVATR<ulong>(genericInst.type_argv, genericInst.type_argc);
                            var pointer = pointers[genericParameter.num];
                            var type = il2Cpp.GetIl2CppType(pointer);
                            return ParseType(type);
                        }
                        return "Il2CppObject*";
                    }
                default:
                    throw new NotSupportedException();
            }
        }
    }

    public class StructJsonGenerator
    {
        private Il2CppExecutor executor;
        private Metadata metadata;
        private Il2Cpp il2Cpp;
        private Il2CppIndex index;

        private Dictionary<Il2CppTypeDefinition, int> lookupTypeToImageIndex = new Dictionary<Il2CppTypeDefinition, int>();

        public StructJsonGenerator(Il2CppExecutor il2CppExecutor)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;
            index = new Il2CppIndex(il2CppExecutor);
        }

        public void WriteJson(string outputDir)
        {
            File.WriteAllText(outputDir + "structs.json", JsonConvert.SerializeObject(index, Formatting.Indented), new UTF8Encoding(false));
        }
    }
}
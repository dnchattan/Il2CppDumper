using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppDumper
{
    public class Il2CppIndex
    {
        private Il2CppExecutor executor;
        private Metadata metadata;
        private Il2Cpp il2Cpp;
        private Config config;

        private readonly Dictionary<Il2CppTypeDefinition, TypeDefinitionMetadata> TypeMetadata = new Dictionary<Il2CppTypeDefinition, TypeDefinitionMetadata>();
        private readonly Dictionary<string, Il2CppType> lookupGenericType = new Dictionary<string, Il2CppType>();
        private readonly Dictionary<long, ulong> typeIndexToAddress = new Dictionary<long, ulong>();
        private readonly Dictionary<ulong, string> lookupGenericClassName = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> genericClassList = new HashSet<ulong>();

        public readonly Dictionary<string, List<StructStaticMethodInfo>> TypeNameToStaticMethods = new Dictionary<string, List<StructStaticMethodInfo>>();
        public List<Il2CppTypeDefinitionInfo> TypeInfoList = new List<Il2CppTypeDefinitionInfo>();

        private class UniqueName
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

        private class TypeDefinitionMetadata
        {
            private static UniqueName UniqueNames = new UniqueName();

            public TypeDefinitionMetadata(int imageIndex, int typeInfoIndex, string imageName, string ns, string typeName)
            {
                ImageIndex = imageIndex;
                TypeInfoIndex = typeInfoIndex;
                ImageName = imageName;
                Namespace = ns;
                UniqueName = UniqueNames.Get(typeName);
            }
            public int ImageIndex;
            public int TypeInfoIndex;
            public string ImageName;
            public string Namespace;
            public string UniqueName;
        }

        public Il2CppIndex(Il2CppExecutor il2CppExecutor, Config config)
        {
            this.config = config;
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
            // foreach (var ptr in genericClassList)
            // {
            //     var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(ptr);
            //     var typeDef = executor.GetGenericClassTypeDefinition(genericClass);
            //     var typeInfo = AddStruct(typeDef, TypeMetadata[typeDef], genericClass);
            //     typeInfo.IsGenericInstance = true;
            // }
        }

        private Il2CppTypeDefinitionInfo AddStruct(Il2CppTypeDefinition typeDef, TypeDefinitionMetadata metadata, Il2CppGenericClass genericClass = null)
        {
            var typeInfo = executor.GetTypeDefInfo(typeDef, genericClass);
            typeInfo.ImageName = metadata.ImageName;
            TypeInfoList.Add(typeInfo);
            return typeInfo;
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
                var (typeName, methodSpecMethodName, typeArgs) = executor.GetMethodSpecName(methodSpec, true);
                staticMethod.Name = methodSpecMethodName;
                staticMethod.TypeArgs = typeArgs;

                if (!TypeNameToStaticMethods.ContainsKey(typeName))
                {
                    TypeNameToStaticMethods.Add(typeName, new List<StructStaticMethodInfo>());
                }
                TypeNameToStaticMethods[typeName].Add(staticMethod);
            }
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
                    TypeMetadata.Add(typeDef, new TypeDefinitionMetadata(imageIndex, typeIndex, imageName, null, typeName));
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
                genericClassList.Add(il2CppType.data.generic_class);
                lookupGenericType[typeStructName] = il2CppType;
                lookupGenericClassName[il2CppType.data.generic_class] = typeStructName;
            }
        }
    }
}
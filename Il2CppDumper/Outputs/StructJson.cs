using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Il2CppDumper
{
    public class StructJsonGenerator
    {
        private Il2CppExecutor executor;
        private Metadata metadata;
        private Il2Cpp il2Cpp;
        private Il2CppIndex index;

        private Dictionary<Il2CppTypeDefinition, int> lookupTypeToImageIndex = new Dictionary<Il2CppTypeDefinition, int>();

        public StructJsonGenerator(Il2CppExecutor il2CppExecutor, Config config)
        {
            executor = il2CppExecutor;
            metadata = il2CppExecutor.metadata;
            il2Cpp = il2CppExecutor.il2Cpp;
            index = new Il2CppIndex(il2CppExecutor, config);
        }

        public void WriteJson(string outputDir)
        {
            File.WriteAllText(outputDir + "structs.json",
                JsonConvert.SerializeObject(index, Formatting.Indented, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }
            ), new UTF8Encoding(false));
        }
    }
}
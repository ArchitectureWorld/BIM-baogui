using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Hifc.Reference
{
    // Current input requirements are separate from the immutable 2026-10-20 evidence.
    public static class PlatformInputOverrides
    {
        public const string Revision="hbbim-420626-required-20261105";
        public const string Sha256="19f623d8d8309d13eb8b40e581f375813d3afd1aa35115f7898689213394d335";
        static readonly Lazy<Dictionary<string,object>> Sets=new Lazy<Dictionary<string,object>>(Load);
        static Dictionary<string,object> Load()
        {
            using(var stream=typeof(PlatformInputOverrides).Assembly.GetManifestResourceStream("Hifc.Reference.Platform420626.20261105.Inputs.json")) {
                if(stream==null)throw new InvalidOperationException("当前平台必填检查配置缺失。");
                byte[] bytes;using(var buffer=new MemoryStream()){stream.CopyTo(buffer);bytes=buffer.ToArray();}
                using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=Sha256)
                    throw new InvalidOperationException("当前平台必填检查配置与已核对证据不一致。");
                var root=(Dictionary<string,object>)new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.DeserializeObject(Encoding.UTF8.GetString(bytes));
                return (Dictionary<string,object>)root["property_sets"];
            }
        }
        internal static PlatformFieldRule[] Apply(string pset,PlatformFieldRule[] fields)
        {
            if(!Sets.Value.TryGetValue(pset,out var value))return fields;
            var group=(Dictionary<string,object>)value;var result=fields.ToList();
            foreach(var raw in (object[])group["fields"]) {
                var entry=(Dictionary<string,object>)raw;var name=(string)entry["name"];
                var field=result.FirstOrDefault(f=>f.Name==name);
                if(field==null){field=new PlatformFieldRule{Name=name,InternalName=entry["platform_internal_name"] as string??name,
                    Code=entry["platform_code"] as string,ShowProperty=true,DefaultValue=entry["platform_default"] as string};result.Add(field);}
                field.DisplayRequired=(bool)entry["required"];field.AllowNull=!field.DisplayRequired;
                field.Items=((object[])entry["choices"]).Cast<string>().ToArray();
            }
            return result.ToArray();
        }
    }
}

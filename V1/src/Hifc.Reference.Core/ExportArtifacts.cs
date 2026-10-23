using System;
using System.IO;
using System.Security.Cryptography;

namespace Hifc.Reference
{
    /// <summary>Internal export work files; publishes only a verified IFC.</summary>
    public sealed class ExportArtifacts
    {
        public string OutputPath { get; private set; }
        public string DirectoryPath { get; private set; }
        public string StagedIfcPath { get; private set; }
        public string ConfigurationPath { get; private set; }
        public string ReceiptPath { get; private set; }
        public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Hifc.V1","export-records");
        public static ExportArtifacts Create(string outputPath,string internalRoot,string exportId)
        {
            if(!Guid.TryParse(exportId,out var id))throw new ArgumentException("Invalid export identifier.");
            var path=Path.GetFullPath(outputPath);
            if(!string.Equals(Path.GetExtension(path),".ifc",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("导出文件扩展名应为 .ifc。");
            var directory=Path.Combine(Path.GetFullPath(internalRoot),id.ToString("D"));
            if(Directory.Exists(directory))throw new IOException("导出内部记录目录已存在。");
            Directory.CreateDirectory(directory);
            return new ExportArtifacts {OutputPath=path,DirectoryPath=directory,StagedIfcPath=Path.Combine(directory,Path.GetFileName(path)),
                ConfigurationPath=Path.Combine(directory,"mapping.psets.tsv"),ReceiptPath=Path.Combine(directory,"export.receipt.json")};
        }
        static string Hash(Stream stream)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        public void Publish(string expectedHash)
        {
            if(!File.Exists(ReceiptPath)||new FileInfo(ReceiptPath).Length==0)throw new InvalidOperationException("导出校验回执尚未完成，未生成交付 IFC。");
            using(var source=new FileStream(StagedIfcPath,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(source.Length==0||!string.Equals(Hash(source),expectedHash,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("内部 IFC 取值与成功回执不一致，未生成交付 IFC。");
                source.Position=0;Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));bool created=false;
                try {
                    // CreateNew also protects against another exporter publishing
                    // the same version between filename confirmation and this copy.
                    using(var target=new FileStream(OutputPath,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) {
                        created=true;source.CopyTo(target);target.Flush(true);target.Position=0;
                        if(!string.Equals(Hash(target),expectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("交付 IFC 校验失败。");
                    }
                }catch {
                    if(created)File.Delete(OutputPath); // only this invocation's new file
                    throw;
                }
            }
            // A failed cleanup can retain the private working copy, but cannot
            // invalidate the verified delivery. Never remove prior user outputs.
            try{File.Delete(StagedIfcPath);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}

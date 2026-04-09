using System;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GVFS.MSBuild
{
    public class CompileTemplatedFile : Task
    {
        [Required]
        public ITaskItem Template { get; set; }

        [Required]
        public string OutputFile { get; set; }

        [Output]
        public ITaskItem CompiledTemplate { get; set; }

        public override bool Execute()
        {
            string templateFilePath = this.Template.ItemSpec;
            IDictionary<string, string> properties = ParseProperties(this.Template.GetMetadata("Properties"));

            string outputFileDirectory = Path.GetDirectoryName(this.OutputFile);

            if (!File.Exists(templateFilePath))
            {
                this.Log.LogError("Failed to find template file '{0}'.", templateFilePath);
                return false;
            }

            // Copy the template to the destination to keep the same file mode bits/ACLs as the template
            File.Copy(templateFilePath, this.OutputFile, true);

            this.Log.LogMessage(MessageImportance.Low, "Reading template contents");
            string template = File.ReadAllText(this.OutputFile);

            this.Log.LogMessage(MessageImportance.Normal, "Compiling template '{0}'", templateFilePath);
            string compiled = Compile(template, properties);

            if (!Directory.Exists(outputFileDirectory))
            {
                this.Log.LogMessage(MessageImportance.Low, "Creating output directory '{0}'", outputFileDirectory);
                Directory.CreateDirectory(outputFileDirectory);
            }

            this.Log.LogMessage(MessageImportance.Normal, "Writing compiled template to '{0}'", this.OutputFile);
            File.WriteAllText(this.OutputFile, compiled);

            this.CompiledTemplate = new TaskItem(this.OutputFile, this.Template.CloneCustomMetadata());

            return true;
        }

        private IDictionary<string, string> ParseProperties(string propertiesStr)
        {
            string[] properties = propertiesStr?.Split(';') ?? new string[0];
            var dict = new Dictionary<string, string>();

            foreach (string propertyStr in properties)
            {
                string[] kvp = propertyStr.Split(new[] {'='}, count: 2);
                if (kvp.Length > 1)
                {
                    string key = kvp[0].Trim();
                    dict[key]  = kvp[1].Trim();
                }
            }

            return dict;
        }

        private string Compile(string template, IDictionary<string, string> properties)
        {
            var sb = new StringBuilder(template);

            foreach (var kvp in properties)
            {
                this.Log.LogMessage(MessageImportance.Low, "Replacing \"{0}\" -> \"{1}\"", kvp.Key, kvp.Value);
                sb.Replace(kvp.Key, kvp.Value);
            }

            return sb.ToString();
        }
    }
}

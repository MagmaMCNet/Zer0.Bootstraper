using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Bootstraper
{
    public static class BootstrapExtensions
    {
        public static BootstrapCompiler AddEmbeddedResources(this BootstrapCompiler self, string name, byte[] resourceData)
        {
            byte[] processedData = self.UseGZip ? BootstrapCompiler.CompressDataWithGZip(resourceData) : resourceData;
            self.AddResource(name, processedData);
            return self;
        }

        public static BootstrapCompiler SetMainExe(this BootstrapCompiler self, string mainExeName, byte[] data)
        {
            byte[] processedData = self.UseGZip ? BootstrapCompiler.CompressDataWithGZip(data) : data;
            self.AddResource(mainExeName, processedData, false);
            self._mainExeName = mainExeName;
            return self;
        }

        public static BootstrapCompiler SetIcon(this BootstrapCompiler self, string iconPath)
        {
            if (File.Exists(iconPath))
            {
                self._iconPath = iconPath;
            }
            else
            {
                throw new FileNotFoundException("Icon file not found.");
            }
            return self;
        }
    }

    public class BootstrapCompiler : IDisposable
    {
        internal readonly StringBuilder _resourcesCode = new StringBuilder();
        internal string _mainExeName;
        internal string _iconPath = null;
        private readonly ResourceWriter _resourceWriter;
        public bool UseGZip = true;
        private const string BOOTSTRAP_VERSION = "2.0.0.0";
        private const string Author = "ZER0, (MagmaMC)";
        private const string Description = "Application Built With Zer0's Bootstrap For [MainExe]";
        private const string Header =
@"using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Collections;
using Bootstraper;
using static Bootstraper.Bootstrap;

namespace [NAMESPACE]
{
    internal class [CLASS]
    {
";
        private const string Main =
@"
        [BootstrapGenerated]
        [CompilerGenerated]
        static void Main(string[] args)
        {
            Console.WriteLine(""Auto Generated Using Zer0's Bootstraper - V"+BOOTSTRAP_VERSION+@""");
";
        private string Footer => @"
        }
    }
}
namespace Bootstraper
{
    public static class Bootstrap
    {
        [BootstrapGenerated]
        [CompilerGenerated]
        public static string ExtractEmbeddedContent(string resourceName, string fileName)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            if (!File.Exists(tempPath))
            {
                using (var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(""EmbeddedResources""))
                {
                    if (resourceStream == null)
                        throw new ArgumentException(""EmbeddedResources file not found in embedded resources."");

                    using (ResourceReader reader = new ResourceReader(resourceStream))
                    {
                        bool resourceFound = false;

                        foreach (DictionaryEntry entry in reader)
                        {
                            if (entry.Key.ToString() == resourceName)
                            {
                                resourceFound = true;
                                var resourceData = (byte[])entry.Value;

                                using (var decompressedStream = new MemoryStream())
                                {
                                    using (var stream = new MemoryStream(resourceData))
                                    {
                                        Stream decompressionStream = " + (UseGZip ? "(Stream)new GZipStream(stream, CompressionMode.Decompress)" : "stream")+@";
                                        decompressionStream.CopyTo(decompressedStream);
                                    }
                                    File.WriteAllBytes(tempPath, decompressedStream.ToArray());
                                }
                                break;
                            }
                        }

                        if (!resourceFound)
                            throw new ArgumentException($""Resource '{resourceName}' not found in Bootstrap.resources."");
                    }
                }
            }
            AppDomain.CurrentDomain.ProcessExit += (_, __) => File.Delete(tempPath);
            return tempPath;
        }
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    sealed class BootstrapGeneratedAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    sealed class CompilerGeneratedAttribute : Attribute
    {
    }
}
";

        internal static string GetNamespace(string text)
        {
            text = Path.GetFileNameWithoutExtension(text).Replace("'", "").Replace(".", "").Replace(" ", ".");
            if (string.IsNullOrEmpty(text))
                return text;

            return char.ToUpper(text[0]) + text.Substring(1);
        }

        internal static string GetClass(string text)
        {
            text = Path.GetFileNameWithoutExtension(text).Replace(" ", "_").Replace("'", "").Replace(".", "");
            if (string.IsNullOrEmpty(text))
                return text;

            return char.ToLower(text[0]) + text.Substring(1);
        }

        internal static byte[] CompressDataWithGZip(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        private static readonly string ResourcesPath = Path.Combine(Path.GetTempPath(), $"Bootstraper.resources");

        private string GenerateCode()
        {
            var programContent = new StringBuilder();
            programContent.Append(Header.Replace("[NAMESPACE]", GetNamespace(_mainExeName)).Replace("[CLASS]", GetClass(_mainExeName)));
            programContent.Append(Main);
            programContent.AppendLine(_resourcesCode.ToString());
            programContent.AppendLine($"            Process.Start(ExtractEmbeddedContent(\"{GetInternalName(_mainExeName)}\", \"{_mainExeName}\")).WaitForExit();");
            programContent.Append(Footer);
            return programContent.ToString();
        }

        public BootstrapCompiler()
        {
            _resourceWriter = new ResourceWriter(ResourcesPath);
        }

        public void AddResource(string resourceName, byte[] resourceData, bool autoextract = true)
        {
            string internalName = GetInternalName(resourceName);
            _resourceWriter.AddResource(internalName, resourceData);
            if (autoextract)
                _resourcesCode.AppendLine($@"            ExtractEmbeddedContent(""{internalName}"", ""{resourceName}"");");
        }

        public bool Compile(string outputExe)
        {
            _resourceWriter.Generate();
            _resourceWriter.Close();

            var programCode = GenerateCode();
            File.WriteAllText(GetInternalName(_mainExeName) + ".cs", programCode);
            var syntaxTree = CSharpSyntaxTree.ParseText(programCode);
            var references = new[] {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Process).Assembly.Location)
            };

            var compilationOptions = new CSharpCompilationOptions(OutputKind.WindowsApplication,
                optimizationLevel: OptimizationLevel.Release
            );

            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(outputExe),
                new[] { syntaxTree },
                references,
                compilationOptions);

            compilation = compilation.WithOptions(compilation.Options.WithGeneralDiagnosticOption(ReportDiagnostic.Error));
            compilation = compilation.AddSyntaxTrees(GenerateAssemblyData());

            EmitResult result;
            try
            {
                using (var fs = new FileStream(outputExe, FileMode.Create))
                {
                    Stream iconStream = null;
                    if (!string.IsNullOrEmpty(_iconPath))
                    {
                        iconStream = File.OpenRead(_iconPath);
                    }

                    var win32Resources = compilation.CreateDefaultWin32Resources(
                        versionResource: true,
                        noManifest: false,
                        manifestContents: null,
                        iconInIcoFormat: iconStream
                    );
                    var resourceDescription = new ResourceDescription(
                                        "EmbeddedResources",
                                        () => File.OpenRead(ResourcesPath),
                                        true);

                    result = compilation.Emit(
                        peStream: fs,
                        win32Resources: win32Resources,
                        manifestResources: new[] { resourceDescription });

                    iconStream?.Dispose();
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("The file is not writable. Please check the file permissions.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (IOException ex)
            {
                MessageBox.Show($"An I/O error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                if (File.Exists(ResourcesPath))
                    File.Delete(ResourcesPath);
            }

            if (!result.Success)
            {
                foreach (var diagnostic in result.Diagnostics)
                    Console.WriteLine($"Error: {diagnostic.GetMessage()}");
                return false;
            }
            return true;
        }

        private SyntaxTree GenerateAssemblyData()
        {
            return CSharpSyntaxTree.ParseText($@"
using System.Reflection;
[assembly: AssemblyTitle(""{Path.GetFileNameWithoutExtension(_mainExeName)}"")]
[assembly: AssemblyDescription(""{Description.Replace("[MainExe]", _mainExeName)}"")]
[assembly: AssemblyConfiguration("""")]
[assembly: AssemblyCompany("""")]
[assembly: AssemblyProduct("""")]
[assembly: AssemblyCopyright(""Copyright © {Author}"")]
[assembly: AssemblyTrademark("""")]
[assembly: AssemblyCulture("""")]
[assembly: AssemblyVersion(""{BOOTSTRAP_VERSION}"")]
[assembly: AssemblyFileVersion(""{BOOTSTRAP_VERSION}"")]
");
        }

        public string GetInternalName(string resourceName)
        {
            return resourceName.Replace(".", "_").Replace("-", "_").Replace(" ", "_").Replace("'", "") + (UseGZip ? "_gzip" : "");
        }

        public void Dispose()
        {
            _resourceWriter?.Dispose();
        }
    }
}

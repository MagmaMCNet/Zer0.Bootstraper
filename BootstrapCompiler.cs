using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Bootstraper
{
    public static class BootstrapExtensions
    {
        public static BootstrapCompiler AddEmbeddedResource(this BootstrapCompiler self, string name, byte[] resourceData)
        {
            var processedData = resourceData;

            if (self.UseCompression)
                processedData = CompilerBase.EncodeWithDeflate(processedData);

            if (self.UseXOREncoding)
                processedData = CompilerBase.EncodeWithXOR(processedData);

            self.AddResource(name, new MemoryStream(processedData));
            return self;
        }


        public static BootstrapCompiler ProgramaticallyStart(this BootstrapCompiler self, string name)
        {

            self._resourcesCode.AppendLine($@"
            Task.Run(()  =>
            {{
                var sideProcess = Process.Start(new ProcessStartInfo()
                {{
                    UseShellExecute = true,
                    FileName = {self.GetInternalName(name)},
                    WorkingDirectory = Path.GetTempPath()
                }});
                AppDomain.CurrentDomain.ProcessExit += (_, __) =>
                {{
                    if (sideProcess != null && !sideProcess.HasExited)
                        sideProcess.Kill();
                }};
            }});");

            return self;
        }

        public static BootstrapCompiler SetMainExe(this BootstrapCompiler self, string mainExeName, byte[] data)
        {
            var processedData = data;

            if (self.UseCompression)
                processedData = CompilerBase.EncodeWithDeflate(processedData);

            if (self.UseXOREncoding)
                processedData = CompilerBase.EncodeWithXOR(processedData);

            self.AddResource(mainExeName, new MemoryStream(processedData), false);
            self._mainExeName = mainExeName;
            return self;
        }

        public static BootstrapCompiler SetIcon(this BootstrapCompiler self, string iconPath)
        {
            if (File.Exists(iconPath))
                self._iconPath = iconPath;
            else
                throw new FileNotFoundException("Icon file not found.");
            return self;
        }

        public static BootstrapCompiler EnableCompression(this BootstrapCompiler self, bool value)
        {
            self.UseCompression = value;
            return self;
        }

        public static BootstrapCompiler EnableXOREncoding(this BootstrapCompiler self, bool value)
        {
            self.UseXOREncoding = value;
            return self;
        }
    }

    public class BootstrapCompiler: CompilerBase
    {
        internal readonly StringBuilder _resourcesCode = new StringBuilder();
        internal readonly List<ResourceDescription> _resources = new List<ResourceDescription>();
        internal string _mainExeName;
        internal string _iconPath = null;

        internal bool UseXOREncoding = false;
        internal bool UseCompression = false;

        private const string Header =
@"

namespace [NAMESPACE]
{
    internal class [CLASS]
    {
";
        private const string Main =
@"
        [BootstrapGenerated]
        static void Main(string[] args)
        {
            Console.WriteLine(""Auto Generated Using Zer0's Bootstraper - V" + BOOTSTRAP_VERSION + @""");
";
        private string Footer => @"
        }
    }
}
";
        private string ImportBootstrap =>
@"
using Bootstraper;
using static Bootstraper.Bootstrap;";

        private string BootstraperLibrary =>
@"
namespace Bootstraper
{
    public static class Bootstrap
    {
        [BootstrapGenerated]
        [CompilerGenerated]
        public static string ExtractEmbeddedContent(string resourceName, string fileName)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            var assembly = Assembly.GetExecutingAssembly();
            var resourceFullName = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith(resourceName.Replace('.', '_')));

            if (resourceFullName == null)
                throw new ArgumentException($""Resource '{resourceName}' not found in embedded resources."");

            using (Stream resourceStream = assembly.GetManifestResourceStream(resourceFullName))
            {
                if (resourceStream == null)
                    throw new ArgumentException($""Embedded resource '{resourceName}' not found."");

                using (MemoryStream Data = new MemoryStream())
                {
                    resourceStream.CopyTo(Data);
" +
(UseXOREncoding ? @"
                    Data.Position = 0;
                    DecodeWithXOR(resourceStream, Data);
                    Data.Position = 0;
" : "") +
(UseCompression ? @"
                    Stream decompressionStream = new GZipStream(Data, CompressionMode.Decompress, leaveOpen: true);
                    Data.Position = 0;
                    decompressionStream.CopyTo(Data);
                    Data.Position = 0;
" : "") +
                  @"
                    try 
                    {
                        File.WriteAllBytes(tempPath, Data.ToArray());
                    } catch {}
                    AppDomain.CurrentDomain.ProcessExit += (_, __) => File.Delete(tempPath);
                    return tempPath;
                }
            }
        }
        private static void DecodeWithXOR(Stream input, Stream output)
        {
            byte[] key = { 0xAA, 0xBB, 0xCC };
            input.Position = 0;

            int keyLength = key.Length;
            int byteValue;
            int keyIndex = 0;

            while ((byteValue = input.ReadByte()) != -1)
            {
                byte decodedByte = (byte)(byteValue ^ key[keyIndex]);
                output.WriteByte(decodedByte);

                keyIndex = (keyIndex + 1) % keyLength;
            }

            output.Position = 0;
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
}";


        private string GenerateCode()
        {
            var programContent = new StringBuilder();
            programContent.Append(BaseLibraries);
            programContent.Append(ImportBootstrap);
            programContent.Append(Header.Replace("[NAMESPACE]", GetNamespace(_mainExeName)).Replace("[CLASS]", GetClass(_mainExeName)));
            programContent.Append(Main);
            programContent.AppendLine(_resourcesCode.ToString());
            programContent.AppendLine($"            Process.Start(ExtractEmbeddedContent(\"{GetInternalName(_mainExeName)}\", \"{_mainExeName}\")).WaitForExit();");
            programContent.Append(Footer);
            programContent.Append(BootstraperLibrary);

            return programContent.ToString();
        }

        public void AddResource(string resourceName, Stream resourceData, bool autoextract = true)
        {
            var internalName = GetInternalName(resourceName);
            if (autoextract)
                _resourcesCode.AppendLine($@"            string {internalName} = ExtractEmbeddedContent(""{internalName}"", ""{resourceName}"");");

            _resources.Add(new ResourceDescription(
                                internalName,
                                () => resourceData,
                                true));
        }

        public bool Compile(string outputExe)
        {
            var programCode = GenerateCode();
            if (Debugger.IsAttached)
                File.WriteAllText(GetInternalName(_mainExeName) + ".cs", programCode);

            var syntaxTree = CSharpSyntaxTree.ParseText(programCode);

            var frameworkPath = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2";

            var references = new[] {
                MetadataReference.CreateFromFile(Path.Combine(frameworkPath, "mscorlib.dll")),
                MetadataReference.CreateFromFile(Path.Combine(frameworkPath, "System.dll")),
                MetadataReference.CreateFromFile(Path.Combine(frameworkPath, "System.Core.dll")),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Process).Assembly.Location)
            };

            var compilationOptions = new CSharpCompilationOptions(OutputKind.WindowsApplication, optimizationLevel: OptimizationLevel.Release, checkOverflow: false);

            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(outputExe),
                new[] { syntaxTree },
                references,
                compilationOptions );
            compilation = compilation.AddSyntaxTrees(GenerateAssemblyData(_mainExeName, Description.Replace("[MainExe]", _mainExeName), Author, BOOTSTRAP_VERSION));

            EmitResult result;
            try
            {
                using (var fs = new FileStream(outputExe, FileMode.Create))
                {
                    Stream iconStream = null;
                    if (!string.IsNullOrEmpty(_iconPath))
                        iconStream = File.OpenRead(_iconPath);

                    var win32Resources = compilation.CreateDefaultWin32Resources(
                        versionResource: true,
                        noManifest: false,
                        manifestContents: null,
                        iconInIcoFormat: iconStream
                    );


                    result = compilation.Emit(
                        peStream: fs,
                        win32Resources: win32Resources,
                        manifestResources: _resources);

                    result = compilation.Emit(fs);

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

            if (!result.Success)
            {
                foreach (var diagnostic in result.Diagnostics)
                    Console.WriteLine($"Error: {diagnostic.GetMessage()}");
                return false;
            }
            return true;
        }

        public string GetInternalName(string resourceName)
        {
            return resourceName.Replace(".", "_").Replace("-", "_").Replace(" ", "_").Replace("'", "") + (UseXOREncoding ? "_deflate" : "");
        }

        protected const string BOOTSTRAP_VERSION = "2.0.0.0";
        protected const string Author = "ZER0, (MagmaMC)";
        protected const string Description = "Application Built With Zer0's Bootstrap For [MainExe]";
    }

    public class CompilerBase
    {
        public const string BaseLibraries =
@"using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Resources;
using System.Collections;
using System.Threading.Tasks;
using System.Linq;";

        public static SyntaxTree GenerateAssemblyData(string Exe, string Description, string Author, string Version)
        {
            return CSharpSyntaxTree.ParseText($@"
using System.Reflection;
[assembly: AssemblyTitle(""{Path.GetFileNameWithoutExtension(Exe)}"")]
[assembly: AssemblyDescription(""{Description}"")]
[assembly: AssemblyConfiguration("""")]
[assembly: AssemblyCompany(""{Author}"")]
[assembly: AssemblyProduct("""")]
[assembly: AssemblyCopyright(""Copyright © {Author}"")]
[assembly: AssemblyTrademark("""")]
[assembly: AssemblyCulture("""")]
[assembly: AssemblyVersion(""{Version}"")]
[assembly: AssemblyFileVersion(""{Version}"")]
");
        }

        public static string GetNamespace(string text)
        {
            text = Path.GetFileNameWithoutExtension(text).Replace("'", "").Replace(".", "").Replace(" ", ".");
            if (string.IsNullOrEmpty(text))
                return text;

            return char.ToUpper(text[0]) + text.Substring(1);
        }

        public static string GetClass(string text)
        {
            text = Path.GetFileNameWithoutExtension(text).Replace(" ", "_").Replace("'", "").Replace(".", "");
            if (string.IsNullOrEmpty(text))
                return text;

            return char.ToLower(text[0]) + text.Substring(1);
        }

        public static byte[] DecodeWithXOR(byte[] data)
        {
            byte[] key = { 0xAA, 0xBB, 0xCC };
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }
            return result;
        }
        public static byte[] EncodeWithXOR(byte[] data)
        {
            byte[] key = { 0xAA, 0xBB, 0xCC };
            byte[] result = new byte[data.Length];

            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }

            return result;
        }
        public static byte[] EncodeWithDeflate(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Data to compress cannot be null or empty.");
            }

            using (var output = new MemoryStream())
            {
                using (var deflateStream = new GZipStream(output, CompressionLevel.Optimal))
                {
                    deflateStream.Write(data, 0, data.Length);
                    deflateStream.Flush(); // Ensure all data is written to the output stream
                }
                return output.ToArray();
            }
        }
    }
}

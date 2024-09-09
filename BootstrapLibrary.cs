#define XOR
#define COMPRESSION
using System.IO;
using System.Reflection;
using System;
using System.Linq;
using System.IO.Compression;

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
                throw new ArgumentException($"Resource '{resourceName}' not found in embedded resources.");

            using (Stream resourceStream = assembly.GetManifestResourceStream(resourceFullName))
            {
                if (resourceStream == null)
                    throw new ArgumentException($"Embedded resource '{resourceName}' not found.");

                using (MemoryStream Data = new MemoryStream())
                {
                    resourceStream.CopyTo(Data);
                    Data.Position = 0;
#if XOR
                    using (MemoryStream decodedStream = new MemoryStream())
                    {
                        using (Stream xorDecodedStream = DecodeWithXOR(Data))
                        {
                            xorDecodedStream.CopyTo(decodedStream);
                        }
                        decodedStream.Position = 0;
                        decodedStream.CopyTo(Data);
                    }
#endif

#if COMPRESSION
                    Data.Position = 0;
                    using (MemoryStream decompressedStream = new MemoryStream())
                    {
                        using (Stream decompressionStream = new DeflateStream(Data, CompressionMode.Decompress))
                        {
                            decompressionStream.CopyTo(decompressedStream);
                        }
                        decompressedStream.Position = 0;
                        decompressedStream.CopyTo(Data);
                    }
#endif
                    File.WriteAllBytes(tempPath, Data.ToArray());
                    AppDomain.CurrentDomain.ProcessExit += (_, __) => File.Delete(tempPath);
                    return tempPath;
                }
            }
        }

        private static Stream DecodeWithXOR(Stream stream)
        {
            byte[] key = { 0xAA, 0xBB, 0xCC };
            int keyLength = key.Length;

            MemoryStream outputStream = new MemoryStream();

            int byteRead;
            int index = 0;

            while ((byteRead = stream.ReadByte()) != -1)
            {
                byte decodedByte = (byte)(byteRead ^ key[index % keyLength]);
                outputStream.WriteByte(decodedByte);
                index++;
            }

            outputStream.Position = 0;
            return outputStream;
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
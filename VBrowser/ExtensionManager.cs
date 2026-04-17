using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace VBrowser
{
    public static class ExtensionManager
    {
        public static async Task<string?> DownloadAndUnpackExtensionAsync(string extensionId)
        {
            try
            {
                var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VBrowser", "Extensions");
                Directory.CreateDirectory(appData);

                bool isEdge = extensionId.StartsWith("EDGE:");
                string realId = isEdge ? extensionId.Substring(5) : extensionId;

                var extractPath = Path.Combine(appData, realId);
                // If it already exists, let's assume it's installed or we might want to update it.
                // For simplicity, we just return the existing path.
                if (Directory.Exists(extractPath))
                {
                    return extractPath;
                }

                string url;
                if (isEdge)
                {
                    // Edge add-on download endpoint
                    url = $"https://edge.microsoft.com/extensionwebstorebase/v1/crx?os=win&arch=x64&os_arch=x86_64&nacl_arch=x86-64&prod=edgecrx&prodchannel=&prodversion=120.0.2210.121&lang=en-US&acceptformat=crx3&x=id%3D{realId}%26installsource%3Dondemand%26uc";
                }
                else
                {
                    // Google Chrome Web Store download endpoint
                    url = $"https://clients2.google.com/service/update2/crx?response=redirect&os=win&arch=x86-64&nacl_arch=x86-64&prod=chromecrx&prodchannel=unknown&prodversion=120.0.0.0&acceptformat=crx2,crx3&x=id%3D{realId}%26uc";
                }
                
                using var client = new HttpClient();
                // Send a generic Chrome user agent so Google lets us download it
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                
                using var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                using var crxStream = await response.Content.ReadAsStreamAsync();
                using var reader = new BinaryReader(crxStream);
                
                // Read the magic number to check if it's a CRX
                var magicBytes = reader.ReadChars(4);
                string magic = new string(magicBytes);
                if (magic != "Cr24")
                {
                    throw new Exception("Invalid CRX file format. Magic string was: " + magic);
                }

                uint version = reader.ReadUInt32();
                if (version == 2)
                {
                    uint pubKeyLength = reader.ReadUInt32();
                    uint sigLength = reader.ReadUInt32();
                    crxStream.Seek(pubKeyLength + sigLength, SeekOrigin.Current);
                }
                else if (version == 3)
                {
                    uint headerLength = reader.ReadUInt32();
                    crxStream.Seek(headerLength, SeekOrigin.Current);
                }
                else
                {
                    throw new Exception($"Unsupported CRX version: {version}");
                }

                // Generate a temporary zip file and copy the rest of the stream (which is a standard zip archive)
                string tempZip = Path.GetTempFileName();
                using (var fileStream = File.Create(tempZip))
                {
                    await crxStream.CopyToAsync(fileStream);
                }

                // Create the final extraction folder and unzip
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(tempZip, extractPath);
                
                // Cleanup temp file
                File.Delete(tempZip);

                return extractPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to install extension {extensionId}: {ex.Message}");
                return null;
            }
        }
    }
}

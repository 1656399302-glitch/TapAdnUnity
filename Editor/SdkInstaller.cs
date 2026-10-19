using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Networking;

namespace ZZWGameMaker.TapAdn.Editor
{
    [InitializeOnLoad]
    public static class SdkInstaller
    {
        private const string AttemptKey = "TapADN.InstallAttempted";
        private const string Marker = "tapadn-install.json";
        private static bool busy;
        public static string Status { get; private set; } = "Not checked";
        public static string VendorPath => Path.GetFullPath("Packages/" + SdkLock.VendorName);
        public static string PackageRoot => UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SdkInstaller).Assembly).resolvedPath;
        private static string CachePath => Path.GetFullPath("Library/TapAdn/" + SdkLock.Sha256 + ".unitypackage");

        static SdkInstaller() { EditorApplication.delayCall += AutoInstall; }
        private static async void AutoInstall()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += AutoInstall; return; }
            if (IsReady()) { Status = "Ready: " + SdkLock.Version; return; }
            // Batch jobs use InstallForBatchMode explicitly, allowing deterministic completion before quitting.
            if (Application.isBatchMode || SessionState.GetBool(AttemptKey, false)) return;
            SessionState.SetBool(AttemptKey, true);
            await InstallAsync(false);
        }

        public static bool IsReady()
        {
            try
            {
                var path = Path.Combine(VendorPath, Marker);
                if (!File.Exists(path)) return false;
                var state = JsonUtility.FromJson<InstallState>(File.ReadAllText(path));
                if (state.Sha256 != SdkLock.Sha256 || state.AdapterRevision != SdkLock.AdapterRevision || state.Files == null || state.Files.Length < 8) return false;
                foreach (var item in state.Files)
                {
                    var file = Path.GetFullPath(Path.Combine(VendorPath, item.Path));
                    if (!file.StartsWith(VendorPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(file) || Hash(File.ReadAllBytes(file)) != item.Sha256) return false;
                }
                return true;
            }
            catch { return false; }
        }

        public static async Task<bool> InstallAsync(bool redownload)
        {
            if (busy) return false;
            busy = true;
            try
            {
                EnsureIgnored();
                if (redownload && File.Exists(CachePath)) File.Delete(CachePath);
                if (!File.Exists(CachePath))
                {
                    Status = "Downloading official SDK " + SdkLock.Version;
                    using (var request = UnityWebRequest.Get(SdkLock.Url))
                    {
                        request.timeout = 30;
                        var operation = request.SendWebRequest();
                        while (!operation.isDone) await Task.Delay(100);
                        SaveDownload(request);
                    }
                }
                InstallArchive(File.ReadAllBytes(CachePath));
                Status = "Ready: " + SdkLock.Version;
                AssetDatabase.Refresh();
                Client.Resolve();
                return true;
            }
            catch (Exception error)
            {
                Status = "Installation failed: " + error.Message + " Retry in SDK Setup.";
                Debug.LogWarning("[TapADN] " + Status);
                return false;
            }
            finally { busy = false; }
        }

        // Unity -batchmode -executeMethod ZZWGameMaker.TapAdn.Editor.SdkInstaller.InstallForBatchMode -quit
        public static void InstallForBatchMode()
        {
            if (busy) throw new InvalidOperationException("Another TapADN installation is in progress.");
            EnsureIgnored();
            if (!IsReady())
            {
                if (!File.Exists(CachePath))
                {
                    using (var request = UnityWebRequest.Get(SdkLock.Url))
                    {
                        request.timeout = 30;
                        var operation = request.SendWebRequest();
                        while (!operation.isDone) Thread.Sleep(50);
                        SaveDownload(request);
                    }
                }
                InstallArchive(File.ReadAllBytes(CachePath));
            }
            AssetDatabase.Refresh();
            Client.Resolve();
            Debug.Log("[TapADN] Verified SDK installation ready: " + SdkLock.Version);
        }

        private static void SaveDownload(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success) throw new IOException("Official SDK download failed.");
            UnityPackageReader.Verify(request.downloadHandler.data);
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            File.WriteAllBytes(CachePath, request.downloadHandler.data);
        }

        public static void InstallArchive(byte[] archive)
        {
            UnityPackageReader.Verify(archive);
            var assets = UnityPackageReader.Read(archive);
            if (Directory.Exists(VendorPath) && !File.Exists(Path.Combine(VendorPath, Marker)))
                throw new IOException("The vendor directory is not owned by this installer; move it aside before installation.");
            var staging = Path.GetFullPath("Library/TapAdn/staging-" + Guid.NewGuid().ToString("N"));
            var backup = Path.GetFullPath("Library/TapAdn/previous-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                void Extract(string source, string destination)
                {
                    if (!assets.TryGetValue(source, out var bytes)) throw new InvalidDataException("The pinned SDK is missing " + source);
                    Write(staging, destination, bytes);
                }
                Extract("Assets/DirichletAd/Runtime/DirichletAdSdk.cs", "Runtime/DirichletAdSdk.cs");
                Extract("Assets/DirichletAd/Runtime/DirichletAdTypes.cs", "Runtime/DirichletAdTypes.cs");
                Extract("Assets/DirichletAd/Runtime/Dirichlet.Ad.Runtime.asmdef", "Runtime/Dirichlet.Ad.Runtime.asmdef");
                Extract("Assets/DirichletAd/Resources/DirichletSdkVersion.txt", "Runtime/Resources/DirichletSdkVersion.txt");
                Extract("Assets/Plugins/Android/DirichletAd/libs/dirichlet_ad_5.3.0.4.aar", "Runtime/Plugins/Android/dirichlet_ad_5.3.0.4.aar");
                Extract("Assets/Plugins/Android/DirichletAd/src/main/java/com/dirichlet/unity/TapAdUnityBridge.java", "Native~/TapAdUnityBridge.java");
                var javaPath = Path.Combine(staging, "Native~/TapAdUnityBridge.java");
                var java = File.ReadAllText(javaPath);
                const string oldController = "new UnityCustomController()";
                if (java.Split(new[] { oldController }, StringSplitOptions.None).Length != 2)
                    throw new InvalidDataException("Unexpected official Java bridge; privacy adaptation was not applied.");
                java = java.Replace(oldController, "new com.zzwgamemaker.tapadn.TapAdnPrivacy()");
                java = java.Replace("success.set(true);", "success.set(TapAdSdk.isInitialized());");
                File.WriteAllText(javaPath, java, new UTF8Encoding(false));
                Write(staging, "Native~/TapAdnPrivacy.java", File.ReadAllBytes(Path.Combine(PackageRoot, "Templates~/TapAdnPrivacy.java.txt")));
                Write(staging, "Adapter/OfficialBackend.cs", File.ReadAllBytes(Path.Combine(PackageRoot, "Templates~/OfficialBackend.cs.txt")));
                WriteText(staging, "Adapter/ZZWGameMaker.TapAdn.Vendor.asmdef", "{\"name\":\"ZZWGameMaker.TapAdn.Vendor\",\"references\":[\"ZZWGameMaker.TapAdn\",\"Dirichlet.Ad.Runtime\"]}");
                WriteText(staging, "package.json", "{\"name\":\"" + SdkLock.VendorName + "\",\"version\":\"" + SdkLock.VendorPackageVersion + "\",\"displayName\":\"TapADN Official SDK (locally generated)\",\"unity\":\"6000.0\"}");
                WriteText(staging, "THIRD_PARTY_NOTICES.md", "Downloaded locally from " + SdkLock.Url + "\nOfficial SDK remains subject to its own license and service terms.\nhttps://ssp.dirichlet.cn/docs/compliance/\nSHA256: " + SdkLock.Sha256 + "\nAdaptations: privacy controller, native initialization result, UPM layout, isolated rewarded adapter. No Demo or iOS sources imported.\n");
                WriteText(staging, "Runtime/Plugins/Android/dirichlet_ad_5.3.0.4.aar.meta", AarMeta);
                var state = new InstallState { Sha256 = SdkLock.Sha256, AdapterRevision = SdkLock.AdapterRevision,
                    Files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal)
                        .Select(f => new InstalledFile { Path = f.Substring(staging.Length + 1).Replace('\\', '/'), Sha256 = Hash(File.ReadAllBytes(f)) }).ToArray() };
                WriteText(staging, Marker, JsonUtility.ToJson(state, true));
                if (Directory.Exists(VendorPath)) Directory.Move(VendorPath, backup);
                try { Directory.Move(staging, VendorPath); }
                catch { if (Directory.Exists(backup)) Directory.Move(backup, VendorPath); throw; }
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }

        private static void EnsureIgnored()
        {
            // Local Git exclude preserves the user's tracked .gitignore; works for standalone projects and nested game folders.
            var current = new DirectoryInfo(Path.GetFullPath("."));
            while (current != null && !Directory.Exists(Path.Combine(current.FullName, ".git")) && !File.Exists(Path.Combine(current.FullName, ".git"))) current = current.Parent;
            if (current == null) return;
            var git = Path.Combine(current.FullName, ".git");
            if (!Directory.Exists(git))
            {
                var pointer = File.ReadAllText(git).Trim();
                if (!pointer.StartsWith("gitdir: ", StringComparison.Ordinal)) return;
                git = Path.GetFullPath(Path.Combine(current.FullName, pointer.Substring(8)));
                var common = Path.Combine(git, "commondir");
                if (File.Exists(common)) git = Path.GetFullPath(Path.Combine(git, File.ReadAllText(common).Trim()));
            }
            var relative = VendorPath.Substring(current.FullName.Length).Replace('\\', '/');
            var exclude = Path.Combine(git, "info/exclude");
            Directory.CreateDirectory(Path.GetDirectoryName(exclude));
            var text = File.Exists(exclude) ? File.ReadAllText(exclude) : "";
            if (!text.Split('\n').Contains(relative + "/")) File.AppendAllText(exclude, "\n# Generated official TapADN SDK\n" + relative + "/\n" + relative + ".meta\n");
            var local = Path.GetFullPath("Assets/TapAdnLocal").Substring(current.FullName.Length).Replace('\\', '/');
            if (!text.Split('\n').Contains(local + "/")) File.AppendAllText(exclude, local + "/\n" + local + ".meta\n");
        }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return UnityPackageReader.Hex(sha.ComputeHash(bytes)); }
        private static void Write(string root, string relative, byte[] bytes)
        { var file = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllBytes(file, bytes); }
        private static void WriteText(string root, string relative, string text) => Write(root, relative, new UTF8Encoding(false).GetBytes(text));
        [Serializable] private sealed class InstallState { public string Sha256; public int AdapterRevision; public InstalledFile[] Files; }
        [Serializable] private sealed class InstalledFile { public string Path; public string Sha256; }
        private const string AarMeta = "fileFormatVersion: 2\nguid: 791ada1e781942a79b8906435abca020\nPluginImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  iconMap: {}\n  executionOrder: {}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      Android: Android\n    second:\n      enabled: 1\n      settings: {}\n  - first:\n      Any: \n    second:\n      enabled: 0\n      settings: {}\n  - first:\n      Editor: Editor\n    second:\n      enabled: 0\n      settings: {}\n";
    }
}

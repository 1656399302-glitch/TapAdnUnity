using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using UnityEditor.Build;
using ZZWGameMaker.TapAdn.Editor;

namespace ZZWGameMaker.TapAdn.Tests
{
    public sealed class PackagingTests
    {
        [Test] public void GeneratedVendorVersionUsesSemver()
        { Assert.That(SdkLock.VendorPackageVersion, Does.Match(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")); }
        [Test] public void InvalidArchivePreservesExistingInstallation()
        {
            var marker = Path.Combine(SdkInstaller.VendorPath, "tapadn-install.json");
            var before = File.Exists(marker) ? File.ReadAllText(marker) : null;
            Assert.Throws<InvalidDataException>(() => SdkInstaller.InstallArchive(new byte[] { 1, 2, 3 }));
            Assert.That(File.Exists(marker) ? File.ReadAllText(marker) : null, Is.EqualTo(before));
        }
        [Test] public void CorruptedSdkIsRejected() => Assert.Throws<InvalidDataException>(() => UnityPackageReader.Verify(new byte[] { 1, 2, 3 }));
        [Test] public void OfficialSdkIsReadyAfterInstallation()
        {
            if (!Directory.Exists(SdkInstaller.VendorPath)) Assert.Ignore("Run SDK Setup first to exercise the installed SDK check.");
            Assert.That(SdkInstaller.IsReady(), Is.True);
        }
        [Test] public void ReaderMapsUnityPackageAssetEntries()
        {
            var assets = UnityPackageReader.Read(Archive("guid/pathname", "Assets/Test.txt", "guid/asset", "example"));
            Assert.That(Encoding.UTF8.GetString(assets["Assets/Test.txt"]), Is.EqualTo("example"));
        }
        [Test] public void ReaderRejectsTraversalPaths() => Assert.Throws<InvalidDataException>(() => UnityPackageReader.Read(Archive("../escape", "test")));
        [Test] public void ModuleDependenciesAreIdempotent()
        {
            const string source = "apply plugin: 'com.android.library'\ndependencies {\n    implementation files('libs/dirichlet_ad_5.3.0.4.aar')\n}\n";
            var once = TapAdnAndroidBuild.RewriteModuleGradle(source);
            Assert.That(TapAdnAndroidBuild.RewriteModuleGradle(once), Is.EqualTo(once));
            Assert.That(once.Split(new[] { "okhttp:3.12.1" }, StringSplitOptions.None).Length, Is.EqualTo(2));
        }
        [Test] public void ConflictingOkHttpFailsExplicitly()
        { Assert.Throws<BuildFailedException>(() => TapAdnAndroidBuild.RewriteModuleGradle("dependencies {\n implementation 'com.squareup.okhttp3:okhttp:4.12.0'\n}")); }
        [Test] public void SettingsInjectionIsIdempotent()
        {
            var once = TapAdnAndroidBuild.RewriteSettingsGradle("dependencyResolutionManagement { repositories { google() } }");
            Assert.That(TapAdnAndroidBuild.RewriteSettingsGradle(once), Is.EqualTo(once));
        }
        [Test] public void ManifestPreservesHostAndAddsSdkScopedRemoval()
        {
            const string source = "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"><application><activity android:name=\"HostActivity\" /></application><uses-permission android:name=\"android.permission.REQUEST_INSTALL_PACKAGES\" /></manifest>";
            var once = TapAdnAndroidBuild.RewriteManifest(source);
            Assert.That(once, Does.Contain("HostActivity")); Assert.That(once, Does.Contain("com.tapsdk.tapad"));
            Assert.That(once, Does.Contain("android.permission.INTERNET"));
            Assert.That(TapAdnAndroidBuild.RewriteManifest(once), Is.EqualTo(once));
            Assert.That(once, Does.Not.Contain("utf-16"));
        }
        private static byte[] Archive(params string[] entries)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
                {
                    for (int i = 0; i < entries.Length; i += 2)
                    {
                        var name = Encoding.ASCII.GetBytes(entries[i]); var content = Encoding.UTF8.GetBytes(entries[i + 1]);
                        var header = new byte[512]; Array.Copy(name, header, name.Length);
                        var length = Encoding.ASCII.GetBytes(Convert.ToString(content.Length, 8).PadLeft(11, '0'));
                        Array.Copy(length, 0, header, 124, length.Length); header[156] = (byte)'0';
                        gzip.Write(header, 0, header.Length); gzip.Write(content, 0, content.Length);
                        var padding = new byte[(512 - content.Length % 512) % 512]; gzip.Write(padding, 0, padding.Length);
                    }
                    gzip.Write(new byte[1024], 0, 1024);
                }
                return output.ToArray();
            }
        }
    }
}

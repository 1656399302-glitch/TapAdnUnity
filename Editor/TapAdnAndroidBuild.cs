using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace ZZWGameMaker.TapAdn.Editor
{
    public sealed class TapAdnAndroidBuild : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        private const string Start = "// TapADN managed dependencies begin";
        private const string End = "// TapADN managed dependencies end";
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ToolsNs = "http://schemas.android.com/tools";
        public int callbackOrder => 1000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android && !SdkInstaller.IsReady())
                throw new BuildFailedException("TapADN SDK is not verified. Run Tools/TapADN/SDK Setup before building.");
        }
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!SdkInstaller.IsReady()) throw new BuildFailedException("TapADN generated SDK was modified or is incomplete.");
            var libs = Path.Combine(path, "libs");
            if (!Directory.Exists(libs) || !File.Exists(Path.Combine(libs, "dirichlet_ad_5.3.0.4.aar")))
                throw new BuildFailedException("Unity did not import the verified Android TapADN AAR.");
            if (Directory.GetFiles(path, "*.aar", SearchOption.AllDirectories).Count(f =>
                Path.GetFileName(f).IndexOf("dirichlet", StringComparison.OrdinalIgnoreCase) >= 0) != 1)
                throw new BuildFailedException("Conflicting TapADN AARs detected; do not mix direct, mediation, or multiple SDK versions.");
            CopyJava(path, "TapAdUnityBridge.java", "com/dirichlet/unity");
            CopyJava(path, "TapAdnPrivacy.java", "com/zzwgamemaker/tapadn");
            var gradlePath = Path.Combine(path, "build.gradle");
            File.WriteAllText(gradlePath, RewriteModuleGradle(File.ReadAllText(gradlePath)));
            var settingsPath = Path.Combine(Directory.GetParent(path).FullName, "settings.gradle");
            File.WriteAllText(settingsPath, RewriteSettingsGradle(File.ReadAllText(settingsPath)));
            var manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
            File.WriteAllText(manifestPath, RewriteManifest(File.ReadAllText(manifestPath)));
            var proguardPath = Path.Combine(path, "proguard-unity.txt");
            var rules = File.Exists(proguardPath) ? File.ReadAllText(proguardPath) : "";
            const string keep = "-keep class com.dirichlet.unity.** { *; }\n-keep class com.zzwgamemaker.tapadn.** { *; }";
            if (!rules.Contains(keep)) File.WriteAllText(proguardPath, rules + "\n" + keep + "\n");
        }
        private static void CopyJava(string path, string name, string package)
        {
            var destination = Path.Combine(path, "src/main/java", package, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(Path.Combine(SdkInstaller.VendorPath, "Native~", name), destination, true);
        }
        public static string RewriteModuleGradle(string content)
        {
            content = Regex.Replace(content, "\\r?\\n" + Regex.Escape(Start) + "[\\s\\S]*?" + Regex.Escape(End) + "\\r?\\n?", "");
            var okhttp = Regex.Matches(content, @"com\.squareup\.okhttp3:okhttp:([^'""\s]+)");
            foreach (Match match in okhttp)
                if (match.Groups[1].Value != "3.12.1") throw new BuildFailedException("A conflicting OkHttp dependency requires review; TapADN pins 3.12.1.");
            var dependencies = "\n" + Start + "\n";
            if (okhttp.Count == 0) dependencies += "    implementation 'com.squareup.okhttp3:okhttp:3.12.1'\n";
            if (!content.Contains("dirichlet_ad_5.3.0.4")) dependencies += "    implementation files('libs/dirichlet_ad_5.3.0.4.aar')\n";
            dependencies += End + "\n";
            var block = new Regex(@"(?m)^\s*dependencies\s*\{");
            if (!block.IsMatch(content)) throw new BuildFailedException("Cannot locate Unity module dependencies block.");
            return block.Replace(content, m => m.Value + dependencies, 1);
        }
        public static string RewriteSettingsGradle(string content)
        {
            const string marker = "// TapADN Maven Central";
            if (content.Contains(marker)) return content;
            var block = new Regex(@"(dependencyResolutionManagement\s*\{[\s\S]*?repositories\s*\{)");
            if (!block.IsMatch(content)) throw new BuildFailedException("Cannot locate Unity 6 dependencyResolutionManagement repositories.");
            return block.Replace(content, m => m.Value + "\n        " + marker + "\n        mavenCentral()\n", 1);
        }
        public static string RewriteManifest(string content)
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(content);
            var manifest = document.DocumentElement;
            manifest.SetAttribute("xmlns:tools", ToolsNs);
            foreach (var permission in new[] { "android.permission.INTERNET", "android.permission.ACCESS_NETWORK_STATE" })
                Permission(document, permission, false);
            // Only remove declarations contributed by this SDK, not permissions owned by the host or another SDK.
            foreach (var permission in new[] { "android.permission.QUERY_ALL_PACKAGES", "android.permission.REQUEST_INSTALL_PACKAGES",
                "com.asus.msa.SupplementaryDID.ACCESS", "freemme.permission.msa" })
                Permission(document, permission, true);
            if (document.FirstChild is XmlDeclaration declaration) declaration.Encoding = "utf-8";
            return document.OuterXml;
        }
        private static void Permission(XmlDocument document, string name, bool remove)
        {
            var existing = document.DocumentElement.ChildNodes.Cast<XmlNode>().OfType<XmlElement>()
                .FirstOrDefault(n => n.Name == "uses-permission" && n.GetAttribute("name", AndroidNs) == name);
            if (existing != null && existing.GetAttribute("node", ToolsNs) != "remove") return;
            var permission = existing ?? document.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNs, name);
            if (remove)
            {
                permission.SetAttribute("node", ToolsNs, "remove");
                permission.SetAttribute("selector", ToolsNs, "com.tapsdk.tapad");
            }
            if (existing == null) document.DocumentElement.AppendChild(permission);
        }
    }
}

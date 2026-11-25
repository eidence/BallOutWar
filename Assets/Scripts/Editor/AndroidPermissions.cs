using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace BOW.Editor
{
    /// <summary>
    /// Adds the Android permissions LAN play needs to the generated manifest (TDD 8.3): reading Wi-Fi state and
    /// taking a multicast lock so discovery broadcasts arrive. INTERNET comes from Player Settings
    /// (Internet Access: Require).
    /// </summary>
    public sealed class AndroidPermissions : IPostGenerateGradleAndroidProject
    {
        private static readonly string[] Permissions =
        {
            "android.permission.ACCESS_WIFI_STATE",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.CHANGE_WIFI_MULTICAST_STATE",
            "android.permission.VIBRATE",
        };

        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 0;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath)) return;

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var manifest = doc.SelectSingleNode("/manifest");
            if (manifest == null) return;

            foreach (string permission in Permissions)
            {
                bool present = false;
                foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
                    if (node.Attributes?["android:name"]?.Value == permission) present = true;
                if (present) continue;
                var element = doc.CreateElement("uses-permission");
                element.SetAttribute("name", AndroidNs, permission);
                manifest.PrependChild(element);
            }
            doc.Save(manifestPath);
        }
    }
}

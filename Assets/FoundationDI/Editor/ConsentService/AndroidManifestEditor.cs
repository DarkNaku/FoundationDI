using System.Xml;

namespace DarkNaku.FoundationDI.Editor
{
    internal static class AndroidManifestEditor
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";

        internal static bool HasMetaData(string xml, string name)
        {
            var doc = Load(xml);
            return Find(doc, name) != null;
        }

        internal static string AddMetaData(string xml, string name, string value)
        {
            var doc = Load(xml);

            if (Find(doc, name) != null) return xml;

            var application = doc.SelectSingleNode("/manifest/application") as XmlElement;

            if (application == null)
            {
                application = doc.CreateElement("application");
                doc.DocumentElement.AppendChild(application);
            }

            var meta = doc.CreateElement("meta-data");
            meta.SetAttribute("name", AndroidNs, name);
            meta.SetAttribute("value", AndroidNs, value);
            application.AppendChild(meta);

            using var writer = new System.IO.StringWriter();
            doc.Save(writer);
            return writer.ToString();
        }

        private static XmlDocument Load(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }

        private static XmlNode Find(XmlDocument doc, string name)
        {
            foreach (XmlElement meta in doc.GetElementsByTagName("meta-data"))
            {
                if (meta.GetAttribute("name", AndroidNs) == name) return meta;
            }

            return null;
        }
    }
}

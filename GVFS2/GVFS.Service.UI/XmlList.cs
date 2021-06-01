using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace GVFS.Service.UI
{
    public class XmlList<T> : List<T>, IXmlSerializable where T : class
    {
        public XmlSchema GetSchema()
        {
            throw new NotImplementedException();
        }

        public void ReadXml(XmlReader reader)
        {
            throw new NotImplementedException();
        }

        public void WriteXml(XmlWriter writer)
        {
            XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
            ns.Add(string.Empty, string.Empty);
            foreach (T item in this)
            {
                XmlSerializer xml = new XmlSerializer(item.GetType());
                xml.Serialize(writer, item, ns);
            }
        }
    }
}

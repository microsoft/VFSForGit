using System.Xml.Serialization;

namespace GVFS.Service.UI.Data
{
    public class BindingData
    {
        [XmlAttribute("template")]
        public string Template { get; set; }

        [XmlAnyElement]
        public XmlList<BindingItem> Items { get; set; }
    }
}

using System.Xml.Serialization;

namespace GVFS.Service.UI.Data
{
    public class VisualData
    {
        [XmlElement("binding")]
        public BindingData Binding { get; set; }
    }
}

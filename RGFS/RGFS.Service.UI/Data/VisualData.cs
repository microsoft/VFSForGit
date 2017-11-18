using System.Xml.Serialization;

namespace RGFS.Service.UI.Data
{
    public class VisualData
    {
        [XmlElement("binding")]
        public BindingData Binding { get; set; }
    }
}

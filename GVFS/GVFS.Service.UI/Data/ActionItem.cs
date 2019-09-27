using System.Xml.Serialization;

namespace GVFS.Service.UI.Data
{
    [XmlRoot("action")]
    public class ActionItem
    {
        [XmlAttribute("content")]
        public string Content { get; set; }

        [XmlAttribute("arguments")]
        public string Arguments { get; set; }

        [XmlAttribute("activationtype")]
        public string ActivationType { get; set; }
    }
}